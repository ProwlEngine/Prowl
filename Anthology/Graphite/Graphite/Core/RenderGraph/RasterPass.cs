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

    /// <summary>Declare target and other reads/writes here. Call SetTarget.</summary>
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

    /// <summary>Declares the view's target as this pass's target. The pass is skipped for a view with no target.</summary>
    protected TextureHandle SetViewTarget(RenderContextBuilder builder, TargetLoadStoreOps? ops = null)
    {
        _target = builder.DeclareViewTarget(ops);
        _hasTarget = true;
        return _target;
    }

    /// <summary>
    /// Binds the declared target and applies its load ops, clearing with the values the declaration carries.
    /// </summary>
    protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd)
    {
        if (!_hasTarget)
            throw new InvalidOperationException($"RasterPass '{Name}' called BindTarget without declaring a target in Setup via SetTarget.");

        RenderTexture target = context.GetRenderTexture(_target);
        TargetLoadStoreOps ops = context.GetTargetOps(_target.Id);

        cmd.SetFramebuffer(target.Framebuffer);
        cmd.SetAttachmentOps(ops);

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
