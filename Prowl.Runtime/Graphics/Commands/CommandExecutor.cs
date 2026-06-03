// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

/// <summary>
/// Walks the byte stream of a <see cref="CommandBuffer"/> and issues real GL calls.
/// One instance lives on <see cref="Graphics"/> and is reused across submits, so its
/// mirror of GL state is preserved between buffers (redundant binds are skipped).
///
/// <para>
/// The executor is the ONLY place in the engine that calls Silk.NET.OpenGL methods
/// outside of resource constructors. Adding a new command type means adding a switch
/// case here.
/// </para>
/// </summary>
internal sealed class CommandExecutor
{
    // ─────────────────────── Mirror state ───────────────────────
    //
    // GL state mirrors used to skip redundant binds. The executor is the sole GL
    // writer (all mutations route through CB opcodes), so these stay in sync as
    // long as we invalidate them when resources are disposed and when external
    // helpers that bypass the cache mutate the corresponding state (currently
    // only GraphicsBuffer.Bind for the ELEMENT_ARRAY_BUFFER / VAO 0 workaround).

    private GraphicsProgram? _boundProgram;

    // Cached GL handles 0 means "default" (backbuffer / no VAO).
    private uint _lastDrawFb;
    private uint _lastReadFb;
    private uint _lastBoundVAO;

    /// <summary>Invalidate the VAO bind cache. Called by GraphicsBuffer.Bind when
    /// it forces VAO 0 to safely manipulate ELEMENT_ARRAY_BUFFER state.</summary>
    internal void InvalidateBoundVAO() => _lastBoundVAO = 0;

    private RasterizerState _raster;
    private bool _rasterInitialized;

    // Sticky property bindings — applied per-draw onto the bound program.
    private PropertyState? _boundProperties;
    private PropertyState? _boundInstanceProperties;

    // When set, the executor also fills in shader defaults for any property the
    // material doesn't override. Set via SetMaterialProperties; cleared when
    // SetProperties or ClearProperties is encoded. We only need the Shader (not the
    // full Material) since the snapshotted properties already capture the overrides.
    private Resources.Shader? _boundShader;

    // Globals walk runs per draw GraphicsProgram.uniformCache dedupes the actual
    // uniform uploads, so the cost is just dictionary enumeration.

    // No cross-draw texture-unit cache. Slot assignment in PrepareDraw is dynamic
    // (depends on how many globals + material props are bound this draw), so a
    // cached "unit X = texture Y" mapping would desync within a single frame.

    // Per-uniform SetTexture opcodes can be encoded BEFORE the draw, but their
    // texture-slot allocation must wait until AFTER PrepareDraw has bound globals
    // and the material/instance property blocks otherwise they race for slot 0
    // and the global walk silently overwrites the per-uniform binding. Buffer them
    // here and flush at the tail of PrepareDraw.
    private readonly List<(string name, GraphicsTexture tex)> _pendingDirectTextures = new(8);

    // ─────────────────────── Entry point ───────────────────────

