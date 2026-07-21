using System;

using Prowl.Vector;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Convenience base for a raster pass that renders into one declared graph target. It owns target setup
/// only: declare the target in <see cref="Setup"/> with <see cref="SetTarget"/> or <see cref="SetTargets"/>,
/// then in <see cref="Render"/> rent a command buffer from the context, call
/// <see cref="BindTarget(RenderContext{TView}, CommandBuffer)"/> to bind it and apply the declared
/// load/clear operations, record draws, and submit. The base never rents or submits command buffers - the
/// pass body owns that per the command-buffer lifecycle. Use raw <see cref="IPass{TView}"/> when you need
/// full control over target setup.
/// </summary>
public abstract class RasterPass<TView> : IPass<TView>
    where TView : IRenderView
{
    private TextureHandle _target;
    private bool _hasTarget;

    /// <summary>Pass name for debugging.</summary>
    public abstract string Name { get; }

    /// <summary>Declares the pass's target (and any other reads/writes). Call SetTarget/SetTargets here.</summary>
    public abstract void Setup(RenderContextBuilder builder);

    /// <summary>Records the pass. Rent a command buffer, call BindTarget, record draws, submit.</summary>
    public abstract void Render(RenderContext<TView> context);

    /// <summary>
    /// Declares a single-target framebuffer this pass renders into, with its load/store operations. The
    /// returned handle resolves to the render target in <see cref="Render"/>.
    /// </summary>
    protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        _target = builder.GetOutputTexture(id, desc, history, ops);
        _hasTarget = true;
        return _target;
    }

    /// <summary>
    /// Declares a multiple-render-target framebuffer this pass renders into: one target resource whose
    /// description carries several color formats, resolving to one framebuffer with several color
    /// attachments. <see cref="BindTarget(RenderContext{TView}, CommandBuffer)"/> applies the declared load
    /// operations to every attachment.
    /// </summary>
    protected TextureHandle SetTargets(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc mrtDesc, int history = 0, TargetLoadStoreOps? ops = null)
        => SetTarget(builder, id, mrtDesc, history, ops);

    /// <summary>
    /// Binds the declared target framebuffer on the given command buffer and applies its declared load
    /// operations, clearing color to opaque black and depth to 1 where the declaration asks for a clear.
    /// </summary>
    protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd)
        => BindTarget(context, cmd, default, 1f, 0);

    /// <summary>
    /// Binds the declared target framebuffer on the given command buffer and applies its declared load
    /// operations, clearing with the supplied values where the declaration asks for a clear.
    /// </summary>
    /// <param name="context">The pass's render context.</param>
    /// <param name="cmd">A command buffer rented from the context.</param>
    /// <param name="clearColor">Color used for attachments whose load op is Clear.</param>
    /// <param name="depthClear">Depth value used when the depth load op is Clear.</param>
    /// <param name="stencilClear">Stencil value used when the depth load op is Clear.</param>
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
