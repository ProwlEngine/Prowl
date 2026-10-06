using System;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Base for a raster pass with one declared target: declare it in Setup and draw in Render.
/// The graph binds the target with its declared ops and submits the buffer. Need full control? Use raw IPass instead.
/// </summary>
public abstract class RasterPass : IPass
{
    /// <summary>Pass name for debugging.</summary>
    public abstract string Name { get; }

    /// <summary>Declare target and other reads/writes here. Call SetTarget.</summary>
    public abstract void Setup(RenderContextBuilder builder);

    /// <summary>Draw into the already bound target on cmd. The graph submits cmd.</summary>
    public abstract void Render(RenderContext context, CommandBuffer cmd);

    /// <summary>
    /// Declares a single-target framebuffer with load/store ops. Handle resolves to the render target in Render.
    /// </summary>
    protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        return builder.DeclareOutputTexture(id, desc, history, ops);
    }

    /// <summary>Declares the view's target as this pass's target, with a depth attachment when a format is given. The pass is skipped for a view with no target.</summary>
    protected TextureHandle SetViewTarget(RenderContextBuilder builder, TargetLoadStoreOps? ops = null, PixelFormat? depthFormat = null)
    {
        return builder.DeclareViewTarget(ops, depthFormat: depthFormat);
    }
}
