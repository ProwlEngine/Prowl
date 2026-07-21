namespace Prowl.Graphite.RenderGraph;

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
