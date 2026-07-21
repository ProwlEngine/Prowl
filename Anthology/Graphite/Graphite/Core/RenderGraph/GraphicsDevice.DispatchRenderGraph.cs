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
    /// <param name="profiler">Optional profiler to record this execution.</param>
    public ExecutionTask DispatchGraph<T, T2>(
        RenderPipeline<T, T2> pipeline,
        IReadOnlyList<T> views,
        IPassProfiler? profiler = null)
        where T : IRenderView
    {
        ValidationHelpers.RequireNotNull(pipeline, nameof(pipeline), nameof(DispatchGraph));
        ValidationHelpers.RequireNotNull(views, nameof(views), nameof(DispatchGraph));

        RenderGraph<T, T2> graph = pipeline.Graph;

        ExecutionTask task = BeginExecution();
        bool present = false;

        foreach (T view in views)
        {
            var context = new RenderContext<T, T2>(
                this, task, graph, view, pipeline.Provider, profiler);

            pipeline.ExecuteView(context);
            present |= context.RequestPresent;
        }

        CompleteExecution(task);

        if (present)
            SwapBuffers();

        return task;
    }
}
