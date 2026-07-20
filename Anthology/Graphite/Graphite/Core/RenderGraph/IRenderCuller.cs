using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Turns scene renderables into pipeline-specific draw commands. The only seam between scene and
/// framework. Primed once per view via Initialize, then passes pull slices on demand via GetDrawCommands.
/// </summary>
public interface IRenderCuller<TDrawCommand>
{
    /// <summary>
    /// Runs at the start of view rendering. Use for pre-culling before repeat on-demand queries.
    /// </summary>
    void Initialize(IRenderView view);

    /// <summary>Returns draw commands matching the query. Uses current view unless query overrides frustum.</summary>
    IReadOnlyList<TDrawCommand> GetDrawCommands(RenderQuery query);

    /// <summary>Renderables ingested for the current view.</summary>
    int RenderablesCollected { get; }

    /// <summary>Renderables culled away for the current view.</summary>
    int RenderablesCulled { get; }

    /// <summary>Renderables that survived culling for the current view.</summary>
    int RenderablesVisible { get; }
}
