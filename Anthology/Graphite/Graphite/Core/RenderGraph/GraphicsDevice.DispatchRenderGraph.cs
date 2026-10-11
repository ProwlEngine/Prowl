using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite;

/// <summary>
/// One pipeline and the views it renders, for <see cref="GraphicsDevice.DispatchGraph(ReadOnlySpan{GraphDispatch}, IProfiler[])"/>.
/// </summary>
/// <param name="Pipeline">Pipeline to run.</param>
/// <param name="Views">Views to render with it.</param>
public readonly record struct GraphDispatch(RenderPipeline Pipeline, IReadOnlyList<IRenderView> Views);

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

        ExecutionTask task = BeginExecution(pipeline.Graph.Name, profilers);
        List<Swapchain>? presents = null;
        int index = 0;
        try
        {
            RunViews(task, pipeline, views, ref index, ref presents);
        }
        catch
        {
            CompleteExecution(task);
            throw;
        }
        return Finish(task, presents);
    }

    /// <summary>
    /// Runs several pipelines, each for its own views, in order as one graph execution. Resources a pipeline
    /// writes are ready for the pipelines after it, for example a world's shadows before the cameras that sample them.
    /// </summary>
    /// <param name="graphs">Pipelines with their views, run in this order.</param>
    /// <param name="profilers">Profilers for this execution only. Empty creates one from each <see cref="GlobalProfilers"/> factory.</param>
    public ExecutionTask DispatchGraph(ReadOnlySpan<GraphDispatch> graphs, params IProfiler[] profilers)
    {
        string[] names = new string[graphs.Length];
        for (int i = 0; i < graphs.Length; i++)
        {
            ValidationHelpers.RequireNotNull(this, graphs[i].Pipeline, nameof(GraphDispatch.Pipeline), nameof(DispatchGraph));
            ValidationHelpers.RequireNotNull(this, graphs[i].Views, nameof(GraphDispatch.Views), nameof(DispatchGraph));
            names[i] = graphs[i].Pipeline.Graph.Name;
        }

        ExecutionTask task = BeginExecution(string.Join(", ", names), profilers);
        List<Swapchain>? presents = null;
        int index = 0;
        try
        {
            foreach (GraphDispatch graph in graphs)
                RunViews(task, graph.Pipeline, graph.Views, ref index, ref presents);
        }
        catch
        {
            CompleteExecution(task);
            throw;
        }

        return Finish(task, presents);
    }

    // View indexes run across the whole execution, so profilers and captures see each view once.
    private void RunViews<T>(ExecutionTask task, RenderPipeline pipeline, IReadOnlyList<T> views, ref int index, ref List<Swapchain>? presents)
        where T : IRenderView
    {
        RenderGraph.RenderGraph graph = pipeline.Graph;
        IGraphProfiler? profiler = task.Profilers.Graph;

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
    }

    private ExecutionTask Finish(ExecutionTask task, List<Swapchain>? presents)
    {
        CompleteExecution(task);

        if (presents != null)
        {
            foreach (Swapchain swapchain in presents)
                SwapBuffers(swapchain);
        }

        return task;
    }
}
