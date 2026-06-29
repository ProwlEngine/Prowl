// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Graphite;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GraphiteIndexFormat = Prowl.Graphite.IndexFormat;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Bridges Prowl's render pipeline onto Graphite's <see cref="CommandBuffer"/> + <see cref="PropertySet"/>.
/// </summary>
public static class RenderCommandExtensions
{
    /// <summary>
    /// Per-submesh variant: exposes the mesh's vertex streams but overrides the index count so
    /// <c>DrawIndexed</c> only covers the slice defined by <paramref name="indexStart"/> and
    /// <paramref name="indexCount"/>. The topology is taken from the submesh descriptor.
    /// </summary>
    public readonly struct SubMeshVertexSource(Mesh mesh, int indexStart, int indexCount, PrimitiveTopology topology) : IVertexSource
    {
        private readonly uint _indexCount = (uint)indexCount;
        private readonly PrimitiveTopology _topology = topology;

        public PrimitiveTopology Topology => _topology;

        public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
            => ((IVertexSource)mesh).ResolveSlot(layoutSlot, layout, out binding);

        public bool TryGetIndexBuffer(out DeviceBuffer buffer, out GraphiteIndexFormat format, out uint indexCount)
        {
            bool has = ((IVertexSource)mesh).TryGetIndexBuffer(out buffer, out format, out _);
            indexCount = _indexCount;
            return has;
        }
    }

    /// <summary>
    /// Two-slot vertex source for GPU instancing. Slot 0 provides per-vertex mesh data;
    /// any slot with <c>InstanceStepRate &gt; 0</c> is fulfilled by the per-instance buffer.
    /// </summary>
    public readonly struct InstancedMeshVertexSource(Mesh mesh, DeviceBuffer instanceBuffer) : IVertexSource
    {
        public PrimitiveTopology Topology => ((IVertexSource)mesh).Topology;

        public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        {
            if (layout.InstanceStepRate > 0)
                binding = new VertexBinding(instanceBuffer);
            else
                ((IVertexSource)mesh).ResolveSlot(layoutSlot, layout, out binding);
        }

        public bool TryGetIndexBuffer(out DeviceBuffer buffer, out GraphiteIndexFormat format, out uint indexCount)
            => ((IVertexSource)mesh).TryGetIndexBuffer(out buffer, out format, out indexCount);
    }

    /// <summary>
    /// Two-slot instanced source that also restricts the index range to a single submesh slice.
    /// </summary>
    public readonly struct InstancedSubMeshVertexSource(Mesh mesh, DeviceBuffer instanceBuffer, int indexStart, int indexCount, PrimitiveTopology topology) : IVertexSource
    {
        private readonly uint _indexCount = (uint)indexCount;
        private readonly PrimitiveTopology _topology = topology;

        public PrimitiveTopology Topology => _topology;

        public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        {
            if (layout.InstanceStepRate > 0)
                binding = new VertexBinding(instanceBuffer);
            else
                ((IVertexSource)mesh).ResolveSlot(layoutSlot, layout, out binding);
        }

        public bool TryGetIndexBuffer(out DeviceBuffer buffer, out GraphiteIndexFormat format, out uint indexCount)
        {
            bool has = ((IVertexSource)mesh).TryGetIndexBuffer(out buffer, out format, out _);
            indexCount = _indexCount;
            return has;
        }
    }

    private readonly struct FullscreenVertexSource : IVertexSource
    {
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        {
            if (layout.Elements[0].Name == s_positionAttr)
                binding = new VertexBinding(GetFullscreenPositionBuffer());
            else
                binding = new VertexBinding(GetFullscreenUVBuffer());
        }

        public bool TryGetIndexBuffer(out DeviceBuffer buffer, out GraphiteIndexFormat format, out uint indexCount)
        {
            buffer = null!;
            format = default;
            indexCount = 0;
            return false;
        }
    }

    private static readonly VertexAttributeID s_positionAttr = "POSITION0";

