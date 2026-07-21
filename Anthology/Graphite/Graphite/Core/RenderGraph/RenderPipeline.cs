using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Base class for graph-driven render pipelines. Subclass adds passes and a present pass in InitializePasses;
/// gets solved into an ordered graph and run per view via ExecuteView.
/// </summary>
public abstract class RenderPipeline<TView> : IDisposable
    where TView : IRenderView
{
    private readonly List<IPass<TView>> _passes = new();
    private readonly List<GraphResource> _centralResources = new();
    private IPresentPass<TView>? _presentPass;
    private RenderGraph<TView>? _graph;
    private bool _initialized;

    /// <summary>
    /// Runs once lazily before first execution. Override to add passes and set the present pass.
    /// Read/write declarations decide order.
    /// </summary>
    protected abstract void InitializePasses();

    /// <summary>Adds a pass. Call from InitializePasses.</summary>
    protected void AddPass(IPass<TView> pass)
        => _passes.Add(pass ?? throw new ArgumentNullException(nameof(pass)));

    /// <summary>Sets the required present pass. Call from InitializePasses.</summary>
    protected void SetPresentPass(IPresentPass<TView> presentPass)
        => _presentPass = presentPass ?? throw new ArgumentNullException(nameof(presentPass));

    /// <summary>
    /// Declares a texture resource centrally so passes can reference it by ID with no owner. Call from InitializePasses.
    /// </summary>
    /// <param name="id">ID passes reference.</param>
    /// <param name="desc">Allocation description.</param>
    protected void DeclareTexture(RenderResourceID id, GraphTextureDesc desc)
        => _centralResources.Add(new GraphTextureResource(id, desc));

    /// <summary>
    /// Declares a buffer resource centrally so passes can reference it by ID with no owner. Call from InitializePasses.
    /// </summary>
    /// <param name="id">ID passes reference.</param>
    /// <param name="desc">Allocation description.</param>
    protected void DeclareBuffer(RenderResourceID id, GraphBufferDesc desc)
        => _centralResources.Add(new GraphBufferResource(id, desc));

    /// <summary>The present pass, resolved after init. Throws if none was set.</summary>
    public IPresentPass<TView> PresentPass
    {
        get
        {
            EnsureInitialized();
            return _presentPass ?? throw new InvalidOperationException(
                "A render pipeline must set a present pass in InitializePasses (via SetPresentPass).");
        }
    }

    /// <summary>The solved graph, built on first use from the added passes.</summary>
    public RenderGraph<TView> Graph
    {
        get
        {
            EnsureInitialized();
            return _graph ??= RenderGraph<TView>.Build(_passes, PresentPass, _centralResources);
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
    /// Runs the solved graph for one view: ordered passes with profiler scopes and capture, then present.
    /// Once per view per dispatch.
    /// </summary>
    public void ExecuteView(RenderContext<TView> context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        RenderGraph<TView> graph = Graph;
        IProfiler? profiler = context.Profiler;

        int index = 0;
        foreach (RenderGraph<TView>.PassNode node in graph.OrderedPasses)
        {
            var passInfo = new PassInfo(node.Pass.Name, index++, node.Inputs, node.Outputs);

            profiler?.BeginPass(passInfo);
            if (profiler != null)
            {
                foreach (RenderResourceID input in node.Inputs)
                    profiler.RecordPassRead(passInfo, input, context.ResolveForProfiler(input));
            }

            context.SetCurrentPass(passInfo);
            node.Pass.Render(context);
            context.SetCurrentPass(null);

            profiler?.EndPass(passInfo);
            if (profiler != null)
            {
                foreach (RenderResourceID output in node.Outputs)
                    profiler.RecordPassRead(passInfo, output, context.ResolveForProfiler(output));
            }

            context.ReclaimUnsubmittedCommandBuffers(node.Pass.Name);

            if (profiler != null && profiler.RequestCapture)
                CapturePassOutputs(context, profiler, passInfo, node);
        }

        PresentPass.Present(context);
        context.ReclaimUnsubmittedCommandBuffers(PresentPass.Name);
    }

    private static void CapturePassOutputs(RenderContext<TView> context, IProfiler profiler, in PassInfo passInfo, RenderGraph<TView>.PassNode node)
    {
        if (node.Outputs == null || node.Outputs.Length == 0)
            return;

        var framebuffers = new List<Framebuffer>(node.Outputs.Length);
        foreach (RenderResourceID output in node.Outputs)
        {
            if (context.IsTextureResource(output))
                framebuffers.Add(context.GetRenderTexture(new TextureHandle(output)).Framebuffer);
        }

        if (framebuffers.Count == 0)
            return;

        Framebuffer[] outputs = framebuffers.ToArray();
        TransferCommandBuffer transfer = context.GetTransferCommandBuffer($"{node.Pass.Name} Capture");
        profiler.Capture(passInfo, outputs, transfer);
        context.SubmitTransferCommandBuffer(transfer);
    }

    /// <summary>Disposes passes and the present pass that are disposable.</summary>
    public virtual void Dispose()
    {
        foreach (IPass<TView> pass in _passes)
            (pass as IDisposable)?.Dispose();

        (_presentPass as IDisposable)?.Dispose();

        _graph?.Dispose();

        GC.SuppressFinalize(this);
    }
}
