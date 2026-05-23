// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Records a sequence of GPU commands without touching the GL context. Submit the
/// buffer through <see cref="Graphics.Submit"/> to enqueue it for the render thread.
/// Buffers are pooled; the typical pattern is:
///
/// <code>
///   using var cmd = Graphics.GetCommandBuffer("My Pass");
///   cmd.SetRenderTarget(rt);
///   cmd.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black);
///   cmd.DrawMesh(mesh, material);
///   Graphics.Submit(cmd);
/// </code>
///
/// <para>Single-producer: only one thread encodes a given buffer. CBs execute on the
/// render thread in submit order, so an upload CB can be safely followed by a CB
/// that uses the uploaded data.</para>
///
/// <para><see cref="SetProperties"/>, <see cref="SetInstanceProperties"/>, and
/// <see cref="SetMaterialProperties"/> all snapshot the PropertyState at encode
/// time, so the caller is free to mutate the original immediately after.</para>
/// </summary>
public sealed class CommandBuffer : IDisposable
{
    // --- Stream backing ---
    // _stream holds fixed-size payloads packed end-to-end after each CommandHeader.
    // Object refs are encoded as ushort indices into _objects.
    // Blobs (matrix arrays, upload data) are parked into _store.
    internal byte[] _stream;
    internal int _streamPos;

    internal readonly List<object?> _objects = new(64);
    internal readonly Dictionary<string, ushort> _nameMap = new(32);
    internal readonly TransientStore _store = new();

    // PropertyState snapshots rented from the pool for SetMaterialProperties (and
    // any other "capture material state at encode time" operations). Returned to
    // the pool wholesale on buffer reset.
    private readonly List<PropertyState> _rentedSnapshots = new(8);

    /// <summary>Optional name for debug markers and RenderDoc captures.</summary>
    public string? Name { get; set; }

    internal bool _submitted;

    /// <summary>Set by Submit, cleared only by OnRent (NOT by OnReturn). Lets Dispose
    /// stay correct even if the render thread has already executed and re-rented this
    /// buffer to another caller before our using-block fires.</summary>
    internal bool _ownerReleased;

    internal bool _inPool;

    internal CommandBuffer()
    {
        _stream = ArrayPool<byte>.Shared.Rent(4096);
    }

    // ─────────────────────── Pool lifecycle ───────────────────────

    /// <summary>Pool calls this when handing the buffer back to a user.</summary>
    internal void OnRent(string? name)
    {
        Name = name;
        _submitted = false;
        _inPool = false;
        _ownerReleased = false;
    }

    /// <summary>Pool calls this after execution to wipe the buffer for reuse.</summary>
    internal void OnReturn()
    {
        _streamPos = 0;
        _objects.Clear();
        _nameMap.Clear();
        _store.Reset();
        for (int i = 0; i < _rentedSnapshots.Count; i++)
            PropertyStatePool.Return(_rentedSnapshots[i]);
        _rentedSnapshots.Clear();
        Name = null;
        _submitted = false;
    }

    /// <summary>Final teardown when the pool drops the buffer entirely.</summary>
    internal void OnDestroy()
    {
        if (_stream != null)
        {
            ArrayPool<byte>.Shared.Return(_stream);
            _stream = null!;
        }
        _store.Dispose();
    }

    /// <summary>No-op if Submit was called (render thread returns the buffer).
    /// Returns to pool if the buffer was rented but never submitted.</summary>
    public void Dispose()
    {
        if (_ownerReleased) return;
        CommandBufferPool.Return(this);
    }

    // ─────────────────────── Render target / viewport / clear ───────────────────────

    /// <summary>Set the active draw + read framebuffer. null/default = backbuffer.</summary>
    public void SetRenderTarget(GraphicsFrameBuffer? fb)
    {
        WriteHeader(CommandOpcode.SetRenderTarget);
        Write(PushObject(fb));
    }

    /// <summary>Set draw and read framebuffers independently (used by blits).</summary>
    public void SetRenderTargets(GraphicsFrameBuffer? draw, GraphicsFrameBuffer? read)
    {
        WriteHeader(CommandOpcode.SetRenderTargets);
        Write(PushObject(draw));
        Write(PushObject(read));
    }

