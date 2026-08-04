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

    /// <summary>
    /// Display name for profiler/debug tooling. Defaults to the type name; override to tell instances apart (e.g. per camera).
    /// </summary>
    string Name => GetType().Name;
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

    /// <summary>Record rendering. Get textures via context.GetRenderTexture.</summary>
    void Render(RenderContext<TView> context);
}

/// <summary>
/// Decides how a view's result reaches the screen. Runs once per view, last. Can present
/// to swapchain or stay offscreen. Handles sRGB, scaling, pre-present UI.
/// </summary>
public interface IPresentPass<TView>
    where TView : IRenderView
{
    /// <summary>Name for command-buffer labels and diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Declares textures read and whether swapchain is needed this run. No output
    /// textures, this is the graph's terminal step.
    /// </summary>
    void Setup(PresentContextBuilder builder);

    /// <summary>
    /// Runs after every other pass for the view. To present: grab swapchain target,
    /// draw, arm present. Otherwise do nothing.
    /// </summary>
    void Present(RenderContext<TView> context);
}
