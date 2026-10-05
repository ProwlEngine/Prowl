using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Graph-driven render pipeline. Set passes with SetPasses or the constructor; solved into an ordered graph and run per view via ExecuteView.
/// </summary>
public class RenderPipeline : IDisposable
{
    private readonly List<IPass> _passes = new();
    private RenderGraph? _graph;
    private bool _executingView;

    /// <summary>Creates an empty pipeline. Call SetPasses before the first dispatch.</summary>
    public RenderPipeline()
    {
    }

    /// <summary>Creates a pipeline from a pass list. Read/write declarations decide order.</summary>
    /// <param name="passes">Passes to run.</param>
    public RenderPipeline(IEnumerable<IPass> passes)
    {
        SetPasses(passes);
    }

    /// <summary>Replaces the pass list; the graph rebuilds on next use. Not callable mid-dispatch.</summary>
    /// <param name="passes">Passes to run.</param>
    public void SetPasses(IEnumerable<IPass> passes)
    {
        if (passes == null)
            throw new ArgumentNullException(nameof(passes));

        if (_executingView)
            throw new InvalidOperationException("SetPasses cannot be called while a view is executing.");

        List<IPass> list = new();
        foreach (IPass pass in passes)
            list.Add(pass ?? throw new ArgumentException("Pass list contains null.", nameof(passes)));

        _graph?.Dispose();
        _graph = null;
        _passes.Clear();
        _passes.AddRange(list);
    }

    /// <summary>The solved graph, built on first use from the current passes.</summary>
    public RenderGraph Graph => _graph ??= RenderGraph.Build(_passes);

    /// <summary>
    /// Runs the solved graph for one view: ordered passes with profiler scopes and capture. Passes that write the view target are skipped when the view has none. The dispatch presents if a pass wrote the view target of a view whose Target is a swapchain framebuffer.
    /// Once per view per dispatch.
    /// </summary>
    public void ExecuteView(RenderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        RenderGraph graph = Graph;
        IGraphProfiler? profiler = context.GraphProfiler;
        IProfiler? capturer = context.Profiler;

        _executingView = true;
        try
        {
            int index = 0;
            bool hasViewTarget = context.HasViewTarget;
            foreach (RenderGraph.PassNode node in graph.OrderedPasses)
            {
                if (node.WritesViewTarget && !hasViewTarget)
                    continue;

                RenderResourceID[] inputs = profiler != null ? node.InputIds() : Array.Empty<RenderResourceID>();
                RenderResourceID[] outputs = profiler != null ? node.OutputIds() : Array.Empty<RenderResourceID>();
                var passInfo = new PassInfo(node.Pass.Name, index++, inputs, outputs);

                profiler?.BeginPass(passInfo);
                if (profiler != null)
                {
                    foreach (RenderResourceID input in inputs)
                    {
                        context.ResolveForProfiler(input, out RenderTexture? texture, out DeviceBuffer? buffer);
                        profiler.RecordPassRead(passInfo, input, texture, buffer);
                    }
                }

                context.SetCurrentPass(passInfo, node.Accesses, node.Pass.Name);
                context.TransitionForAccesses(node.Accesses);
                CommandBuffer passCommands = context.BeginPassCommandBuffer(node.Pass.Name);
                context.BindDeclaredTarget(passCommands, node.Accesses);
                node.Pass.Render(context, passCommands);
                context.EndCommandBuffer(passCommands);
                context.SetCurrentPass(null);

                profiler?.EndPass(passInfo);
                if (profiler != null)
                {
                    foreach (RenderResourceID output in outputs)
                    {
                        context.ResolveForProfiler(output, out RenderTexture? texture, out DeviceBuffer? buffer);
                        profiler.RecordPassWrite(passInfo, output, texture, buffer);
                    }
                }

                if (capturer is { RequestCapture: true })
                    CapturePassOutputs(context, capturer, passInfo, node);
            }

            context.RestoreRestingStates("View");
        }
        finally
        {
            _executingView = false;
        }
    }

    private static void CapturePassOutputs(RenderContext context, IProfiler profiler, in PassInfo passInfo, RenderGraph.PassNode node)
    {
        var framebuffers = new List<Framebuffer>();
        foreach (RenderResourceID output in node.OutputIds())
        {
            if (context.IsTextureResource(output) && output != GraphViewTargetResource.ViewTargetId)
                framebuffers.Add(context.GetRenderTexture(new TextureHandle(output)).Framebuffer);
        }

        if (framebuffers.Count == 0)
            return;

        Framebuffer[] outputs = framebuffers.ToArray();
        CommandBuffer capture = context.BeginCommandBuffer($"{node.Pass.Name} Capture");
        try
        {
            profiler.Capture(passInfo, outputs, capture);
        }
        finally
        {
            context.EndCommandBuffer(capture);
        }
    }

    /// <summary>Disposes passes that are disposable.</summary>
    public virtual void Dispose()
    {
        foreach (IPass pass in _passes)
            (pass as IDisposable)?.Dispose();

        _graph?.Dispose();

        GC.SuppressFinalize(this);
    }
}
