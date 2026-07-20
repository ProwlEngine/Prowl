namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Required module that decides how a view's result gets to the screen. Runs once per view, after all
/// other passes. Can present to the swapchain or leave it offscreen. Handles sRGB, scaling, and pre-present UI.
/// </summary>
public interface IPresentPass<TView, TDrawCommand>
    where TView : IRenderView
{
    /// <summary>Name used in command-buffer labels and diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Runs after every other pass for the view. To present, grab the swapchain target from
    /// context, draw into it, and arm the present. To stay offscreen, do nothing.
    /// </summary>
    void Present(RenderContext<TView, TDrawCommand> context);
}
