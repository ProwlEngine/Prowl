using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Runs a pipeline for the views as one graph execution.
    /// </summary>
    /// <param name="pipeline">Pipeline to run.</param>
    /// <param name="views">Views to render.</param>
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

        int index = 0;
        foreach (T view in views)
        {
            var context = new RenderContext<T>(
                this, task, graph, view);

            var viewInfo = new ViewInfo(view.Name, index++, view.PixelWidth, view.PixelHeight);

            Profiler?.BeginView(viewInfo);
            pipeline.ExecuteView(context);
            Profiler?.EndView(viewInfo);

            present |= context.RequestPresent;
        }

        CompleteExecution(task);

        if (present)
            SwapBuffers();

        return task;
    }
}
