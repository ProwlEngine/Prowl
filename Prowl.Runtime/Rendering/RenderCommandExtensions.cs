// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GraphiteIndexFormat = Prowl.Graphite.IndexFormat;
using IndexFormat = Prowl.Runtime.Resources.IndexFormat;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Bridges Prowl's render pipeline onto Graphite's <see cref="CommandBuffer"/> + <see cref="PropertySet"/>.
/// <para>
/// Prowl's old custom <c>CommandBuffer</c> exposed a large draw API (render targets, blits, grab
/// textures, global uniforms, mesh draws). Graphite's command buffer is lower level. These extensions
/// re-expose the old call shapes: the ones that map cleanly to Graphite are implemented; the ones that
/// require the full Stage-2 render-graph rework (blits, grab textures, multi-target binding) are no-ops
/// for now, with the original behaviour described inline so they can be finished in the later pass.
/// </para>
/// </summary>
public static class RenderCommandExtensions
{
    /// <summary>Per-mesh <see cref="IVertexSource"/> built from the mesh's Graphite vertex/index buffers.</summary>
    public readonly struct MeshVertexSource(Mesh mesh) : IVertexSource
    {
        public PrimitiveTopology Topology => ToPrimitive(mesh.MeshTopology);

        public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
            => binding = new VertexBinding(mesh.VertexBuffer!);

        public bool TryGetIndexBuffer(out DeviceBuffer buffer, out GraphiteIndexFormat format, out uint indexCount)
        {
            buffer = mesh.IndexBuffer!;
            format = mesh.IndexFormat == IndexFormat.UInt32 ? GraphiteIndexFormat.UInt32 : GraphiteIndexFormat.UInt16;
            indexCount = (uint)mesh.IndexCount;
            return buffer != null;
        }
    }

    public static PrimitiveTopology ToPrimitive(Topology topology) => topology switch
    {
        Topology.Triangles => PrimitiveTopology.TriangleList,
        Topology.TriangleStrip => PrimitiveTopology.TriangleStrip,
        Topology.Lines => PrimitiveTopology.LineList,
        Topology.LineStrip => PrimitiveTopology.LineStrip,
        Topology.Points => PrimitiveTopology.PointList,
        _ => PrimitiveTopology.TriangleList,
    };

    // ─────────────────────── PropertySet convenience (old PropertyState names) ───────────────────────

    public static void SetColor(this PropertySet set, string name, Color color) => set.SetFloat4(name, color);
    public static void SetVector(this PropertySet set, string name, Float2 value) => set.SetFloat2(name, value);
    public static void SetVector(this PropertySet set, string name, Float3 value) => set.SetFloat3(name, value);
    public static void SetVector(this PropertySet set, string name, Float4 value) => set.SetFloat4(name, value);

    public static void SetTexture(this PropertySet set, string name, Texture2D? texture)
    {
        if (texture?.Handle != null)
            set.SetTexture(name, texture.Handle, texture.Sampler);
    }

    public static void SetTexture(this PropertySet set, string name, AssetRef<Texture2D> texture)
        => set.SetTexture(name, texture.Res);

    // ─────────────────────── Global uniforms ───────────────────────

    public static void SetGlobalInt(this CommandBuffer cmd, string name, int value) => GlobalPropertySet.SetInt(name, value);
    public static void SetGlobalFloat(this CommandBuffer cmd, string name, float value) => GlobalPropertySet.SetFloat(name, value);
    public static void SetGlobalVector(this CommandBuffer cmd, string name, Float2 value) => GlobalPropertySet.SetFloat2(name, value);
    public static void SetGlobalVector(this CommandBuffer cmd, string name, Float3 value) => GlobalPropertySet.SetFloat3(name, value);
    public static void SetGlobalVector(this CommandBuffer cmd, string name, Float4 value) => GlobalPropertySet.SetFloat4(name, value);
    public static void SetGlobalMatrix(this CommandBuffer cmd, string name, Float4x4 value) => GlobalPropertySet.SetMatrix(name, value);

