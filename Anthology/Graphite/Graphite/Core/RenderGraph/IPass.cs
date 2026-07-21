namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// A single pass in a pipeline. Declares its texture in/out once via Setup so the graph can
/// order it and resolve/share dependencies. Pipeline calls Render when it executes.
/// </summary>
public interface IPass<TView>
    where TView : IRenderView
{
    /// <summary>Pass name for debugging.</summary>
    string Name { get; }

    /// <summary>Declares the in/out textures for this pass.</summary>
    void Setup(RenderContextBuilder builder);

    /// <summary>Records this pass's rendering. Resolve textures with context.GetRenderTexture.</summary>
    void Render(RenderContext<TView> context);
}
