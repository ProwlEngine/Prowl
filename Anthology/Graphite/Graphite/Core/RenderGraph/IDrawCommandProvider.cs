using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Produces pipeline-specific draw commands for passes. The only seam between scene and framework.
/// Primed once per view via Initialize, then passes pull slices on demand via GetDrawCommands.
/// </summary>
public interface IDrawCommandProvider<TDrawCommand>
{
    /// <summary>
    /// Runs at the start of view rendering. Use for pre-collecting before repeat on-demand queries.
    /// </summary>
    void Initialize(IRenderView view);

    /// <summary>Returns draw commands matching the query. Uses current view unless query overrides frustum.</summary>
    IReadOnlyList<TDrawCommand> GetDrawCommands(RenderQuery query);
}