    public void SetViewport(int x, int y, uint w, uint h)
    {
        WriteHeader(CommandOpcode.SetViewport);
        Write(x); Write(y); Write(w); Write(h);
    }

    public void ClearRenderTarget(ClearFlags flags, Color color, float depth = 1f, int stencil = 0)
    {
        WriteHeader(CommandOpcode.ClearRenderTarget);
        Write((byte)flags);
        Write((float)color.R);
        Write((float)color.G);
        Write((float)color.B);
        Write((float)color.A);
        Write(depth);
        Write(stencil);
    }

    public void BlitFramebuffer(int srcX, int srcY, int srcWidth, int srcHeight,
                                 int dstX, int dstY, int dstWidth, int dstHeight,
                                 ClearFlags mask, BlitFilter filter)
    {
        WriteHeader(CommandOpcode.BlitFramebuffer);
        Write(srcX); Write(srcY); Write(srcWidth); Write(srcHeight);
        Write(dstX); Write(dstY); Write(dstWidth); Write(dstHeight);
        Write((byte)mask);
        Write((byte)filter);
    }

    // ─────────────────────── Pipeline state ───────────────────────

    public void SetRasterState(in RasterizerState state)
    {
        WriteHeader(CommandOpcode.SetRasterState);
        Write(in state);
    }

    public void SetShader(GraphicsProgram program)
    {
        WriteHeader(CommandOpcode.SetShader);
        Write(PushObject(program));
    }

    // ─────────────────────── Property binding ───────────────────────

    /// <summary>Bind a PropertyState as the active material property set. Snapshotted
    /// at encode time so the caller can mutate the original after this call.</summary>
    public void SetProperties(PropertyState properties)
    {
        var snapshot = PropertyStatePool.RentSnapshot(properties);
        _rentedSnapshots.Add(snapshot);
        WriteHeader(CommandOpcode.SetProperties);
        Write(PushObject(snapshot));
    }

    /// <summary>Bind a Material's properties plus its shader's defaults. The
    /// material's properties are snapshotted at encode time so callers can mutate
    /// the material between Blit calls without affecting earlier-encoded draws.</summary>
    public void SetMaterialProperties(Material material)
    {
        var snapshot = PropertyStatePool.RentSnapshot(material._properties);
        _rentedSnapshots.Add(snapshot);

        WriteHeader(CommandOpcode.SetMaterialProperties);
        Write(PushObject(snapshot));
        Write(PushObject(material.Shader));
    }

    /// <summary>Clear the bound material properties.</summary>
    public void ClearProperties()
    {
        WriteHeader(CommandOpcode.ClearProperties);
    }

    /// <summary>Per-instance PropertyState applied AFTER material properties so it
    /// can override them. Snapshotted at encode time.</summary>
    public void SetInstanceProperties(PropertyState properties)
    {
        var snapshot = PropertyStatePool.RentSnapshot(properties);
        _rentedSnapshots.Add(snapshot);
        WriteHeader(CommandOpcode.SetInstanceProperties);
        Write(PushObject(snapshot));
    }

    public void ClearInstanceProperties()
    {
        WriteHeader(CommandOpcode.ClearInstanceProperties);
    }

    /// <summary>Set a global texture at execute time. Ordered against draws in this CB.</summary>
    public void SetGlobalTexture(string name, Texture2D? tex)
    {
        WriteHeader(CommandOpcode.SetGlobalTexture);
        Write(InternName(name));
        Write(PushObject(tex));
    }

    public void ClearGlobalTexture(string name)
    {
        WriteHeader(CommandOpcode.ClearGlobalTexture);
        Write(InternName(name));
    }

    public void SetGlobalInt(string name, int value)
    {
        WriteHeader(CommandOpcode.SetGlobalInt);
        Write(InternName(name));
        Write(value);
    }

    public void SetGlobalFloat(string name, float value)
    {
        WriteHeader(CommandOpcode.SetGlobalFloat);
        Write(InternName(name));
        Write(value);
    }

    public void SetGlobalVector(string name, Float2 value)
    {
        WriteHeader(CommandOpcode.SetGlobalVec2);
        Write(InternName(name));
        Write(in value);
    }

    public void SetGlobalVector(string name, Float3 value)
    {
        WriteHeader(CommandOpcode.SetGlobalVec3);
        Write(InternName(name));
        Write(in value);
    }

