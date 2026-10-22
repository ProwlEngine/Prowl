using System;

namespace Prowl.Graphite;

/// <summary>
/// Base for an execution raster pass with one declared target: declare it in Setup and draw in Render.
/// The graph binds the target with its declared ops and submits the buffer. Need full control? Use raw IExecutionPass instead.
/// </summary>
public abstract class RasterPass : IExecutionPass
{
    /// <summary>Pass name for debugging.</summary>
    public abstract string Name { get; }

    /// <summary>Declare target and other reads/writes here. Call SetTarget.</summary>
    public abstract void Setup(RenderContextBuilder builder);

    /// <summary>Draw into the already bound target on cmd. The graph submits cmd.</summary>
    public abstract void Render(RenderContext context, CommandBuffer cmd);

    /// <summary>Declares a single-target framebuffer with load/store ops. Handle resolves to the render target in Render.</summary>
    protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        return builder.DeclareOutputTexture(id, desc, history, ops);
    }
}

/// <summary>
/// Base for an instance raster pass with one declared target: declare it in Setup and draw in Render.
/// The graph binds the target with its declared ops and submits the buffer. Need full control? Use raw IInstancePass instead.
/// </summary>
public abstract class InstanceRasterPass : IInstancePass
{
    /// <summary>Pass name for debugging.</summary>
    public abstract string Name { get; }

    /// <summary>The kind of instance the pass runs in.</summary>
    public abstract GraphKind Kind { get; }

    /// <summary>Declare target and other reads/writes here. Call SetTarget or SetInstanceTarget.</summary>
    public abstract void Setup(InstanceContextBuilder builder);

    /// <summary>Draw into the already bound target on cmd. The graph submits cmd.</summary>
    public abstract void Render(InstanceContext context, CommandBuffer cmd);

    /// <summary>Declares a single-target framebuffer with load/store ops. Handle resolves to the render target in Render.</summary>
    protected TextureHandle SetTarget(InstanceContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        return builder.DeclareOutputTexture(id, desc, history, ops);
    }

    /// <summary>Declares the instance target as this pass's target, with depth when a format is given. Skipped for an instance with no target.</summary>
    protected TextureHandle SetInstanceTarget(InstanceContextBuilder builder, TargetLoadStoreOps? ops = null, PixelFormat? depthFormat = null)
    {
        return builder.DeclareInstanceTarget(ops, depthFormat: depthFormat);
    }
}
