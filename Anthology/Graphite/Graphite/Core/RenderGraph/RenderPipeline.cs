using System;
using System.Collections.Generic;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Graph-driven render pipeline. Set passes with SetPasses or the constructor; solved into an ordered graph and run per view via ExecuteView.
/// </summary>
public class RenderPipeline : IDisposable
{
    private readonly List<IPass> _passes = new();
    private RenderGraph? _graph;
    private string? _name;
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

    /// <summary>Debug name of the graph, reported to profilers. Defaults to the pipeline type name. Setting it rebuilds the graph on next use.</summary>
    public string Name
    {
        get => _name ?? GetType().Name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_executingView)
                throw new InvalidOperationException("Name cannot be set while a view is executing.");

            _name = value;
            _graph?.Dispose();
            _graph = null;
        }
    }

    /// <summary>The solved graph, built on first use from the current passes.</summary>
    public RenderGraph Graph => _graph ??= RenderGraph.Build(_passes, Name);

    /// <summary>
    /// Runs the solved graph for one view: ordered passes with profiler scopes. Passes that write the view target are skipped when the view has none. The dispatch presents if a pass wrote the view target of a view whose Target is a swapchain framebuffer.
    /// Once per view per dispatch.
    /// </summary>
    public void ExecuteView(RenderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        RenderGraph graph = Graph;
        IGraphProfiler? profiler = context.GraphProfiler;

        GraphCapture? capture = null;
        _executingView = true;
        try
        {
            int index = 0;
            bool hasViewTarget = context.HasViewTarget;
            capture = BeginCapture(context, graph, hasViewTarget);
            foreach (RenderGraph.PassNode node in graph.OrderedPasses)
            {
                if (node.WritesViewTarget && !hasViewTarget)
                    continue;

                RenderResourceID[] inputs = node.Inputs;
                RenderResourceID[] outputs = node.Outputs;
                var passInfo = new PassInfo(node.Pass.Name, index, context.ViewIndex, inputs, outputs);

                profiler?.BeginPass(passInfo);
                capture?.BeginPass(index);
                context.SetCurrentPass(passInfo, node.Accesses, node.Pass.Name);
                context.TransitionForAccesses(node.Accesses);
                CommandBuffer passCommands = context.BeginPassCommandBuffer(node.Pass.Name);
                context.BindDeclaredTarget(passCommands, node.Accesses);
                node.Pass.Render(context, passCommands);
                PassStats stats = passCommands.Stats;
                context.EndCommandBuffer(passCommands);
                context.MarkAttachmentWrites(node.Accesses);
                context.SetCurrentPass(null);
                capture?.EndPass(index, passCommands);
                index++;

                profiler?.EndPass(passInfo, stats);
            }

            context.RestoreRestingStates("View");
        }
        finally
        {
            capture?.EndView();
            _executingView = false;
        }
    }

    private static GraphCapture? BeginCapture(RenderContext context, RenderGraph graph, bool hasViewTarget)
    {
        ICaptureProfiler? hook = context.CaptureHook;
        if (hook == null)
            return null;

        List<RenderGraph.PassNode> nodes = new();
        foreach (RenderGraph.PassNode node in graph.OrderedPasses)
        {
            if (!node.WritesViewTarget || hasViewTarget)
                nodes.Add(node);
        }

        PassInfo[] infos = new PassInfo[nodes.Count];
        for (int i = 0; i < infos.Length; i++)
            infos[i] = new PassInfo(nodes[i].Pass.Name, i, context.ViewIndex, nodes[i].Inputs, nodes[i].Outputs);

        GraphCapture capture = new(hook, context, graph, nodes.ToArray(), infos);
        capture.BeginView(context.View.Name, context.ViewIndex, context.View.PixelWidth, context.View.PixelHeight);
        return capture;
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
