using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Base class for graph-driven render pipelines. Subclass adds passes and a present pass in
/// InitializePasses; the pipeline solves them into an ordered graph and runs it per view via ExecuteView.
/// Optional Culler feeds the scene in as draw commands.
/// </summary>
public abstract class RenderPipeline<TView, TDrawCommand> : IDisposable
    where TView : IRenderView
{
    /// <summary>Culls the scene into draw commands for the passes. Optional.</summary>
    public IRenderCuller<TDrawCommand>? Culler { get; set; }

    private readonly List<IPass<TView, TDrawCommand>> _passes = new();
    private IPresentPass<TView, TDrawCommand>? _presentPass;
    private RenderGraph<TView, TDrawCommand>? _graph;
    private bool _initialized;

    /// <summary>
    /// Runs once, lazily, before first execution. Override to add passes and set the present pass.
    /// Pass read/write declarations decide execution order.
    /// </summary>
    protected abstract void InitializePasses();

    /// <summary>Adds a pass. Call from InitializePasses.</summary>
    protected void AddPass(IPass<TView, TDrawCommand> pass)
        => _passes.Add(pass ?? throw new ArgumentNullException(nameof(pass)));

    /// <summary>Sets the required present pass. Call from InitializePasses.</summary>
    protected void SetPresentPass(IPresentPass<TView, TDrawCommand> presentPass)
        => _presentPass = presentPass ?? throw new ArgumentNullException(nameof(presentPass));

    /// <summary>The present pass, resolved after init. Throws if none was set.</summary>
    public IPresentPass<TView, TDrawCommand> PresentPass
    {
        get
        {
            EnsureInitialized();
            return _presentPass ?? throw new InvalidOperationException(
                "A render pipeline must set a present pass in InitializePasses (via SetPresentPass).");
        }
    }

    /// <summary>The solved graph, built on first use from the added passes.</summary>
    public RenderGraph<TView, TDrawCommand> Graph
    {
        get
        {
            EnsureInitialized();
            return _graph ??= RenderGraph<TView, TDrawCommand>.Build(_passes);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        InitializePasses();
        _initialized = true;
    }

    /// <summary>
    /// Runs the solved graph for one view. Primes the culler, runs ordered passes with profiler scopes
    /// and capture, then runs the present pass. Called once per view per dispatch.
    /// </summary>
    public void ExecuteView(RenderContext<TView, TDrawCommand> context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        RenderGraph<TView, TDrawCommand> graph = Graph;
        IPassProfiler? profiler = context.Profiler;

        context.Culler?.Initialize(context.View);

        foreach (RenderGraph<TView, TDrawCommand>.PassNode node in graph.OrderedPasses)
        {
            profiler?.BeginSample(node.Pass.Name);
            node.Pass.Render(context);
            profiler?.EndSample();

            if (profiler != null && profiler.RequestCapture)
                CapturePassOutputs(context, profiler, node);
        }

        PresentPass.Present(context);
    }

    private static void CapturePassOutputs(RenderContext<TView, TDrawCommand> context, IPassProfiler profiler, RenderGraph<TView, TDrawCommand>.PassNode node)
    {
        if (node.Outputs == null || node.Outputs.Length == 0)
            return;

        var outputs = new Framebuffer[node.Outputs.Length];
        for (int i = 0; i < outputs.Length; i++)
            outputs[i] = context.GetRenderTexture(new TextureHandle(node.Outputs[i])).Framebuffer;

        TransferCommandBuffer transfer = context.GetTransferCommandBuffer($"{node.Pass.Name} Capture");
        profiler.Capture(outputs, transfer);
        context.SubmitTransferCommandBuffer(transfer);
    }

    /// <summary>Disposes passes and the present pass that are disposable.</summary>
    public virtual void Dispose()
    {
        foreach (IPass<TView, TDrawCommand> pass in _passes)
            (pass as IDisposable)?.Dispose();

        (_presentPass as IDisposable)?.Dispose();

        GC.SuppressFinalize(this);
    }
}
