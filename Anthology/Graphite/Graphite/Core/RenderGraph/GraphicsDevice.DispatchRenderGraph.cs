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
    /// <param name="profilers">Profilers for this execution only. Empty creates one from each <see cref="GlobalProfilers"/> factory.</param>
    public ExecutionTask DispatchGraph<T>(
        RenderPipeline pipeline,
        IReadOnlyList<T> views,
        params IProfiler[] profilers)
        where T : IRenderView
    {
        ValidationHelpers.RequireNotNull(this, pipeline, nameof(pipeline), nameof(DispatchGraph));
        ValidationHelpers.RequireNotNull(this, views, nameof(views), nameof(DispatchGraph));

        RenderGraph.RenderGraph graph = pipeline.Graph;

        List<Swapchain>? presents = null;
        ExecutionTask task = BeginExecution(graph.Name, profilers);
        IGraphProfiler? profiler = task.Profilers.Graph;

        int index = 0;
        foreach (T view in views)
        {
            var context = new RenderContext(
                this, task, graph, view, index);

            var viewInfo = new ViewInfo(view.Name, index++, view.PixelWidth, view.PixelHeight);

            profiler?.BeginView(viewInfo);
            pipeline.ExecuteView(context);
            profiler?.EndView(viewInfo);

            Swapchain? swapchain = context.PresentSwapchain;
            if (swapchain != null)
            {
                presents ??= [];
                if (!presents.Contains(swapchain))
                    presents.Add(swapchain);
            }
        }

        CompleteExecution(task);

        if (presents != null)
        {
            foreach (Swapchain swapchain in presents)
                SwapBuffers(swapchain);
        }

        return task;
    }
}
