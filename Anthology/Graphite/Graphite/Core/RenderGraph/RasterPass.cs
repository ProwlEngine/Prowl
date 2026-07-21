using System;

using Prowl.Vector;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Base for a raster pass with one declared target. Only owns target setup: declare target in Setup via
/// SetTarget/SetTargets, then in Render rent a command buffer, call BindTarget, draw, submit. Doesn't rent
/// or submit buffers itself - that's on the pass body. Need full control? Use raw IPass instead.
/// </summary>
public abstract class RasterPass<TView> : IPass<TView>
    where TView : IRenderView
{
    private TextureHandle _target;
    private bool _hasTarget;

    /// <summary>Pass name for debugging.</summary>
    public abstract string Name { get; }

    /// <summary>Declare target and other reads/writes here. Call SetTarget/SetTargets.</summary>
    public abstract void Setup(RenderContextBuilder builder);

    /// <summary>Rent a command buffer, call BindTarget, draw, submit.</summary>
    public abstract void Render(RenderContext<TView> context);

    /// <summary>
    /// Declares a single-target framebuffer with load/store ops. Handle resolves to the render target in Render.
    /// </summary>
    protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        _target = builder.GetOutputTexture(id, desc, history, ops);
        _hasTarget = true;
        return _target;
    }

    /// <summary>
    /// Declares an MRT framebuffer: one resource, desc with several color formats, one framebuffer with
    /// several color attachments. BindTarget applies load ops to every attachment.
    /// </summary>
    protected TextureHandle SetTargets(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc mrtDesc, int history = 0, TargetLoadStoreOps? ops = null)
        => SetTarget(builder, id, mrtDesc, history, ops);

    /// <summary>
    /// Binds the declared target and applies load ops. Clears color to opaque black, depth to 1.
    /// </summary>
    protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd)
        => BindTarget(context, cmd, default, 1f, 0);

    /// <summary>
    /// Binds the declared target and applies load ops, clearing with the given values.
    /// </summary>
    /// <param name="context">Render context.</param>
    /// <param name="cmd">Command buffer.</param>
    /// <param name="clearColor">Clear color for Clear-load attachments.</param>
    /// <param name="depthClear">Clear depth.</param>
    /// <param name="stencilClear">Clear stencil.</param>
    protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd, Color clearColor, float depthClear = 1f, byte stencilClear = 0)
    {
        if (!_hasTarget)
            throw new InvalidOperationException($"RasterPass '{Name}' called BindTarget without declaring a target in Setup via SetTarget or SetTargets.");

        RenderTexture target = context.GetRenderTexture(_target);
        TargetLoadStoreOps ops = context.GetTargetOps(_target.Id);

        cmd.SetFramebuffer(target.Framebuffer);

        if (ops.Color.Load == LoadAction.Clear)
        {
            int colorCount = target.Framebuffer.ColorTargets.Count;
            for (uint i = 0; i < colorCount; i++)
                cmd.ClearColorTarget(i, clearColor);
        }

        if (ops.Depth.Load == LoadAction.Clear && target.Framebuffer.DepthTarget != null)
            cmd.ClearDepthStencil(depthClear, stencilClear);
    }
}