    public void SetGlobalVector(string name, Float4 value)
    {
        WriteHeader(CommandOpcode.SetGlobalVec4);
        Write(InternName(name));
        Write(in value);
    }

    public void SetGlobalColor(string name, Color value)
    {
        WriteHeader(CommandOpcode.SetGlobalColor);
        Write(InternName(name));
        Write(in value);
    }

    public void SetGlobalMatrix(string name, Float4x4 value)
    {
        WriteHeader(CommandOpcode.SetGlobalMatrix);
        Write(InternName(name));
        Write(in value);
    }

    public void SetGlobalMatrices(string name, Float4x4[] values)
    {
        WriteHeader(CommandOpcode.SetGlobalMatrices);
        Write(InternName(name));
        Write(PushObject(values));
    }

    public void SetGlobalBuffer(string name, GraphicsBuffer buffer, uint bindingPoint)
    {
        WriteHeader(CommandOpcode.SetGlobalBuffer);
        Write(InternName(name));
        Write(PushObject(buffer));
        Write(bindingPoint);
    }

    public void SetGlobalTexture3D(string name, Texture3D? tex)
    {
        WriteHeader(CommandOpcode.SetGlobalTexture3D);
        Write(InternName(name));
        Write(PushObject(tex));
    }

    public void ClearAllGlobals()
    {
        WriteHeader(CommandOpcode.ClearAllGlobals);
    }

    // ─────────────────────── Per-uniform sugar ───────────────────────

    public void SetFloat(string name, float v)
    {
        WriteHeader(CommandOpcode.SetUniformFloat);
        Write(InternName(name));
        Write(v);
    }

    public void SetInt(string name, int v)
    {
        WriteHeader(CommandOpcode.SetUniformInt);
        Write(InternName(name));
        Write(v);
    }

    public void SetVector(string name, Float2 v)
    {
        WriteHeader(CommandOpcode.SetUniformVec2);
        Write(InternName(name));
        Write(v.X); Write(v.Y);
    }

    public void SetVector(string name, Float3 v)
    {
        WriteHeader(CommandOpcode.SetUniformVec3);
        Write(InternName(name));
        Write(v.X); Write(v.Y); Write(v.Z);
    }

    public void SetVector(string name, Float4 v)
    {
        WriteHeader(CommandOpcode.SetUniformVec4);
        Write(InternName(name));
        Write(v.X); Write(v.Y); Write(v.Z); Write(v.W);
    }

    public void SetColor(string name, Color c)
    {
        SetVector(name, new Float4((float)c.R, (float)c.G, (float)c.B, (float)c.A));
    }

    public void SetMatrix(string name, in Float4x4 m)
    {
        WriteHeader(CommandOpcode.SetUniformMatrix);
        Write(InternName(name));
        Write(in m);
    }

    public void SetMatrixArray(string name, ReadOnlySpan<Float4x4> ms)
    {
        WriteHeader(CommandOpcode.SetUniformMatrixArray);
        Write(InternName(name));
        Write((uint)ms.Length);
        var r = _store.Park(ms);
        Write(in r);
    }

    public void SetTexture(string name, Texture2D? tex)
    {
        WriteHeader(CommandOpcode.SetUniformTexture);
        Write(InternName(name));
        Write(PushObject(tex?.Handle));
    }

    public void SetTexture(string name, Texture3D? tex)
    {
        WriteHeader(CommandOpcode.SetUniformTexture);
        Write(InternName(name));
        Write(PushObject(tex?.Handle));
    }

    public void SetTexture(string name, GraphicsTexture? tex)
    {
        WriteHeader(CommandOpcode.SetUniformTexture);
        Write(InternName(name));
        Write(PushObject(tex));
    }

    public void SetBuffer(string name, GraphicsBuffer buf, uint bindingPoint = 0)
    {
        WriteHeader(CommandOpcode.SetUniformBuffer);
        Write(InternName(name));
        Write(PushObject(buf));
        Write(bindingPoint);
    }

    // ─────────────────────── Resource uploads ───────────────────────

    /// <summary>Upload <paramref name="data"/> into <paramref name="buf"/> at byte offset
    /// <paramref name="dstOffset"/>. The data is copied into the buffer's transient store
    /// at encode time, so the caller may release/reuse the source span immediately.</summary>
    public void UpdateBuffer<T>(GraphicsBuffer buf, ReadOnlySpan<T> data, uint dstOffset = 0) where T : unmanaged
    {
        WriteHeader(CommandOpcode.UpdateBuffer);
        Write(PushObject(buf));
        Write(dstOffset);
        var r = _store.Park(data);
        Write(in r);
    }