    public static void SetGlobalTexture(this CommandBuffer cmd, string name, Texture2D? texture)
    {
        if (texture?.Handle != null)
            GlobalPropertySet.SetTexture(name, texture.Handle, texture.Sampler);
    }

    public static void ClearGlobalTexture(this CommandBuffer cmd, string name)
    {
        // GlobalPropertySet has no per-slot clear yet; grab-texture scoping is part of the Stage-2 rework.
    }

    // ─────────────────────── Material / per-object properties ───────────────────────

    public static void SetMaterialProperties(this CommandBuffer cmd, Material material)
    {
        PropertySet set = material.BuildPropertySet();
        if (set != null)
            cmd.SetProperties(set);
    }

    public static void SetInstanceProperties(this CommandBuffer cmd, PropertySet properties)
    {
        if (properties != null)
            cmd.SetProperties(properties);
    }

    public static void SetMatrix(this CommandBuffer cmd, string name, in Float4x4 value)
    {
        // Per-object transform uniforms (prowl_ObjectToWorld, etc.). These were bound onto a transient
        // per-draw property set in the old pipeline; that wiring is part of the Stage-2 draw-path port.
    }

    public static void SetRasterState(this CommandBuffer cmd, RasterizerState state)
    {
        // Graphite owns blend/depth/raster on the GraphicsProgram; per-draw raster override is Stage-2.
    }

    // ─────────────────────── Targets / viewport / scissor ───────────────────────

    public static void SetRenderTarget(this CommandBuffer cmd, Framebuffer? framebuffer)
    {
        cmd.SetFramebuffer(framebuffer ?? Graphics.Device.SwapchainFramebuffer!);
    }

    public static void ClearRenderTarget(this CommandBuffer cmd, ClearFlags flags, Color color)
    {
        if ((flags & ClearFlags.Color) != 0)
            cmd.ClearColorTarget(0, color);
        if ((flags & (ClearFlags.Depth | ClearFlags.Stencil)) != 0)
            cmd.ClearDepthStencil(1f, 0);
    }

    public static void SetRenderTargets(this CommandBuffer cmd, Framebuffer color, Framebuffer extra)
    {
        // The old API allowed a separate read/draw target split (for grab/blit). Graphite binds a single
        // framebuffer (with its own attachments); bind the primary draw target and defer the split.
        cmd.SetFramebuffer(color);
    }

    public static void SetViewport(this CommandBuffer cmd, int x, int y, uint width, uint height)
    {
        cmd.SetViewport(0, new Viewport(x, y, width, height, 0f, 1f));
    }

    public static void SetScissor(this CommandBuffer cmd, int x, int y, uint width, uint height)
    {
        cmd.SetScissorRect(0, (uint)x, (uint)y, width, height);
    }

    public static void DisableScissor(this CommandBuffer cmd)
    {
        cmd.SetFullScissorRects();
    }

    public static void GenerateMipmap(this CommandBuffer cmd, Texture2D texture)
    {
        if (texture?.Handle != null)
            cmd.GenerateMipmaps(texture.Handle);
    }

    // ─────────────────────── Blits (Stage-2) ───────────────────────

    public static void Blit(this CommandBuffer cmd, RenderTexture source, RenderTexture destination, Material? material = null, int pass = 0, bool restoreTarget = false, bool clear = false)
    {
        // Full-screen blit through a material pass. Needs the Stage-2 fullscreen-pass plumbing
        // (bind dest framebuffer, set _MainTex = source, draw fullscreen triangle). Deferred.
    }

    public static void Blit(this CommandBuffer cmd, RenderTexture destination, Material? material, int pass = 0)
    {
        // Full-screen material blit into destination (source bound by the caller via the material's
        // textures). Needs the Stage-2 fullscreen-pass plumbing. Deferred.
    }

    public static void BlitFramebuffer(this CommandBuffer cmd, int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, ClearFlags mask, BlitFilter filter)
    {
        // Framebuffer-to-framebuffer copy for the grab-texture handshake; deferred to the Stage-2 rework.
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

        cmd.SetVertexSource(new MeshVertexSource(mesh));
        cmd.DrawIndexed(1, 0, 0, 0);
    }
}
