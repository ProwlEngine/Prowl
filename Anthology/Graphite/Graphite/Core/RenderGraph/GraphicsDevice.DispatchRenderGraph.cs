using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Dispatches a pipeline for the given views as one graph execution.
    /// </summary>
    /// <param name="pipeline">Pipeline to run.</param>
    /// <param name="views">Views to render, one execution per view. Must not be null.</param>
    public ExecutionTask DispatchGraph<T>(
        RenderPipeline<T> pipeline,
        IReadOnlyList<T> views)
        where T : IRenderView
    {
        ValidationHelpers.RequireNotNull(pipeline, nameof(pipeline), nameof(DispatchGraph));
        ValidationHelpers.RequireNotNull(views, nameof(views), nameof(DispatchGraph));

        RenderGraph<T> graph = pipeline.Graph;

        ExecutionTask task = BeginExecution();
        bool present = false;

        foreach (T view in views)
        {
            var context = new RenderContext<T>(
                this, task, graph, view);

            pipeline.ExecuteView(context);
            present |= context.RequestPresent;
        }

        CompleteExecution(task);

        if (present)
            SwapBuffers();

        return task;
    }
}