    /// <summary>Upload a 2D pixel rectangle into a texture's mip level.</summary>
    public void UpdateTexture<T>(Texture2D tex, int x, int y, uint w, uint h,
                                  ReadOnlySpan<T> data, int mip = 0) where T : unmanaged
    {
        WriteHeader(CommandOpcode.UpdateTexture);
        Write(PushObject(tex.Handle));
        Write(x); Write(y); Write(w); Write(h);
        Write(mip);
        var r = _store.Park(data);
        Write(in r);
    }

    public void GenerateMipmap(Texture2D tex)
    {
        WriteHeader(CommandOpcode.GenerateMipmap);
        Write(PushObject(tex.Handle));
    }

    public void GenerateMipmap(GraphicsTexture tex)
    {
        WriteHeader(CommandOpcode.GenerateMipmap);
        Write(PushObject(tex));
    }

    // ─────────────────────── Low-level draws ───────────────────────

    public void DrawIndexed(GraphicsVertexArray vao, Topology topo, uint indexCount,
                            uint startIndex = 0, int baseVertex = 0, bool index32bit = false)
    {
        WriteHeader(CommandOpcode.DrawIndexed);
        Write(PushObject(vao));
        Write((byte)topo);
        Write(indexCount);
        Write(startIndex);
        Write(baseVertex);
        Write(index32bit ? (byte)1 : (byte)0);
    }

    public void DrawIndexedInstanced(GraphicsVertexArray vao, Topology topo,
                                      uint indexCount, uint instanceCount,
                                      uint startIndex = 0, int baseVertex = 0,
                                      bool index32bit = false)
    {
        WriteHeader(CommandOpcode.DrawIndexedInstanced);
        Write(PushObject(vao));
        Write((byte)topo);
        Write(indexCount);
        Write(instanceCount);
        Write(startIndex);
        Write(baseVertex);
        Write(index32bit ? (byte)1 : (byte)0);
    }

    public void DrawArrays(GraphicsVertexArray vao, Topology topo, int first, uint count)
    {
        WriteHeader(CommandOpcode.DrawArrays);
        Write(PushObject(vao));
        Write((byte)topo);
        Write(first);
        Write(count);
    }

    // ─────────────────────── High-level draws (encoder sugar) ───────────────────────

    /// <summary>Encode a single mesh draw. Sets the mesh-attribute keywords on
    /// <paramref name="material"/>, fetches the variant program, binds shader +
    /// raster state + properties, then issues the draw. The mesh must already be
    /// uploaded (call <c>mesh.Upload()</c> first; CB-friendly).</summary>
    public void DrawMesh(Mesh mesh, Material material, int passIndex = 0,
                         in Float4x4 model = default,
                         PropertyState? instanceProperties = null,
                         int subMeshIndex = -1)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (material == null) throw new ArgumentNullException(nameof(material));

        mesh.Upload();
        if (mesh.VertexArrayObject == null || mesh.VertexCount <= 0) return;

