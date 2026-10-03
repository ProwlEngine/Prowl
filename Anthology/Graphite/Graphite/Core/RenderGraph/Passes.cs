namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Minimal size info for view-relative render targets. Concrete views add richer data.
/// </summary>
public interface IRenderView
{
    /// <summary>Width in pixels.</summary>
    uint PixelWidth { get; }

    /// <summary>Height in pixels.</summary>
    uint PixelHeight { get; }

    /// <summary>Stable identity across frames. Temporal history rings are kept per view id.</summary>
    int ViewId { get; }

    /// <summary>
    /// Display name for profiler/debug tooling. Defaults to the type name; override to tell instances apart (e.g. per camera).
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// Framebuffer that passes declaring the view target draw into, or null. Write-only to passes: they can neither
    /// sample nor transition it. Should match <see cref="PixelWidth"/> and <see cref="PixelHeight"/>. Setting this and
    /// <see cref="TargetSwapchain"/> together throws at dispatch.
    /// </summary>
    Framebuffer? TargetFramebuffer => null;

    /// <summary>
    /// True to draw into the device's main swapchain image and present after the dispatch. Needs a main swapchain.
    /// </summary>
    bool TargetSwapchain => false;
}

/// <summary>
/// One pipeline pass. Declares texture in/out via Setup so the graph can order and resolve deps. Pipeline calls Render to execute.
/// </summary>
public interface IPass<TView>
    where TView : IRenderView
{
    /// <summary>Debug name.</summary>
    string Name { get; }

    /// <summary>Declare in/out textures.</summary>
    void Setup(RenderContextBuilder builder);

    /// <summary>Record rendering into cmd, already begun. The graph submits it after Render returns. Get textures via context.GetRenderTexture.</summary>
    void Render(RenderContext<TView> context, CommandBuffer cmd);
}
