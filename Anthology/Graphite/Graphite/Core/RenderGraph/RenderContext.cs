using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Per-view render context passed to passes and the present pass. Fresh per view. Holds the view,
/// command buffers, transient textures, and resolved render targets for this execution.
/// </summary>
public sealed class RenderContext<TView>
    where TView : IRenderView
{
    private readonly GraphicsDevice _device;
    private readonly ExecutionTask _task;
    private readonly RenderGraph<TView> _graph;
    private readonly TView _view;
    private readonly IPassProfiler? _profiler;
    private readonly Dictionary<RenderResourceID, RenderTexture> _resolved = new();
    private readonly Dictionary<RenderResourceID, DeviceBuffer> _resolvedBuffers = new();
    private readonly List<CommandBuffer> _pendingCommandBuffers = new();

    private bool _presentRequested;

    internal RenderContext(
        GraphicsDevice device,
        ExecutionTask task,
        RenderGraph<TView> graph,
        TView view,
        IPassProfiler? profiler)
    {
        _device = device;
        _task = task;
        _graph = graph;
        _view = view;
        _profiler = profiler;
    }

    /// <summary>The execution this context records into.</summary>
    public ExecutionTask Task => _task;

    /// <summary>True once the present pass has armed the swapchain present.</summary>
    public bool RequestPresent => _presentRequested;

    /// <summary>The view being rendered.</summary>
    public TView View => _view;

    /// <summary>Profiler for this execution, or null if profiling is off.</summary>
    public IPassProfiler? Profiler => _profiler;

    /// <summary>
    /// Rents a command buffer for this pass to record into. The buffer is already begun and ready to record;
    /// submit it back through <see cref="SubmitCommandBuffer"/> when done. Do not begin or end it yourself.
    /// </summary>
    /// <param name="name">Optional debug name.</param>
    public CommandBuffer GetCommandBuffer(string name = "")
    {
        CommandBuffer cb = _device.RentGraphCommandBuffer();

        cb.Execution = _task;
        _task.TrackRentedCommandBuffer(cb);
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        cb.Begin();
        _pendingCommandBuffers.Add(cb);

        return cb;
    }

    /// <summary>Ends and submits a recorded command buffer rented from this context.</summary>
    /// <param name="cmd">Command buffer to submit.</param>
    public void SubmitCommandBuffer(CommandBuffer cmd)
    {
        _pendingCommandBuffers.Remove(cmd);
        cmd.End();
        _task.SubmitCommandsInternal(cmd);
    }

    /// <summary>
    /// Warns for and releases any command buffer rented from this context during the named scope but never
    /// submitted. The buffer stays begun-but-unsubmitted, so the execution ring disposes it when its slot
    /// recycles. Called by the pipeline after each pass and the present pass.
    /// </summary>
    /// <param name="scopeName">Pass name used in the warning message.</param>
    internal void ReclaimUnsubmittedCommandBuffers(string scopeName)
    {
        if (_pendingCommandBuffers.Count == 0)
            return;

        foreach (CommandBuffer cb in _pendingCommandBuffers)
        {
            _device.OnWarning?.Invoke(
                $"Command buffer '{cb.Name}' rented by pass '{scopeName}' was never submitted. " +
                "Rent a command buffer only when you intend to submit it through the render context.");
        }

        _pendingCommandBuffers.Clear();
    }

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

    /// <summary>Resolves a declared texture handle to its current allocated render target.</summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle) => GetRenderTexture(handle, 0);

    /// <summary>
    /// Resolves a declared texture handle by age. <paramref name="framesAgo"/> 0 is the current write target;
    /// higher values resolve prior executions' copies of a history resource, up to its declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    /// <param name="framesAgo">How many executions back to resolve; 0 is the current write target.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle, int framesAgo)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default texture handle.", nameof(handle));

        if (framesAgo == 0 && _resolved.TryGetValue(handle.Id, out RenderTexture? existing))
            return existing;

        if (!_graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Texture handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        switch (resource)
        {
            case GraphImportedTextureResource imported:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "An imported texture has no history.");
                _resolved[handle.Id] = imported.Texture;
                return imported.Texture;

            case GraphTextureResource { HistoryDepth: 0 } textureResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Resource '{RenderResourceID.ToString(handle.Id)}' was not declared with history.");
                RenderTexture rented = _device.RentTransientRenderTexture(_task, ToTransientDesc(textureResource.Description));
                _resolved[handle.Id] = rented;
                return rented;

            case GraphTextureResource historyResource:
                RenderTexture copy = historyResource.ResolveHistory(_device, _task.Id, framesAgo, ToTransientDesc(historyResource.Description));
                if (framesAgo == 0)
                    _resolved[handle.Id] = copy;
                return copy;

            default:
                throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(handle.Id)}' is not a texture. Resolve it with GetRenderBuffer.");
        }
    }

    /// <summary>Resolves a declared buffer handle to its current allocated device buffer.</summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle) => GetRenderBuffer(handle, 0);

    /// <summary>
    /// Resolves a declared buffer handle by age. <paramref name="framesAgo"/> 0 is the current write target;
    /// higher values resolve prior executions' copies of a history resource, up to its declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    /// <param name="framesAgo">How many executions back to resolve; 0 is the current write target.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle, int framesAgo)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default buffer handle.", nameof(handle));

        if (framesAgo == 0 && _resolvedBuffers.TryGetValue(handle.Id, out DeviceBuffer? existing))
            return existing;

        if (!_graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Buffer handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        if (resource is not GraphBufferResource bufferResource)
            throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(handle.Id)}' is not a buffer. Resolve it with GetRenderTexture.");

        if (bufferResource.HistoryDepth == 0)
        {
            if (framesAgo != 0)
                throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Resource '{RenderResourceID.ToString(handle.Id)}' was not declared with history.");
            DeviceBuffer rented = _device.RentTransientBuffer(_task, bufferResource.Description.ToBufferDescription());
            _resolvedBuffers[handle.Id] = rented;
            return rented;
        }

        DeviceBuffer copy = bufferResource.ResolveHistory(_device, _task.Id, framesAgo, bufferResource.Description.ToBufferDescription());
        if (framesAgo == 0)
            _resolvedBuffers[handle.Id] = copy;
        return copy;
    }

    internal bool IsTextureResource(RenderResourceID id)
        => _graph.Resources.TryGetValue(id, out GraphResource? resource)
            && resource is GraphTextureResource or GraphImportedTextureResource;

    internal TargetLoadStoreOps GetTargetOps(RenderResourceID id)
    {
        if (!_graph.Resources.TryGetValue(id, out GraphResource? resource))
            throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' was not declared by any pass in this graph.");

        return resource switch
        {
            GraphTextureResource texture => texture.Ops,
            GraphImportedTextureResource imported => imported.Ops,
            _ => throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' is not a render target.")
        };
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

    /// <summary>Opens a nested timing region in the current pass. Pair with EndSample.</summary>
    /// <param name="name">Name of the sample.</param>
    public void BeginSample(string name) => _profiler?.BeginSample(name);

    /// <summary>Closes the most recently opened sample region.</summary>
    public void EndSample() => _profiler?.EndSample();

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
