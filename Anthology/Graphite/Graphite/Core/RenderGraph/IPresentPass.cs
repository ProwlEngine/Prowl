namespace Prowl.Graphite.RenderGraph;

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
