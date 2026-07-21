using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Per-view render context passed to passes and the present pass. Fresh per view. Holds the view, the
/// draw command provider, command buffers, transient textures, and resolved render targets for this execution.
/// </summary>
public sealed class RenderContext<TView, TDrawCommand>
    where TView : IRenderView
{
    private readonly GraphicsDevice _device;
    private readonly ExecutionTask _task;
    private readonly RenderGraph<TView, TDrawCommand> _graph;
    private readonly TView _view;
    private readonly IDrawCommandProvider<TDrawCommand>? _provider;
    private readonly IPassProfiler? _profiler;
    private readonly PropertySet _globals = new();
    private readonly Dictionary<RenderResourceID, RenderTexture> _resolved = new();

    private bool _presentRequested;

    internal RenderContext(
        GraphicsDevice device,
        ExecutionTask task,
        RenderGraph<TView, TDrawCommand> graph,
        TView view,
        IDrawCommandProvider<TDrawCommand>? provider,
        IPassProfiler? profiler)
    {
        _device = device;
        _task = task;
        _graph = graph;
        _view = view;
        _provider = provider;
        _profiler = profiler;
    }

    /// <summary>The execution this context records into.</summary>
    public ExecutionTask Task => _task;

    /// <summary>True once the present pass has armed the swapchain present.</summary>
    public bool RequestPresent => _presentRequested;

    /// <summary>The view being rendered.</summary>
    public TView View => _view;

    /// <summary>Provider holding this view's draw commands, or null if the pipeline has none.</summary>
    public IDrawCommandProvider<TDrawCommand>? Provider => _provider;

    /// <summary>Global shader properties for this view (view matrices, time, ambient, etc).</summary>
    public PropertySet Globals => _globals;

    /// <summary>Profiler for this execution, or null if profiling is off.</summary>
    public IPassProfiler? Profiler => _profiler;

    /// <summary>Rents a command buffer for this pass to record into.</summary>
    /// <param name="name">Optional debug name.</param>
    public CommandBuffer GetCommandBuffer(string name = "")
    {
        CommandBuffer cb = _device.RentGraphCommandBuffer();

        cb.Execution = _task;
        _task.TrackRentedCommandBuffer(cb);
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        return cb;
    }

    /// <summary>Submits a recorded command buffer.</summary>
    /// <param name="cmd">Command buffer to submit.</param>
    public void SubmitCommandBuffer(CommandBuffer cmd) => _task.SubmitCommandsInternal(cmd);

    /// <summary>Rents a transfer command buffer for this execution, restricted to buffer/texture copies.</summary>
    /// <param name="name">Optional debug name.</param>
    public TransferCommandBuffer GetTransferCommandBuffer(string name = "")
    {
        TransferCommandBuffer cb = _device.ResourceFactory.CreateTransferCommandBuffer();

        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        return cb;
    }

    /// <summary>Submits a recorded transfer command buffer without blocking.</summary>
    /// <param name="cmd">Transfer command buffer to submit.</param>
    public void SubmitTransferCommandBuffer(TransferCommandBuffer cmd) => _device.SubmitTransfer(cmd);

    /// <summary>Allocates a transient uniform buffer range from this execution's bump allocator.</summary>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    public DeviceBufferRange AllocateTransient(uint sizeInBytes) => _task.AllocateTransientInternal(sizeInBytes);

    /// <summary>
    /// Rents a scratch transient texture for this execution. Freed back to the pool once the dispatch's
    /// fence signals. Use for scratch targets not declared as graph resources.
    /// </summary>
    /// <param name="desc">Texture to rent.</param>
    public Texture GetTransientTexture(in GraphTextureDesc desc)
        => _device.RentTransientTexture(_task, ToTransientDesc(desc));

    /// <summary>Resolves a declared handle to its allocated render target.</summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default texture handle.", nameof(handle));

        if (_resolved.TryGetValue(handle.Id, out RenderTexture? existing))
            return existing;

        if (!_graph.Resources.TryGetValue(handle.Id, out GraphTextureDesc desc))
            throw new InvalidOperationException($"Texture handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        RenderTexture renderTexture = _device.RentTransientRenderTexture(_task, ToTransientDesc(desc));
        _resolved[handle.Id] = renderTexture;
        return renderTexture;
    }

    /// <summary>
    /// The window's swapchain render target for this view. Only populated if the present pass
    /// requested it via <see cref="PresentContextBuilder.RequestSwapchain"/> during setup; null
    /// otherwise, including when the device has no swapchain (offscreen dispatch).
    /// </summary>
    public Framebuffer? SwapchainTarget => _graph.PresentRequestsSwapchain ? _device.SwapchainFramebuffer : null;

    /// <summary>
    /// Requests the present so it fires when this dispatch finishes. Call from a present pass after drawing
    /// to the swapchain target. If nothing requests, view stays offscreen.
    /// </summary>
    public void Present() => _presentRequested = true;

    /// <summary>Pulls draw commands matching a query from the provider. Empty list if no provider.</summary>
    /// <param name="query">Query describing what to pull.</param>
    public IReadOnlyList<TDrawCommand> GetDrawCommands(RenderQuery query)
        => _provider?.GetDrawCommands(query) ?? Array.Empty<TDrawCommand>();

    /// <summary>Opens a nested timing region in the current pass. Pair with EndSample.</summary>
    /// <param name="name">Name of the sample.</param>
    public void BeginSample(string name) => _profiler?.BeginSample(name);

    /// <summary>Closes the most recently opened sample region.</summary>
    public void EndSample() => _profiler?.EndSample();

    /// <summary>Records one draw call for profiling. No-op if profiling is off.</summary>
    /// <param name="indexCount">Indices drawn.</param>
    /// <param name="instanceCount">Instances drawn.</param>
    public void RecordDrawCall(int indexCount, int instanceCount = 1) => _profiler?.RecordDrawCall(indexCount, instanceCount);

    private RenderTextureDescription ToTransientDesc(in GraphTextureDesc desc)
    {
        (int width, int height) = desc.Resolve(_view.PixelWidth, _view.PixelHeight);
        return new RenderTextureDescription(
            (uint)width,
            (uint)height,
            desc.ColorFormats ?? Array.Empty<PixelFormat>(),
            desc.EnableDepth,
            TextureSampleCount.Count1);
    }
}