        // Mesh-attribute keywords (mirrors RenderPipeline.DrawMeshNow today).
        material.SetKeyword("HAS_NORMALS", mesh.HasNormals);
        material.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
        material.SetKeyword("HAS_UV", mesh.HasUV);
        material.SetKeyword("HAS_UV2", mesh.HasUV2);
        material.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
        material.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
        material.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);
        material.SetKeyword("SKINNED", mesh.HasBoneIndices && mesh.HasBoneWeights);

        var pass = material.Shader.GetPass(passIndex);
        if (!pass.TryGetVariantProgram(material._localKeywords, out GraphicsProgram? variant) || variant == null)
            return;

        SetShader(variant);
        SetRasterState(pass.State);
        SetMaterialProperties(material);

        if (instanceProperties != null)
            SetInstanceProperties(instanceProperties);
        else
            ClearInstanceProperties();

        // Default(Float4x4) is all-zero; treat as "no model override".
        Float4x4 zero = default;
        bool hasModel = !model.Equals(zero);
        if (hasModel)
        {
            SetMatrix("prowl_ObjectToWorld", in model);
            Float4x4 inv = model.Invert();
            SetMatrix("prowl_WorldToObject", in inv);
        }

        bool index32 = mesh.IndexFormat == IndexFormat.UInt32;
        if (subMeshIndex >= 0 && subMeshIndex < mesh.SubMeshCount)
        {
            var sub = mesh.GetSubMesh(subMeshIndex);
            DrawIndexed(mesh.VertexArrayObject, sub.Topology, (uint)sub.IndexCount, (uint)sub.IndexStart, 0, index32);
        }
        else
        {
            DrawIndexed(mesh.VertexArrayObject, mesh.MeshTopology, (uint)mesh.IndexCount, 0, 0, index32);
        }
    }

    /// <summary>Blit <paramref name="source"/> to <paramref name="destination"/>. If
    /// <paramref name="material"/> is null a default copy is performed. Null destination
    /// means the backbuffer.</summary>
    public void Blit(RenderTexture? source, RenderTexture? destination,
                     Material? material = null, int pass = 0,
                     bool clearDepth = false, bool clearColor = false, Color color = default)
    {
        material ??= BlitDefaults.GetBlitMaterial();
        if (source != null)
            material.SetTexture("_MainTex", source.MainTexture);

        if (destination != null && destination.IsValid())
        {
            SetRenderTarget(destination.frameBuffer);
            SetViewport(0, 0, (uint)destination.Width, (uint)destination.Height);
        }
        else
        {
            SetRenderTarget(null);
            // Backbuffer size lives on the Window. Callers that need a different
            // viewport must set it explicitly.
            var fb = Window.InternalWindow.FramebufferSize;
            SetViewport(0, 0, (uint)fb.X, (uint)fb.Y);
        }

        if (clearDepth || clearColor)
        {
            ClearFlags clear = 0;
            if (clearDepth) clear |= ClearFlags.Depth;
            if (clearColor) clear |= ClearFlags.Color;
            ClearRenderTarget(clear | ClearFlags.Stencil, color);
        }

        DrawMesh(Mesh.GetFullscreenQuad(), material, pass);
    }

    public void Blit(Texture2D source, RenderTexture destination,
                     Material? material = null, int pass = 0)
    {
        material ??= BlitDefaults.GetBlitMaterial();
        material.SetTexture("_MainTex", source);
        SetRenderTarget(destination.frameBuffer);
        SetViewport(0, 0, (uint)destination.Width, (uint)destination.Height);
        DrawMesh(Mesh.GetFullscreenQuad(), material, pass);
    }

    /// <summary>Render a fullscreen quad with <paramref name="material"/> into
    /// <paramref name="destination"/>. Used by image effects whose material samples
    /// their own already-bound textures (no _MainTex copy from a source RT).</summary>
    public void Blit(RenderTexture destination, Material material, int pass = 0)
    {
        SetRenderTarget(destination.frameBuffer);
        SetViewport(0, 0, (uint)destination.Width, (uint)destination.Height);
        DrawMesh(Mesh.GetFullscreenQuad(), material, pass);
    }

    /// <summary>Fullscreen blit of <paramref name="material"/> into the currently bound
    /// render target. The caller is responsible for binding the target first.</summary>
    public void Blit(Material material, int pass = 0)
    {
        DrawMesh(Mesh.GetFullscreenQuad(), material, pass);
    }

    // ─────────────────────── Debug markers ───────────────────────

    public void BeginSample(string label)
    {
        WriteHeader(CommandOpcode.BeginSample);
        Write(PushObject(label));
    }

    // ─────────────────────── Internal resource-lifecycle encoders ───────────────────────
    //
    // These are called from resource constructors / Dispose paths. They allow the
    // resource classes to never touch GL directly all GL state mutation routes
    // through the executor, which makes moving execution to a render thread (Step 2)
    // a focused change to Graphics.Submit rather than rewriting every constructor.

    internal void EncodeCreateBuffer(GraphicsBuffer buf, bool dynamic, ReadOnlySpan<byte> data)
    {
        WriteHeader(CommandOpcode.CreateBuffer);
        Write(PushObject(buf));
        Write((byte)(dynamic ? 1 : 0));
        var r = _store.Park(data);
        Write(in r);
    }

    internal void EncodeDisposeBuffer(GraphicsBuffer buf)
    {
        WriteHeader(CommandOpcode.DisposeBuffer);
        Write(PushObject(buf));
    }

    internal void EncodeCreateTexture(GraphicsTexture tex)
    {
        WriteHeader(CommandOpcode.CreateTexture);
        Write(PushObject(tex));
    }

    internal void EncodeDisposeTexture(GraphicsTexture tex)
    {
        WriteHeader(CommandOpcode.DisposeTexture);
        Write(PushObject(tex));
    }

    /// <summary>Allocate storage for a 2D texture. Data may be empty to allocate
    /// uninitialised storage (e.g. for render-target attachments or texture creation
    /// followed by sub-image uploads).</summary>
    internal void EncodeAllocateTexture2D(GraphicsTexture tex, int mip, uint width, uint height, int border, ReadOnlySpan<byte> data)
    {
        WriteHeader(CommandOpcode.AllocateTexture2D);
        Write(PushObject(tex));
        Write(mip);
        Write(width);
        Write(height);
        Write(border);
        var r = _store.Park(data);
        Write(in r);
    }

    internal void EncodeAllocateTexture3D(GraphicsTexture tex, int mip, uint width, uint height, uint depth, ReadOnlySpan<byte> data)
    {
        WriteHeader(CommandOpcode.AllocateTexture3D);
        Write(PushObject(tex));
        Write(mip);
        Write(width);
        Write(height);
        Write(depth);
        var r = _store.Park(data);
        Write(in r);
    }

    /// <summary>Upload a 2D pixel rect into a texture's mip level. Uses the same
    /// opcode as the public <see cref="UpdateTexture{T}(Texture2D, int, int, uint, uint, ReadOnlySpan{T}, int)"/>
    /// but takes a raw GraphicsTexture invoked by Graphics.TexSubImage2D and other
    /// facade paths that don't have a Texture2D wrapper handy.</summary>
    internal void EncodeUpdateTexture2D(GraphicsTexture tex, int mip, int x, int y, uint width, uint height, ReadOnlySpan<byte> data)
    {
        WriteHeader(CommandOpcode.UpdateTexture);
        Write(PushObject(tex));
        Write(x); Write(y); Write(width); Write(height);
        Write(mip);
        var r = _store.Park(data);
        Write(in r);
    }

    /// <summary>Upload a 3D pixel volume into a texture's mip level.</summary>
    internal void EncodeUpdateTexture3D(GraphicsTexture tex, int mip, int x, int y, int z, uint width, uint height, uint depth, ReadOnlySpan<byte> data)
    {
        WriteHeader(CommandOpcode.UpdateTexture3D);
        Write(PushObject(tex));
        Write(mip);
        Write(x); Write(y); Write(z);
        Write(width); Write(height); Write(depth);
        var r = _store.Park(data);
        Write(in r);
    }

    internal void EncodeSetTextureWrap(GraphicsTexture tex, byte axis, TextureWrap mode)
    {
        WriteHeader(CommandOpcode.SetTextureWrap);
        Write(PushObject(tex));
        Write(axis);
        Write((byte)mode);
    }

    internal void EncodeSetTextureFilters(GraphicsTexture tex, TextureMin min, TextureMag mag)
    {
        WriteHeader(CommandOpcode.SetTextureFiltersOp);
        Write(PushObject(tex));
        Write((byte)min);
        Write((byte)mag);
    }

    /// <summary>Read a texture's pixels back. Caller MUST use SubmitAndWait the
    /// destination buffer is only filled when the op runs on the render thread.</summary>
    internal void EncodeGetTextureData(GraphicsTexture tex, int mip, byte[] destination)
    {
        WriteHeader(CommandOpcode.GetTextureData);
        Write(PushObject(tex));
        Write(mip);
        Write(PushObject(destination));
    }

    /// <summary>Pointer-destination variant. Caller MUST use SubmitAndWait the
    /// pointer has to stay valid until the render thread runs the op.</summary>
    internal void EncodeGetTextureDataPtr(GraphicsTexture tex, int mip, nint destination)
    {
        WriteHeader(CommandOpcode.GetTextureDataPtr);
        Write(PushObject(tex));
        Write(mip);
        Write((long)destination);
    }

    /// <summary>Defers GL setup to the executor, which calls the wrapper's
    /// <c>CreateGLObject()</c>. Same pattern for VAO / Framebuffer / Program.</summary>
    internal void EncodeCreateVertexArray(GraphicsVertexArray vao)
    {
        WriteHeader(CommandOpcode.CreateVertexArrayOp);
        Write(PushObject(vao));
    }

    internal void EncodeDisposeVertexArray(GraphicsVertexArray vao)
    {
        WriteHeader(CommandOpcode.DisposeVertexArray);
        Write(PushObject(vao));
    }

    internal void EncodeCreateFramebuffer(GraphicsFrameBuffer fb)
    {
        WriteHeader(CommandOpcode.CreateFramebufferOp);
        Write(PushObject(fb));
    }

    internal void EncodeDisposeFramebuffer(GraphicsFrameBuffer fb)
    {
        WriteHeader(CommandOpcode.DisposeFramebuffer);
        Write(PushObject(fb));
    }

    internal void EncodeCompileShader(GraphicsProgram program)
    {
        WriteHeader(CommandOpcode.CompileShader);
        Write(PushObject(program));
    }

    internal void EncodeDisposeShader(GraphicsProgram program)
    {
        WriteHeader(CommandOpcode.DisposeShader);
        Write(PushObject(program));
    }

    public void EndSample()
    {
        WriteHeader(CommandOpcode.EndSample);
    }

    /// <summary>Start a render-thread stopwatch. Elapsed time between this and
    /// EndTimer accumulates into <see cref="Rendering.GpuTimer.LastMs"/>.</summary>
    public void BeginTimer(Rendering.GpuTimer timer)
    {
        WriteHeader(CommandOpcode.BeginTimer);
        Write(PushObject(timer));
    }

    public void EndTimer(Rendering.GpuTimer timer)
    {
        WriteHeader(CommandOpcode.EndTimer);
        Write(PushObject(timer));
    }

    // ─────────────────────── Internal: stream writers ───────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteHeader(CommandOpcode op)
    {
        // _inPool (not _submitted): OnReturn resets _submitted to false, so
        // checking it wouldn't catch the common "encoded after Submit" misuse.
        if (_inPool) throw new InvalidOperationException("CommandBuffer has been returned to the pool; encoding is closed. Did you encode after Graphics.Submit?");
        EnsureCapacity(sizeof(ushort));
        MemoryMarshal.Write(_stream.AsSpan(_streamPos), in op);
        _streamPos += sizeof(ushort);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write<T>(T value) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        EnsureCapacity(size);
        MemoryMarshal.Write(_stream.AsSpan(_streamPos), in value);
        _streamPos += size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write<T>(in T value) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        EnsureCapacity(size);
        MemoryMarshal.Write(_stream.AsSpan(_streamPos), in value);
        _streamPos += size;
    }

    private void EnsureCapacity(int extra)
    {
        if (_streamPos + extra <= _stream.Length) return;
        int newSize = Math.Max(_stream.Length * 2, _streamPos + extra);
        byte[] next = ArrayPool<byte>.Shared.Rent(newSize);
        Buffer.BlockCopy(_stream, 0, next, 0, _streamPos);
        ArrayPool<byte>.Shared.Return(_stream);
        _stream = next;
    }

    private ushort PushObject(object? obj)
    {
        int idx = _objects.Count;
        if (idx > ushort.MaxValue)
            throw new InvalidOperationException("CommandBuffer object reference table exceeded 65k entries.");
        _objects.Add(obj);
        return (ushort)idx;
    }

    private ushort InternName(string name)
    {
        if (_nameMap.TryGetValue(name, out ushort idx)) return idx;
        idx = PushObject(name);
        _nameMap[name] = idx;
        return idx;
    }
}

/// <summary>Lazy holder for the default blit material so CommandBuffer doesn't depend
/// on RenderPipeline's static state at class-load time.</summary>
internal static class BlitDefaults
{
    private static Shader? s_blitShader;
    private static Material? s_blitMaterial;

    public static Material GetBlitMaterial()
    {
        if (s_blitShader.IsNotValid())
            s_blitShader = Shader.LoadDefault(DefaultShader.Blit);
        if (s_blitMaterial.IsNotValid())
            s_blitMaterial = new Material(s_blitShader);
        return s_blitMaterial!;
    }
}