    private static DeviceBuffer? s_fullscreenPositionBuffer;
    private static DeviceBuffer GetFullscreenPositionBuffer()
    {
        if (s_fullscreenPositionBuffer != null) return s_fullscreenPositionBuffer;
        s_fullscreenPositionBuffer = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = BufferUsage.VertexBuffer,
            SizeInBytes = 3 * 3 * sizeof(float),
        });
        float[] verts = [-1f, -1f, 0f, 3f, -1f, 0f, -1f, 3f, 0f];
        Graphics.Device.UpdateBuffer(s_fullscreenPositionBuffer, 0u, verts);
        return s_fullscreenPositionBuffer;
    }

    private static DeviceBuffer? s_fullscreenUVBuffer;
    private static DeviceBuffer GetFullscreenUVBuffer()
    {
        if (s_fullscreenUVBuffer != null) return s_fullscreenUVBuffer;
        s_fullscreenUVBuffer = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = BufferUsage.VertexBuffer,
            SizeInBytes = 3 * 2 * sizeof(float),
        });
        float[] uvs = [0f, 0f, 2f, 0f, 0f, 2f];
        Graphics.Device.UpdateBuffer(s_fullscreenUVBuffer, 0u, uvs);
        return s_fullscreenUVBuffer;
    }

    private static readonly FullscreenVertexSource s_fullscreenSource = new();

    // ─────────────────────── PropertySet convenience (old PropertyState names) ───────────────────────

    public static void SetColor(this PropertySet set, PropertyID name, Color color) => set.SetFloat4(name, color);
    public static void SetVector(this PropertySet set, PropertyID name, Float2 value) => set.SetFloat2(name, value);
    public static void SetVector(this PropertySet set, PropertyID name, Float3 value) => set.SetFloat3(name, value);
    public static void SetVector(this PropertySet set, PropertyID name, Float4 value) => set.SetFloat4(name, value);

    public static void SetTexture(this PropertySet set, PropertyID name, Texture2D? texture)
    {
        if (texture?.Handle != null)
            set.SetTexture(name, texture.Handle, texture.Sampler);
    }

    public static void SetTexture(this PropertySet set, PropertyID name, AssetRef<Texture2D> texture)
        => set.SetTexture(name, texture.Res);

    // ─────────────────────── Material / per-object properties ───────────────────────

    public static void SetMaterialProperties(this CommandBuffer cmd, Material material)
    {
        PropertySet set = material.BuildPropertySet();
        if (set != null)
            cmd.SetProperties(set);
    }

    // ─────────────────────── Targets / viewport / scissor ───────────────────────

    // Tracked per-encode-thread so BlitFramebuffer can find the read/draw texture handles.
    [System.ThreadStatic]
    private static Framebuffer? s_readFramebuffer;

    [System.ThreadStatic]
    private static Framebuffer? s_drawFramebuffer;

    public static void SetRenderTarget(this CommandBuffer cmd, Framebuffer? framebuffer)
    {
        s_drawFramebuffer = framebuffer ?? Graphics.Device.SwapchainFramebuffer;
        cmd.SetFramebuffer(s_drawFramebuffer!);
    }

    public static void ClearRenderTarget(this CommandBuffer cmd, bool clearColor, bool clearDepthStencil, Color color)
    {
        if (clearColor)
            cmd.ClearColorTarget(0, color);
        if (clearDepthStencil)
            cmd.ClearDepthStencil(1f, 0);
    }

    /// <summary>
    /// Binds <paramref name="color"/> as the draw target and records <paramref name="extra"/> as
    /// the read source for the next <see cref="BlitFramebuffer"/> call.
    /// </summary>
    public static void SetRenderTargets(this CommandBuffer cmd, Framebuffer color, Framebuffer extra)
    {
        s_readFramebuffer = extra;
        s_drawFramebuffer = color;
        cmd.SetFramebuffer(color);
    }

    public static void SetViewport(this CommandBuffer cmd, int x, int y, uint width, uint height)
    {
        cmd.SetViewport(0, new Viewport(x, y, width, height, 0f, 1f));
    }

    // ─────────────────────── Blits ───────────────────────

    /// <summary>
    /// Copies the depth and/or color attachments of the <see cref="s_readFramebuffer"/> (set by
    /// <see cref="SetRenderTargets"/>) into the matching attachments of <see cref="s_drawFramebuffer"/>.
    /// Maps the old GL <c>glBlitFramebuffer</c> semantic onto <c>cmd.CopyTexture</c>.
    /// </summary>
    public static void BlitFramebuffer(this CommandBuffer cmd, bool copyColor, bool copyDepth)
    {
        Framebuffer? src = s_readFramebuffer;
        Framebuffer? dst = s_drawFramebuffer;
        if (src == null || dst == null)
            return;

        if (copyColor
            && src.DepthTarget.HasValue && dst.DepthTarget.HasValue)
        {
            cmd.CopyTexture(src.DepthTarget.Value.Target, dst.DepthTarget.Value.Target);
        }

        if (copyDepth)
        {
            int count = System.Math.Min(src.ColorTargets.Count, dst.ColorTargets.Count);
            for (int i = 0; i < count; i++)
                cmd.CopyTexture(src.ColorTargets[i].Target, dst.ColorTargets[i].Target);
        }
    }

    /// <summary>
    /// Performs a fullscreen material blit from <paramref name="source"/> into
    /// <paramref name="destination"/>. Uses the default Blit material when <paramref name="material"/>
    /// is null, and binds <c>source.MainTexture</c> as <c>_MainTex</c>.
    /// </summary>
    public static void Blit(this CommandBuffer cmd, RenderTexture source, RenderTexture destination, Material? material = null, int pass = 0, bool restoreTarget = false, bool clear = false)
    {
        Material mat = material ?? RenderPipeline.GetBlitMaterial();
        Shader? shader = mat.Shader;
        if (shader == null) return;

        ShaderPass? shaderPass = shader.GetPass(pass);
        if (shaderPass == null) return;

        shaderPass.SetKeywords(Enumerable.ToArray(mat._localKeywords.Values));
        GraphicsProgram? program = shaderPass.ActiveProgram;
        if (program == null) return;

        Framebuffer? destFb = destination?.IsValid() == true ? destination.frameBuffer : null;
        cmd.SetRenderTarget(destFb);
        if (clear)
            cmd.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, 0f));

        PropertySet props = mat.BuildPropertySet();
        if (source?.IsValid() == true && source.MainTexture?.Handle != null)
            props.SetTexture("_MainTex", source.MainTexture.Handle, source.MainTexture.Sampler);

        cmd.SetShader(program);
        cmd.SetVertexSource(s_fullscreenSource);
        cmd.SetProperties(props);
        cmd.Draw(3, 1, 0, 0);

        if (restoreTarget && s_drawFramebuffer != null)
            cmd.SetFramebuffer(s_drawFramebuffer);
    }

    /// <summary>
    /// Performs a fullscreen material blit into <paramref name="destination"/>. The source texture
    /// must already be bound on <paramref name="material"/> (e.g. as <c>_MainTex</c>).
    /// </summary>
    public static void Blit(this CommandBuffer cmd, RenderTexture destination, Material? material, int pass = 0)
    {
        Material mat = material ?? RenderPipeline.GetBlitMaterial();
        Shader? shader = mat.Shader;
        if (shader == null) return;

        ShaderPass? shaderPass = shader.GetPass(pass);
        if (shaderPass == null) return;

        shaderPass.SetKeywords(Enumerable.ToArray(mat._localKeywords.Values));
        GraphicsProgram? program = shaderPass.ActiveProgram;
        if (program == null) return;

        Framebuffer? destFb = destination?.IsValid() == true ? destination.frameBuffer : null;
        cmd.SetRenderTarget(destFb);
        cmd.SetShader(program);
        cmd.SetVertexSource(s_fullscreenSource);
        cmd.SetProperties(mat.BuildPropertySet());
        cmd.Draw(3, 1, 0, 0);
    }

    // ─────────────────────── Mesh draws ───────────────────────

    public static void DrawMesh(this CommandBuffer cmd, Mesh mesh, Material material)
        => DrawMesh(cmd, mesh, material, material?.Shader?.GetPass(0), Float4x4.Identity, null);

    public static void DrawMesh(this CommandBuffer cmd, Mesh mesh, Material material, int passIndex, Float4x4 model, PropertySet? properties)
        => DrawMesh(cmd, mesh, material, material?.Shader?.GetPass(passIndex), model, properties);

    public static void DrawMesh(this CommandBuffer cmd, Mesh mesh, Material material, ShaderPass? pass, Float4x4 model, PropertySet? properties)
    {
        if (mesh == null || material == null || pass == null)
            return;

        GraphicsProgram program = pass.ActiveProgram;
        if (program == null)
            return;

        mesh.Upload();
        if (mesh.VertexBuffer == null || mesh.IndexBuffer == null)
            return;

        cmd.SetShader(program);
        cmd.SetMaterialProperties(material);
        if (properties != null)
            cmd.SetProperties(properties);

        // Per-draw transform matrices. Each DrawMesh call allocates a small PropertySet so
        // that multiple calls into the same CB record distinct per-object matrices.
        var transforms = new PropertySet();
        transforms.SetMatrix("prowl_ObjectToWorld", model);
        transforms.SetMatrix("prowl_WorldToObject", model.Invert());
        transforms.SetMatrix("prowl_PrevObjectToWorld", model);
        cmd.SetProperties(transforms);

        cmd.SetVertexSource(mesh);
        cmd.DrawIndexed(1, 0, 0, 0);
    }
}