    public void Execute(CommandBuffer cmd)
    {
        var stream = cmd._stream.AsSpan(0, cmd._streamPos);
        var objects = cmd._objects;
        var store = cmd._store;
        int pos = 0;

        while (pos < stream.Length)
        {
            CommandOpcode op = ReadOpcode(stream, ref pos);
            switch (op)
            {
                case CommandOpcode.SetRenderTarget:
                {
                    var fb = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    ApplyRenderTarget(fb, fb);
                    break;
                }
                case CommandOpcode.SetRenderTargets:
                {
                    var draw = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    var read = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    ApplyRenderTarget(draw, read);
                    break;
                }
                case CommandOpcode.SetViewport:
                {
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    Graphics.GL.Viewport(x, y, w, h);
                    break;
                }
                case CommandOpcode.ClearRenderTarget:
                {
                    ClearFlags flags = (ClearFlags)ReadU8(stream, ref pos);
                    float r = ReadF32(stream, ref pos);
                    float g = ReadF32(stream, ref pos);
                    float b = ReadF32(stream, ref pos);
                    float a = ReadF32(stream, ref pos);
                    float depth = ReadF32(stream, ref pos);
                    int stencil = ReadI32(stream, ref pos);
                    DoClear(flags, r, g, b, a, depth, stencil);
                    break;
                }
                case CommandOpcode.BlitFramebuffer:
                {
                    int sx = ReadI32(stream, ref pos);
                    int sy = ReadI32(stream, ref pos);
                    int sw = ReadI32(stream, ref pos);
                    int sh = ReadI32(stream, ref pos);
                    int dx = ReadI32(stream, ref pos);
                    int dy = ReadI32(stream, ref pos);
                    int dw = ReadI32(stream, ref pos);
                    int dh = ReadI32(stream, ref pos);
                    ClearFlags mask = (ClearFlags)ReadU8(stream, ref pos);
                    BlitFilter filter = (BlitFilter)ReadU8(stream, ref pos);
                    DoBlit(sx, sy, sw, sh, dx, dy, dw, dh, mask, filter);
                    break;
                }
                case CommandOpcode.SetRasterState:
                {
                    // Eager apply: old Graphics.SetState pushed to GL synchronously,
                    // so a Clear after it saw the correct DepthMask. Lazy-apply would
                    // let Clear run with stale DepthMask between frames and silently
                    // skip the depth clear.
                    var next = ReadStruct<RasterizerState>(stream, ref pos);
                    RasterStateApply.Apply(in next);
                    _raster = next;
                    _rasterInitialized = true;
                    break;
                }
                case CommandOpcode.SetShader:
                {
                    var prog = (GraphicsProgram?)objects[ReadU16(stream, ref pos)];
                    BindProgram(prog);
                    // Pending per-uniform texture binds were meant for the previous
                    // shader's uniform layout drop them so they don't get applied to
                    // a different program at the next draw.
                    _pendingDirectTextures.Clear();
                    break;
                }
                case CommandOpcode.SetProperties:
                {
                    _boundProperties = (PropertyState?)objects[ReadU16(stream, ref pos)];
                    _boundShader = null;
                    break;
                }
                case CommandOpcode.SetMaterialProperties:
                {
                    _boundProperties = (PropertyState?)objects[ReadU16(stream, ref pos)];
                    _boundShader = (Resources.Shader?)objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.ClearProperties:
                {
                    _boundProperties = null;
                    _boundShader = null;
                    break;
                }
                case CommandOpcode.SetInstanceProperties:
                {
                    _boundInstanceProperties = (PropertyState?)objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.ClearInstanceProperties:
                {
                    _boundInstanceProperties = null;
                    break;
                }
                case CommandOpcode.SetGlobalTexture:
                {
                    // Direct dict write public PropertyState.SetGlobalTexture
                    // would recursively submit a CB from inside the executor.
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var tex = (Texture2D?)objects[ReadU16(stream, ref pos)];
                    if (tex != null) PropertyState.s_globalTextures[name] = tex;
                    else PropertyState.s_globalTextures.Remove(name);
                    break;
                }
                case CommandOpcode.ClearGlobalTexture:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalTextures.Remove(name);
                    break;
                }
                case CommandOpcode.SetGlobalInt:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalInts[name] = ReadI32(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalFloat:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalFloats[name] = ReadF32(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalVec2:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalVectors2[name] = ReadStruct<Float2>(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalVec3:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalVectors3[name] = ReadStruct<Float3>(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalVec4:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalVectors4[name] = ReadStruct<Float4>(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalColor:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalColors[name] = ReadStruct<Color>(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalMatrix:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    PropertyState.s_globalMatrices[name] = ReadStruct<Float4x4>(stream, ref pos);
                    break;
                }
                case CommandOpcode.SetGlobalMatrices:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var values = (Float4x4[])objects[ReadU16(stream, ref pos)]!;
                    PropertyState.SetGlobalMatricesInternal(name, values);
                    break;
                }
                case CommandOpcode.SetGlobalBuffer:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var buf = (GraphicsBuffer)objects[ReadU16(stream, ref pos)]!;
                    uint binding = ReadU32(stream, ref pos);
                    PropertyState.s_globalBuffers[name] = buf;
                    PropertyState.s_globalBufferBindings[name] = binding;
                    break;
                }
                case CommandOpcode.SetGlobalTexture3D:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var tex = (Texture3D?)objects[ReadU16(stream, ref pos)];
                    if (tex != null) PropertyState.s_globalTextures3D[name] = tex;
                    else PropertyState.s_globalTextures3D.Remove(name);
                    break;
                }
                case CommandOpcode.SetGlobalTextureCube:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var tex = (Cubemap?)objects[ReadU16(stream, ref pos)];
                    if (tex != null) PropertyState.s_globalTexturesCube[name] = tex;
                    else PropertyState.s_globalTexturesCube.Remove(name);
                    break;
                }
                case CommandOpcode.ClearAllGlobals:
                {
                    PropertyState.ClearGlobalsInternal();
                    break;
                }
                case CommandOpcode.SetUniformFloat:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    float v = ReadF32(stream, ref pos);
                    if (_boundProgram == null) break;
                    PropertyApply.SetFloatCached(_boundProgram, name, v);
                    break;
                }
                case CommandOpcode.SetUniformInt:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    int v = ReadI32(stream, ref pos);
                    if (_boundProgram == null) break;
                    PropertyApply.SetIntCached(_boundProgram, name, v);
                    break;
                }
                case CommandOpcode.SetUniformVec2:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    Float2 v = new(ReadF32(stream, ref pos), ReadF32(stream, ref pos));
                    if (_boundProgram == null) break;
                    PropertyApply.SetVec2Cached(_boundProgram, name, v);
                    break;
                }
                case CommandOpcode.SetUniformVec3:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    Float3 v = new(ReadF32(stream, ref pos), ReadF32(stream, ref pos), ReadF32(stream, ref pos));
                    if (_boundProgram == null) break;
                    PropertyApply.SetVec3Cached(_boundProgram, name, v);
                    break;
                }
                case CommandOpcode.SetUniformVec4:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    Float4 v = new(ReadF32(stream, ref pos), ReadF32(stream, ref pos), ReadF32(stream, ref pos), ReadF32(stream, ref pos));
                    if (_boundProgram == null) break;
                    PropertyApply.SetVec4Cached(_boundProgram, name, v);
                    break;
                }
                case CommandOpcode.SetUniformMatrix:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    Float4x4 m = ReadStruct<Float4x4>(stream, ref pos);
                    if (_boundProgram == null) break;
                    PropertyApply.SetMatrixCached(_boundProgram, name, in m);
                    break;
                }
                case CommandOpcode.SetUniformMatrixArray:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    uint count = ReadU32(stream, ref pos);
                    var blob = ReadBlob<Float4x4>(stream, ref pos, store);
                    if (_boundProgram == null) break;
                    PropertyApply.SetMatrixArray(_boundProgram, name, (uint)count, blob);
                    break;
                }
                case CommandOpcode.SetUniformTexture:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var tex = (GraphicsTexture?)objects[ReadU16(stream, ref pos)];
                    if (tex == null) break;
                    // Defer binding to PrepareDraw so the slot allocation happens
                    // after globals/material/instance have consumed their slots.
                    _pendingDirectTextures.Add((name, tex));
                    break;
                }
                case CommandOpcode.SetUniformBuffer:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    var buf = (GraphicsBuffer?)objects[ReadU16(stream, ref pos)];
                    uint binding = ReadU32(stream, ref pos);
                    if (_boundProgram == null || buf == null) break;
                    PropertyApply.BindUniformBuffer(_boundProgram, name, buf, binding);
                    break;
                }
                case CommandOpcode.UpdateBuffer:
                {
                    var buf = (GraphicsBuffer?)objects[ReadU16(stream, ref pos)];
                    uint dstOffset = ReadU32(stream, ref pos);
                    var blob = ReadBlob<byte>(stream, ref pos, store);
                    if (buf != null) DoUpdateBuffer(buf, dstOffset, blob);
                    break;
                }
                case CommandOpcode.UpdateTexture:
                {
                    var tex = (GraphicsTexture?)objects[ReadU16(stream, ref pos)];
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    int mip = ReadI32(stream, ref pos);
                    var blob = ReadBlob<byte>(stream, ref pos, store);
                    if (tex != null) DoUpdateTexture(tex, mip, x, y, w, h, blob);
                    break;
                }
                case CommandOpcode.GenerateMipmap:
                {
                    var tex = (GraphicsTexture?)objects[ReadU16(stream, ref pos)];
                    tex?.GenerateMipmap();
                    break;
                }
                case CommandOpcode.DrawIndexed:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topo = (Topology)ReadU8(stream, ref pos);
                    uint indexCount = ReadU32(stream, ref pos);
                    uint startIndex = ReadU32(stream, ref pos);
                    int baseVertex = ReadI32(stream, ref pos);
                    bool i32 = ReadU8(stream, ref pos) != 0;
                    DoDrawIndexed(vao, topo, indexCount, startIndex, baseVertex, i32);
                    break;
                }
                case CommandOpcode.DrawIndexedInstanced:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topo = (Topology)ReadU8(stream, ref pos);
                    uint indexCount = ReadU32(stream, ref pos);
                    uint instanceCount = ReadU32(stream, ref pos);
                    uint startIndex = ReadU32(stream, ref pos);
                    int baseVertex = ReadI32(stream, ref pos);
                    bool i32 = ReadU8(stream, ref pos) != 0;
                    DoDrawIndexedInstanced(vao, topo, indexCount, instanceCount, startIndex, baseVertex, i32);
                    break;
                }
                case CommandOpcode.DrawArrays:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topo = (Topology)ReadU8(stream, ref pos);
                    int first = ReadI32(stream, ref pos);
                    uint count = ReadU32(stream, ref pos);
                    DoDrawArrays(vao, topo, first, count);
                    break;
                }
                case CommandOpcode.CreateBuffer:
                {
                    var buf = (GraphicsBuffer)objects[ReadU16(stream, ref pos)]!;
                    bool dynamic = ReadU8(stream, ref pos) != 0;
                    var data = ReadBlob<byte>(stream, ref pos, store);
                    buf.Handle = Graphics.GL.GenBuffer();
                    if (data.Length > 0)
                    {
                        unsafe
                        {
                            fixed (byte* p = data)
                            {
                                // Set updates SizeInBytes too via Bind + glBufferData.
                                buf.Set((uint)data.Length, p, dynamic);
                            }
                        }
                    }
                    break;
                }
                case CommandOpcode.DisposeBuffer:
                {
                    var buf = (GraphicsBuffer)objects[ReadU16(stream, ref pos)]!;
                    if (buf.Handle != 0)
                    {
                        Graphics.GL.DeleteBuffer(buf.Handle);
                        buf.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.CreateTexture:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    tex.Handle = Graphics.GL.GenTexture();
                    break;
                }
                case CommandOpcode.DisposeTexture:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    if (tex.Handle != 0)
                    {
                        Graphics.GL.DeleteTexture(tex.Handle);
                        tex.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.AllocateTexture2D:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    int border = ReadI32(stream, ref pos);
                    var data = ReadBlob<byte>(stream, ref pos, store);
                    unsafe
                    {
                        if (data.Length > 0)
                        {
                            fixed (byte* p = data)
                                tex.TexImage2D(tex.Target, mip, w, h, border, p);
                        }
                        else
                        {
                            tex.TexImage2D(tex.Target, mip, w, h, border, null);
                        }
                    }
                    break;
                }
                case CommandOpcode.AllocateTextureCubeFace:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int face = ReadI32(stream, ref pos);
                    int mip = ReadI32(stream, ref pos);
                    uint size = ReadU32(stream, ref pos);
                    var data = ReadBlob<byte>(stream, ref pos, store);
                    var faceTarget = TextureTarget.TextureCubeMapPositiveX + face;
                    unsafe
                    {
                        if (data.Length > 0)
                        {
                            fixed (byte* p = data)
                                tex.TexImage2D(faceTarget, mip, size, size, 0, p);
                        }
                        else
                        {
                            tex.TexImage2D(faceTarget, mip, size, size, 0, null);
                        }
                    }
                    break;
                }
                case CommandOpcode.AllocateTexture3D:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    uint d = ReadU32(stream, ref pos);
                    var data = ReadBlob<byte>(stream, ref pos, store);
                    unsafe
                    {
                        if (data.Length > 0)
                        {
                            fixed (byte* p = data)
                                tex.TexImage3D(tex.Target, mip, w, h, d, p);
                        }
                        else
                        {
                            tex.TexImage3D(tex.Target, mip, w, h, d, null);
                        }
                    }
                    break;
                }
                case CommandOpcode.UpdateTexture3D:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    int z = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    uint d = ReadU32(stream, ref pos);
                    var data = ReadBlob<byte>(stream, ref pos, store);
                    if (data.Length == 0) break;
                    unsafe
                    {
                        fixed (byte* p = data)
                            tex.TexSubImage3D(tex.Target, mip, x, y, z, w, h, d, p);
                    }
                    break;
                }
                case CommandOpcode.SetTextureWrap:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    byte axis = ReadU8(stream, ref pos);
                    var mode = (TextureWrap)ReadU8(stream, ref pos);
                    switch (axis)
                    {
                        case 0: tex.SetWrapS(mode); break;
                        case 1: tex.SetWrapT(mode); break;
                        case 2: tex.SetWrapR(mode); break;
                    }
                    break;
                }
                case CommandOpcode.SetTextureFiltersOp:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    var min = (TextureMin)ReadU8(stream, ref pos);
                    var mag = (TextureMag)ReadU8(stream, ref pos);
                    tex.SetTextureFilters(min, mag);
                    break;
                }
                case CommandOpcode.SetTextureCompareMode:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    bool enabled = ReadU8(stream, ref pos) != 0;
                    tex.SetCompareMode(enabled);
                    break;
                }
                case CommandOpcode.GetTextureData:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    var destination = (byte[])objects[ReadU16(stream, ref pos)]!;
                    unsafe
                    {
                        fixed (byte* p = destination)
                            tex.GetTexImage(mip, p);
                    }
                    break;
                }
                case CommandOpcode.GetTextureCubeFaceData:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int face = ReadI32(stream, ref pos);
                    int mip = ReadI32(stream, ref pos);
                    var destination = (byte[])objects[ReadU16(stream, ref pos)]!;
                    unsafe
                    {
                        fixed (byte* p = destination)
                            tex.GetTexImageFace(face, mip, p);
                    }
                    break;
                }
                case CommandOpcode.GetTextureDataPtr:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    long raw = ReadStruct<long>(stream, ref pos);
                    unsafe { tex.GetTexImage(mip, (void*)(nint)raw); }
                    break;
                }
                case CommandOpcode.CreateVertexArrayOp:
                {
                    var vao = (GraphicsVertexArray)objects[ReadU16(stream, ref pos)]!;
                    vao.CreateGLObject();
                    // CreateGLObject binds the new VAO to configure it then leaves GL on
                    // VAO 0. Sync the mirror so the next BindVAO doesn't skip as redundant.
                    _lastBoundVAO = 0;
                    break;
                }
                case CommandOpcode.DisposeVertexArray:
                {
                    var vao = (GraphicsVertexArray)objects[ReadU16(stream, ref pos)]!;
                    if (vao.Handle != 0)
                    {
                        // GL implicitly unbinds a deleted VAO mirror that here.
                        if (_lastBoundVAO == vao.Handle) _lastBoundVAO = 0;
                        Graphics.GL.DeleteVertexArray(vao.Handle);
                        vao.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.CreateFramebufferOp:
                {
                    var fb = (GraphicsFrameBuffer)objects[ReadU16(stream, ref pos)]!;
                    fb.CreateGLObject();
                    // CreateGLObject binds the new FBO to attach textures then leaves GL on
                    // FBO 0. Sync the mirrors so the next ApplyRenderTarget doesn't skip.
                    _lastDrawFb = 0;
                    _lastReadFb = 0;
                    break;
                }
                case CommandOpcode.DisposeFramebuffer:
                {
                    var fb = (GraphicsFrameBuffer)objects[ReadU16(stream, ref pos)]!;
                    if (fb.Handle != 0)
                    {
                        if (_lastDrawFb == fb.Handle) _lastDrawFb = 0;
                        if (_lastReadFb == fb.Handle) _lastReadFb = 0;
                        Graphics.GL.DeleteFramebuffer(fb.Handle);
                        fb.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.CompileShader:
                {
                    var program = (GraphicsProgram)objects[ReadU16(stream, ref pos)]!;
                    program.CreateGLObject();
                    break;
                }
                case CommandOpcode.DisposeShader:
                {
                    var program = (GraphicsProgram)objects[ReadU16(stream, ref pos)]!;
                    if (program.Handle != 0)
                    {
                        if (GraphicsProgram.currentProgram != null && GraphicsProgram.currentProgram.Handle == program.Handle)
                            GraphicsProgram.currentProgram = null;
                        Graphics.GL.DeleteProgram(program.Handle);
                        program.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.BeginSample:
                {
                    string label = (string)objects[ReadU16(stream, ref pos)]!;
                    DoBeginSample(label);
                    break;
                }
                case CommandOpcode.EndSample:
                {
                    DoEndSample();
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown command opcode: {op}");
            }
        }
    }

    // ─────────────────────── Apply helpers ───────────────────────

    private void ApplyRenderTarget(GraphicsFrameBuffer? draw, GraphicsFrameBuffer? read)
    {
        uint dh = draw?.Handle ?? 0;
        uint rh = read?.Handle ?? 0;
        if (ReferenceEquals(draw, read))
        {
            if (dh != _lastDrawFb || dh != _lastReadFb)
            {
                Graphics.GL.BindFramebuffer(FramebufferTarget.Framebuffer, dh);
                _lastDrawFb = dh;
                _lastReadFb = dh;
            }
        }
        else
        {
            if (dh != _lastDrawFb)
            {
                Graphics.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, dh);
                _lastDrawFb = dh;
            }
            if (rh != _lastReadFb)
            {
                Graphics.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, rh);
                _lastReadFb = rh;
            }
        }

        // Setting a new render target implicitly resizes the viewport to the FB's
        // full size, otherwise a leftover viewport (e.g. a shadow atlas tile) would
        // clip draws into the new RT. cmd.SetViewport still overrides afterwards.
        if (draw != null)
            Graphics.GL.Viewport(0, 0, draw.Width, draw.Height);
    }

    private void BindProgram(GraphicsProgram? prog)
    {
        _boundProgram = prog;
        if (prog != null) prog.Use();
        else Graphics.GL.UseProgram(0);
    }

    private void BindVAO(GraphicsVertexArray? vao)
    {
        uint h = vao?.Handle ?? 0;
        if (h == _lastBoundVAO) return;
        Graphics.GL.BindVertexArray(h);
        _lastBoundVAO = h;
    }

    private void DoClear(ClearFlags flags, float r, float g, float b, float a, float depth, int stencil)
    {
        Graphics.GL.ClearColor(r, g, b, a);
        Graphics.GL.ClearDepth(depth);
        Graphics.GL.ClearStencil(stencil);

        ClearBufferMask mask = 0;
        if (flags.HasFlag(ClearFlags.Color)) mask |= ClearBufferMask.ColorBufferBit;
        if (flags.HasFlag(ClearFlags.Depth)) mask |= ClearBufferMask.DepthBufferBit;
        if (flags.HasFlag(ClearFlags.Stencil)) mask |= ClearBufferMask.StencilBufferBit;
        if (mask == 0) return;

        // glClear(DEPTH/STENCIL) is gated by the depth-write mask / stencil-write mask. If a
        // prior pass left writes disabled, the clear is silently skipped. Force the masks on
        // for the clear, then restore the cached raster state.
        bool clearDepth = flags.HasFlag(ClearFlags.Depth);
        bool clearStencil = flags.HasFlag(ClearFlags.Stencil);
        if (clearDepth) Graphics.GL.DepthMask(true);
        if (clearStencil) Graphics.GL.StencilMask(0xFF);

        Graphics.GL.Clear(mask);

        if (clearDepth) Graphics.GL.DepthMask(_raster.DepthWrite);
        if (clearStencil) Graphics.GL.StencilMask((uint)_raster.StencilWriteMask);
    }

    private void DoBlit(int sx, int sy, int sw, int sh, int dx, int dy, int dw, int dh,
                        ClearFlags mask, BlitFilter filter)
    {
        ClearBufferMask m = 0;
        if (mask.HasFlag(ClearFlags.Color)) m |= ClearBufferMask.ColorBufferBit;
        if (mask.HasFlag(ClearFlags.Depth)) m |= ClearBufferMask.DepthBufferBit;
        if (mask.HasFlag(ClearFlags.Stencil)) m |= ClearBufferMask.StencilBufferBit;

        BlitFramebufferFilter f = filter switch
        {
            BlitFilter.Linear => BlitFramebufferFilter.Linear,
            _ => BlitFramebufferFilter.Nearest,
        };
        Graphics.GL.BlitFramebuffer(sx, sy, sw, sh, dx, dy, dw, dh, m, f);
    }

    private void DoUpdateBuffer(GraphicsBuffer buf, uint dstOffset, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        // GraphicsBuffer.Bind() handles the ELEMENT_ARRAY_BUFFER / VAO 0 switch
        // and invalidates our _boundVAO mirror, so we don't need to do it here.
        unsafe
        {
            fixed (byte* p = data)
            {
                if (dstOffset == 0 && data.Length >= buf.SizeInBytes)
                    buf.Set((uint)data.Length, p, dynamic: true);
                else
                    buf.Update(dstOffset, (uint)data.Length, p);
            }
        }
    }

    private void DoUpdateTexture(GraphicsTexture tex, int mip, int x, int y, uint w, uint h, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        unsafe
        {
            fixed (byte* p = data)
            {
                tex.TexSubImage2D(tex.Target, mip, x, y, w, h, p);
            }
        }
    }

    private void DoDrawIndexed(GraphicsVertexArray? vao, Topology topo, uint indexCount,
                                uint startIndex, int baseVertex, bool i32)
    {
        if (vao == null) return;
        PrepareDraw();
        BindVAO(vao);

        PrimitiveType mode = ToGL(topo);
        DrawElementsType fmt = i32 ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;
        int indexSize = i32 ? sizeof(uint) : sizeof(ushort);

        unsafe
        {
            if (baseVertex == 0)
                Graphics.GL.DrawElements(mode, indexCount, fmt, (void*)(startIndex * indexSize));
            else
                Graphics.GL.DrawElementsBaseVertex(mode, indexCount, fmt, (void*)(startIndex * indexSize), baseVertex);
        }
    }

    private void DoDrawIndexedInstanced(GraphicsVertexArray? vao, Topology topo, uint indexCount,
                                         uint instanceCount, uint startIndex, int baseVertex, bool i32)
    {
        if (vao == null) return;
        PrepareDraw();
        BindVAO(vao);

        PrimitiveType mode = ToGL(topo);
        DrawElementsType fmt = i32 ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;
        int indexSize = i32 ? sizeof(uint) : sizeof(ushort);

        unsafe
        {
            // baseVertex variant is unused in the existing engine; ignore.
            _ = baseVertex;
            Graphics.GL.DrawElementsInstanced(mode, indexCount, fmt,
                (void*)(startIndex * indexSize), instanceCount);
        }
    }

    private void DoDrawArrays(GraphicsVertexArray? vao, Topology topo, int first, uint count)
    {
        if (vao == null) return;
        PrepareDraw();
        BindVAO(vao);
        Graphics.GL.DrawArrays(ToGL(topo), first, count);
    }

    /// <summary>Applies all pending state required to issue a draw call:
    /// raster state, global properties, material properties, instance properties.
    /// Texture slot counter resets each call.</summary>
    private void PrepareDraw()
    {
        // Raster state is applied eagerly when SetRasterState is encountered (see
        // that opcode handler). The only case where it might still be unset is the
        // very first draw of the application before any SetRasterState fired
        // push GL defaults so we're not reading uninitialised state.
        if (!_rasterInitialized)
        {
            RasterStateApply.Apply(in _raster);
            _rasterInitialized = true;
        }

        if (_boundProgram == null) return;

        _texSlotCounter = 0;

        // GlobalUniforms UBO must be bound on every draw shaders that read camera
        // matrices, time, screen size, etc. expect block 0 to hold this buffer.
        // Done here (rather than per encoding site) so the high-level cmd.DrawMesh /
        // cmd.Blit paths get it automatically; the low-level cmd.DrawIndexed path
        // in DrawRenderables also benefits without needing an explicit cmd.SetBuffer.
        // PropertyApply.BindUniformBuffer skips when the program doesn't declare the
        // block, so shaders that don't use it pay nothing.
        var globalBuf = Rendering.GlobalUniforms.GetBuffer();
        if (globalBuf != null)
            PropertyApply.BindUniformBuffer(_boundProgram, "GlobalUniforms", globalBuf, 0);

        // Global property block (PropertyState statics).
        PropertyApply.ApplyGlobals(_boundProgram, this);

        if (_boundProperties != null)
            PropertyApply.ApplyMaterial(_boundProperties, _boundProgram, this);

        // Shader defaults fill in anything the material didn't override.
        if (_boundShader != null && _boundShader.IsValid())
            PropertyApply.FillShaderDefaults(_boundShader, _boundProperties, _boundProgram, this);

        if (_boundInstanceProperties != null)
            PropertyApply.ApplyInstance(_boundInstanceProperties, _boundProgram, this);

        // Per-uniform SetTexture binds, flushed AFTER globals/material/instance so
        // they get fresh slots that don't collide with the bulk walks above. Cleared
        // after the draw so the next draw's per-uniform sets get their own slots.
        for (int i = 0; i < _pendingDirectTextures.Count; i++)
        {
            var (name, tex) = _pendingDirectTextures[i];
            int slot = AllocateTextureSlot();
            BindTextureToUnit(slot, tex);
            PropertyApply.SetIntCached(_boundProgram, name, slot);
        }
        _pendingDirectTextures.Clear();
    }

    // ─────────────────────── Texture slot management ───────────────────────

    private int _texSlotCounter;

    /// <summary>Allocate the next texture unit for a texture binding within a single draw.
    /// Reset to 0 at the start of <see cref="PrepareDraw"/>.</summary>
    internal int AllocateTextureSlot() => _texSlotCounter++;

    internal void BindTextureToUnit(int unit, GraphicsTexture tex)
    {
        Graphics.GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + unit));
        tex.Bind();
    }

    // Debug markers. Compiled out in release builds the per-call overhead
    // adds up across many CBs per frame.
    [System.Diagnostics.Conditional("DEBUG")]
    private void DoBeginSample(string label)
    {
        try { Graphics.GL.PushDebugGroup(DebugSource.DebugSourceApplication, 0, (uint)label.Length, label); } catch { }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void DoEndSample()
    {
        try { Graphics.GL.PopDebugGroup(); } catch { }
    }

    // ─────────────────────── Stream readers ───────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CommandOpcode ReadOpcode(ReadOnlySpan<byte> s, ref int pos)
    {
        var v = MemoryMarshal.Read<CommandOpcode>(s.Slice(pos));
        pos += sizeof(ushort);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ReadU8(ReadOnlySpan<byte> s, ref int pos) { byte v = s[pos]; pos += 1; return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<ushort>(s.Slice(pos)); pos += sizeof(ushort); return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadI32(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<int>(s.Slice(pos)); pos += sizeof(int); return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<uint>(s.Slice(pos)); pos += sizeof(uint); return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadF32(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<float>(s.Slice(pos)); pos += sizeof(float); return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadStruct<T>(ReadOnlySpan<byte> s, ref int pos) where T : unmanaged
    {
        var v = MemoryMarshal.Read<T>(s.Slice(pos));
        pos += Unsafe.SizeOf<T>();
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<T> ReadBlob<T>(ReadOnlySpan<byte> s, ref int pos, TransientStore store) where T : unmanaged
    {
        var r = ReadStruct<TransientStore.Ref>(s, ref pos);
        return store.Read<T>(r);
    }

    // ─────────────────────── Topology mapping ───────────────────────

    private static PrimitiveType ToGL(Topology t) => t switch
    {
        Topology.Points => PrimitiveType.Points,
        Topology.Lines => PrimitiveType.Lines,
        Topology.LineLoop => PrimitiveType.LineLoop,
        Topology.LineStrip => PrimitiveType.LineStrip,
        Topology.Triangles => PrimitiveType.Triangles,
        Topology.TriangleStrip => PrimitiveType.TriangleStrip,
        Topology.TriangleFan => PrimitiveType.TriangleFan,
        _ => PrimitiveType.Triangles,
    };
}
