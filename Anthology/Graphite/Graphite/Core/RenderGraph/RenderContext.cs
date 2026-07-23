using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Per-view context passed to passes and present pass. Fresh each view. Holds view, command buffers,
/// transient textures, resolved render targets for this execution.
/// </summary>
public sealed class RenderContext<TView>
    where TView : IRenderView
{
    private readonly GraphicsDevice _device;
    private readonly ExecutionTask _task;
    private readonly RenderGraph<TView> _graph;
    private readonly TView _view;
    private readonly Dictionary<RenderResourceID, RenderTexture> _resolved = new();
    private readonly Dictionary<RenderResourceID, DeviceBuffer> _resolvedBuffers = new();
    private readonly List<CommandBuffer> _pendingCommandBuffers = new();

    private bool _presentRequested;
    private PassInfo? _currentPass;

    private static long s_nextCommandBufferRentalId;

    internal RenderContext(
        GraphicsDevice device,
        ExecutionTask task,
        RenderGraph<TView> graph,
        TView view)
    {
        _device = device;
        _task = task;
        _graph = graph;
        _view = view;
    }

    /// <summary>Execution this context records into.</summary>
    public ExecutionTask Task => _task;

    /// <summary>True once present pass armed the swapchain present.</summary>
    public bool RequestPresent => _presentRequested;

    /// <summary>View being rendered.</summary>
    public TView View => _view;

    /// <summary>Device's profiler, null if none attached.</summary>
    public IProfiler? Profiler => _device.Profiler;

    /// <summary>Sets the pass currently rendering, stamped on command buffers rented after this. Null outside a pass.</summary>
    internal void SetCurrentPass(in PassInfo? pass) => _currentPass = pass;

    /// <summary>
    /// Rents a command buffer, already begun, ready to record. Submit via SubmitCommandBuffer when done.
    /// Don't begin/end it yourself.
    /// </summary>
    /// <param name="name">Optional debug name.</param>
    public CommandBuffer GetCommandBuffer(string name = "")
    {
        CommandBuffer cb = _device.RentGraphCommandBuffer();

        cb.Execution = _task;
        cb.Pass = _currentPass;
        cb.RentalId = (ulong)System.Threading.Interlocked.Increment(ref s_nextCommandBufferRentalId);
        _task.TrackRentedCommandBuffer(cb);
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        cb.Begin();
        _pendingCommandBuffers.Add(cb);

        return cb;
    }

    /// <summary>Ends and submits a command buffer rented from this context.</summary>
    /// <param name="cmd">Command buffer to submit.</param>
    public void SubmitCommandBuffer(CommandBuffer cmd)
    {
        _pendingCommandBuffers.Remove(cmd);
        cmd.End();
        _task.SubmitCommandsInternal(cmd);
    }

    /// <summary>
    /// Warns and releases command buffers rented in the named scope but never submitted. Left
    /// begun-but-unsubmitted; ring disposes on recycle. Called by pipeline after each pass and present pass.
    /// </summary>
    /// <param name="scopeName">Pass name for the warning message.</param>
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

    /// <summary>Rents a transfer command buffer, buffer/texture copies only.</summary>
    /// <param name="name">Optional debug name.</param>
    public TransferCommandBuffer GetTransferCommandBuffer(string name = "")
    {
        TransferCommandBuffer cb = _device.ResourceFactory.CreateTransferCommandBuffer();

        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        return cb;
    }

    /// <summary>Submits a transfer command buffer, non-blocking.</summary>
    /// <param name="cmd">Transfer command buffer to submit.</param>
    public void SubmitTransferCommandBuffer(TransferCommandBuffer cmd) => _device.SubmitTransfer(cmd);

    /// <summary>Allocates a transient uniform buffer range from this execution's bump allocator.</summary>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    public DeviceBufferRange AllocateTransient(uint sizeInBytes) => _task.AllocateTransientInternal(sizeInBytes);

    /// <summary>
    /// Rents a scratch transient texture, freed once the dispatch's fence signals. For scratch targets not
    /// declared as graph resources.
    /// </summary>
    /// <param name="desc">Texture to rent.</param>
    public Texture GetTransientTexture(in GraphTextureDesc desc)
        => _device.RentTransientTexture(_task, ToTransientDesc(desc));

    /// <summary>Resolves a declared texture handle to its allocated render target.</summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle) => GetRenderTexture(handle, 0);

    /// <summary>
    /// Resolves a texture handle by age. 0 is the current write target, higher values resolve older
    /// copies of a history resource, up to its declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    /// <param name="framesAgo">Executions back to resolve; 0 is current.</param>
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

    /// <summary>Resolves a declared buffer handle to its allocated device buffer.</summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle) => GetRenderBuffer(handle, 0);

    /// <summary>
    /// Resolves a buffer handle by age. 0 is the current write target, higher values resolve older
    /// copies of a history resource, up to its declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder during setup.</param>
    /// <param name="framesAgo">Executions back to resolve; 0 is current.</param>
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
    /// Window's swapchain target for this view. Null unless the present pass requested it via
    /// RequestSwapchain during setup, or if the device has no swapchain (offscreen dispatch).
    /// </summary>
    public Framebuffer? SwapchainTarget => _graph.PresentRequestsSwapchain ? _device.SwapchainFramebuffer : null;

    /// <summary>Requests present when this dispatch finishes. Call from present pass after drawing to swapchain. No call means view stays offscreen.</summary>
    public void Present() => _presentRequested = true;

    /// <summary>Opens a nested timing region in the current pass. Pair with EndSample.</summary>
    /// <param name="name">Name of the sample.</param>
    public void BeginSample(string name) => _device.Profiler?.BeginSample(name);

    /// <summary>Closes the most recently opened sample region.</summary>
    public void EndSample() => _device.Profiler?.EndSample();

    /// <summary>Resolves a texture or buffer handle to the resource the profiler should see for a pass read.</summary>
    internal void ResolveForProfiler(RenderResourceID resource, out RenderTexture? texture, out DeviceBuffer? buffer)
    {
        if (IsTextureResource(resource))
        {
            texture = GetRenderTexture(new TextureHandle(resource));
            buffer = null;
        }
        else
        {
            texture = null;
            buffer = GetRenderBuffer(new BufferHandle(resource));
        }
    }

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
