using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Graph-driven render pipeline. Pass a pass list to the constructor, or subclass and add passes in InitializePasses;
/// gets solved into an ordered graph and run per view via ExecuteView.
/// </summary>
public class RenderPipeline<TView> : IDisposable
    where TView : IRenderView
{
    private readonly List<IPass<TView>> _passes = new();
    private readonly List<GraphResource> _centralResources = new();
    private readonly IPass<TView>[] _composedPasses;
    private RenderGraph<TView>? _graph;
    private bool _initialized;
    private bool _executingView;

    /// <summary>Creates a pipeline to subclass; add passes in InitializePasses.</summary>
    protected RenderPipeline()
    {
        _composedPasses = Array.Empty<IPass<TView>>();
    }

    /// <summary>Creates a pipeline from a fixed pass list. Read/write declarations decide order.</summary>
    /// <param name="passes">Passes to run. Re-added after InvalidateGraph.</param>
    public RenderPipeline(IEnumerable<IPass<TView>> passes)
    {
        if (passes == null)
            throw new ArgumentNullException(nameof(passes));

        List<IPass<TView>> list = new();
        foreach (IPass<TView> pass in passes)
            list.Add(pass ?? throw new ArgumentException("Pass list contains null.", nameof(passes)));

        _composedPasses = list.ToArray();
    }

    /// <summary>
    /// Runs once lazily before first execution. Override to add passes.
    /// Read/write declarations decide order.
    /// </summary>
    protected virtual void InitializePasses()
    {
    }

    /// <summary>Adds a pass. Call from InitializePasses.</summary>
    protected void AddPass(IPass<TView> pass)
        => _passes.Add(pass ?? throw new ArgumentNullException(nameof(pass)));

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

    /// <summary>The solved graph, built on first use from the added passes.</summary>
    public RenderGraph<TView> Graph
    {
        get
        {
            EnsureInitialized();
            return _graph ??= RenderGraph<TView>.Build(_passes, _centralResources);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _passes.AddRange(_composedPasses);
        InitializePasses();
        _initialized = true;
    }

    /// <summary>Disposes the current graph and re-runs InitializePasses lazily on next access. Not callable mid-dispatch.</summary>
    protected void InvalidateGraph()
    {
        if (_executingView)
            throw new InvalidOperationException("InvalidateGraph cannot be called while a view is executing.");

        _graph?.Dispose();
        _graph = null;
        _passes.Clear();
        _centralResources.Clear();
        _initialized = false;
    }

    /// <summary>
    /// Runs the solved graph for one view: ordered passes with profiler scopes and capture. Presents after dispatch if a pass wrote the backbuffer.
    /// Once per view per dispatch.
    /// </summary>
    public void ExecuteView(RenderContext<TView> context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        RenderGraph<TView> graph = Graph;
        IProfiler? profiler = context.Profiler;

        _executingView = true;
        try
        {
            int index = 0;
            foreach (RenderGraph<TView>.PassNode node in graph.OrderedPasses)
            {
                var passInfo = new PassInfo(node.Pass.Name, index++, node.Inputs, node.Outputs);

                profiler?.BeginPass(passInfo);
                if (profiler != null)
                {
                    foreach (RenderResourceID input in node.Inputs)
                    {
                        context.ResolveForProfiler(input, out RenderTexture? texture, out DeviceBuffer? buffer);
                        profiler.RecordPassRead(passInfo, input, texture, buffer);
                    }
                }

                context.SetCurrentPass(passInfo, node.DeclaredOutputs, node.Accesses, node.Pass.Name);
                context.TransitionForAccesses(node.Pass.Name, node.Accesses);
                CommandBuffer passCommands = context.BeginPassCommandBuffer(node.Pass.Name);
                node.Pass.Render(context, passCommands);
                context.EndPassCommandBuffer(passCommands);
                context.SetCurrentPass(null);

                profiler?.EndPass(passInfo);
                if (profiler != null)
                {
                    foreach (RenderResourceID output in node.Outputs)
                    {
                        context.ResolveForProfiler(output, out RenderTexture? texture, out DeviceBuffer? buffer);
                        profiler.RecordPassRead(passInfo, output, texture, buffer);
                    }
                }

                context.ReclaimUnsubmittedCommandBuffers(node.Pass.Name);

                if (profiler != null && profiler.RequestCapture)
                    CapturePassOutputs(context, profiler, passInfo, node);
            }

            context.RestoreRestingStates("View");
        }
        finally
        {
            _executingView = false;
        }
    }

    private static void CapturePassOutputs(RenderContext<TView> context, IProfiler profiler, in PassInfo passInfo, RenderGraph<TView>.PassNode node)
    {
        var framebuffers = new List<Framebuffer>(node.Outputs?.Length ?? 0);
        if (node.Outputs != null)
        {
            foreach (RenderResourceID output in node.Outputs)
            {
                if (context.IsTextureResource(output) && output != GraphBackbufferResource.BackbufferId)
                    framebuffers.Add(context.GetRenderTexture(new TextureHandle(output)).Framebuffer);
            }
        }

        if (framebuffers.Count == 0)
            return;

        Framebuffer[] outputs = framebuffers.ToArray();
        TransferCommandBuffer transfer = context.GetTransferCommandBuffer($"{node.Pass.Name} Capture");
        try
        {
            profiler.Capture(passInfo, outputs, transfer);
        }
        finally
        {
            if (!transfer.HasEnded)
                transfer.End();
        }
        context.SubmitTransferCommandBuffer(transfer);
    }

    /// <summary>Disposes passes that are disposable.</summary>
    public virtual void Dispose()
    {
        foreach (IPass<TView> pass in _passes)
            (pass as IDisposable)?.Dispose();

        _graph?.Dispose();

        GC.SuppressFinalize(this);
    }
}
