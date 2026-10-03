using System;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Base for a raster pass with one declared target: declare it in Setup, call BindTarget on the given command buffer in Render.
/// The graph submits that buffer. Need full control? Use raw IPass instead.
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

    /// <summary>Call BindTarget on cmd, then draw. The graph submits cmd.</summary>
    public abstract void Render(RenderContext<TView> context, CommandBuffer cmd);

    /// <summary>
    /// Declares a single-target framebuffer with load/store ops. Handle resolves to the render target in Render.
    /// </summary>
    protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        _target = builder.DeclareOutputTexture(id, desc, history, ops);
        _hasTarget = true;
        return _target;
    }

    /// <summary>Declares the main swapchain image as this pass's target. The frame presents after dispatch.</summary>
    protected TextureHandle SetBackbufferTarget(RenderContextBuilder builder, TargetLoadStoreOps? ops = null)
    {
        _target = builder.DeclareBackbuffer(ops);
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
    /// Binds the declared target and applies its load ops, clearing with the values the declaration carries.
    /// </summary>
    protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd)
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
                cmd.ClearColorTarget(i, ops.Color.ClearColor);
        }

        if (ops.Depth.Load == LoadAction.Clear && target.Framebuffer.DepthTarget != null)
            cmd.ClearDepthStencil(ops.Depth.ClearDepth, ops.Depth.ClearStencil);
    }
}
