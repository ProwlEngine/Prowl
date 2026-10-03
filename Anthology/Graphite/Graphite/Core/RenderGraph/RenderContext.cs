using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Per-view context for passes. Fresh each view. Holds command buffers, transient textures, resolved targets.
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
    private readonly Dictionary<Texture, TextureState> _textureStates = new();
    private readonly Dictionary<DeviceBuffer, BufferSync> _bufferSyncs = new();
    private readonly List<TextureBarrier> _barriers = new();

    private PassInfo? _currentPass;
    private GraphResource[]? _currentPassOutputs;
    private ResourceAccess[]? _currentAccesses;
    private string? _currentScopeName;
    private bool _scopeTransitioned;
    private TextureBarrier[]? _deferredBarriers;
    private BufferAccess _deferredBufferSrc;
    private BufferAccess _deferredBufferDst;
    private CommandBuffer? _barrierHost;

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

    internal bool PresentRequested => _graph.WritesViewTarget && _view.TargetSwapchain && _device.SwapchainFramebuffer != null;

    internal bool HasViewTarget
    {
        get
        {
            if (_view.TargetSwapchain && _view.TargetFramebuffer != null)
                throw new InvalidOperationException($"View '{_view.Name}' sets both TargetFramebuffer and TargetSwapchain.");

            return _view.TargetSwapchain ? _device.SwapchainFramebuffer != null : _view.TargetFramebuffer != null;
        }
    }

    /// <summary>View being rendered.</summary>
    public TView View => _view;

    /// <summary>Device's profiler, null if none.</summary>
    public IProfiler? Profiler => _device.Profiler;

    internal void SetCurrentPass(in PassInfo? pass) => SetCurrentPass(pass, null, null, null);

    internal void SetCurrentPass(in PassInfo? pass, GraphResource[]? declaredOutputs, ResourceAccess[]? accesses, string? scopeName)
    {
        _currentPass = pass;
        _currentPassOutputs = declaredOutputs;
        _currentAccesses = accesses;
        _currentScopeName = scopeName;
        _scopeTransitioned = false;
    }

    internal void TransitionForAccesses(string scopeName, ResourceAccess[] accesses)
    {
        _barriers.Clear();
        BufferAccess bufferSrc = BufferAccess.None;
        BufferAccess bufferDst = BufferAccess.None;

        for (int i = 0; i < accesses.Length; i++)
        {
            ResourceAccess access = accesses[i];
            if (access.IsTexture)
            {
                if (!access.IsOutput && HasTextureOutput(accesses, access.Id))
                    continue;

                RenderTexture texture = GetRenderTexture(new TextureHandle(access.Id));
                foreach (Texture color in texture.ColorTextures)
                    AddTextureTransition(color, ResourceAccess.ToState(access.TextureInitial));
                if (texture.DepthTexture != null && access.DepthState(access.TextureInitial) is TextureState depthTarget)
                    AddTextureTransition(texture.DepthTexture, depthTarget);
            }
            else
            {
                DeviceBuffer buffer = GetRenderBuffer(new BufferHandle(access.Id));
                AddBufferAccess(buffer, access.BufferAccess, ref bufferSrc, ref bufferDst);
            }
        }

        RecordBarriers(scopeName, bufferSrc, bufferDst);
    }

    internal void RestoreRestingStates(string scopeName)
    {
        _barriers.Clear();
        foreach ((Texture texture, TextureState state) in _textureStates)
        {
            if (state != TextureState.Resting)
                _barriers.Add(new TextureBarrier(texture, state, TextureState.Resting));
        }

        _textureStates.Clear();
        CommandBufferBase.BumpGraphStateVersion();
        RecordBarriers(scopeName, BufferAccess.None, BufferAccess.None);
        FlushDeferredBarriers(scopeName);
    }

    /// <summary>
    /// Moves a declared texture to another of its declared kinds mid-pass, recording into the pass's only open command buffer.
    /// Throws if the pass holds zero or several open command buffers; use the overload taking a command buffer then.
    /// </summary>
    public void Transition(TextureHandle handle, TextureUsageKind usage)
    {
        if (_currentAccesses == null)
            throw new InvalidOperationException("Transition is only valid while a pass is rendering.");
        if (_pendingCommandBuffers.Count != 1)
        {
            throw new InvalidOperationException(
                $"Pass '{_currentScopeName}' holds {_pendingCommandBuffers.Count} open command buffers, so the transition target is ambiguous. Use Transition(cmd, handle, usage).");
        }

        Transition(_pendingCommandBuffers[0], handle, usage);
    }

    /// <summary>
    /// Moves a declared texture to another of its declared kinds mid-pass, recording the barrier into cmd.
    /// After a transition the pass must submit its command buffers in the order it rented them.
    /// </summary>
    public void Transition(CommandBuffer cmd, TextureHandle handle, TextureUsageKind usage)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (!handle.IsValid)
            throw new ArgumentException("Cannot transition a default texture handle.", nameof(handle));
        if (_currentAccesses == null)
            throw new InvalidOperationException("Transition is only valid while a pass is rendering.");
        if (!_pendingCommandBuffers.Contains(cmd))
            throw new InvalidOperationException("Transition needs a command buffer rented by the running pass and not yet submitted.");

        ResourceAccess access = FindTextureAccess(handle.Id);
        if (usage == 0 || (usage & (usage - 1)) != 0 || (access.TextureUsage & usage) == 0)
        {
            throw new ArgumentException(
                $"Pass '{_currentScopeName}' declared '{RenderResourceID.ToString(handle.Id)}' as {access.TextureUsage}; cannot transition it to {usage}.",
                nameof(usage));
        }

        RenderTexture texture = GetRenderTexture(handle);
        _barriers.Clear();
        foreach (Texture color in texture.ColorTextures)
            AddTextureTransition(color, ResourceAccess.ToState(usage));
        if (texture.DepthTexture != null && access.DepthUsage == null && access.DepthState(usage) is TextureState depthTarget)
            AddTextureTransition(texture.DepthTexture, depthTarget);

        _scopeTransitioned = true;
        if (_barriers.Count > 0)
            cmd.RecordBarriers(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_barriers), BufferAccess.None, BufferAccess.None);
        CommitBarrierStates();
    }

    private ResourceAccess FindTextureAccess(RenderResourceID id)
    {
        ResourceAccess? found = null;
        foreach (ResourceAccess access in _currentAccesses!)
        {
            if (!access.IsTexture || access.Id != id)
                continue;
            if (access.IsOutput)
                return access;
            found ??= access;
        }

        return found ?? throw new InvalidOperationException(
            $"Pass '{_currentScopeName}' uses texture '{RenderResourceID.ToString(id)}' without declaring it in Setup.");
    }

    private static bool HasTextureOutput(ResourceAccess[] accesses, RenderResourceID id)
    {
        foreach (ResourceAccess access in accesses)
        {
            if (access.IsTexture && access.IsOutput && access.Id == id)
                return true;
        }
        return false;
    }

    private void AddTextureTransition(Texture texture, TextureState target)
    {
        TextureState current = _textureStates.TryGetValue(texture, out TextureState state) ? state : TextureState.Resting;
        bool writes = target is TextureState.Storage or TextureState.Attachment or TextureState.TransferDst;
        if (current == target && !writes)
            return;

        _barriers.Add(new TextureBarrier(texture, current, target));
    }

    private void CommitBarrierStates()
    {
        foreach (TextureBarrier barrier in _barriers)
            _textureStates[barrier.Texture] = barrier.After;
        if (_barriers.Count > 0)
            CommandBufferBase.BumpGraphStateVersion();
        _barriers.Clear();
    }

    private void AddBufferAccess(DeviceBuffer buffer, BufferAccess access, ref BufferAccess src, ref BufferAccess dst)
    {
        if (!_bufferSyncs.TryGetValue(buffer, out BufferSync? sync))
        {
            sync = new BufferSync();
            _bufferSyncs[buffer] = sync;
        }

        BufferAccess writes = access & BufferAccess.AllWrites;
        BufferAccess reads = access & BufferAccess.AllReads;
        if (writes != BufferAccess.None)
        {
            src |= sync.LastWrite | sync.ReadsSinceWrite;
            dst |= access;
            sync.LastWrite = writes;
            sync.ReadsSinceWrite = reads;
            sync.Visible = access;
            return;
        }

        if (sync.LastWrite != BufferAccess.None && (reads & ~sync.Visible) != BufferAccess.None)
        {
            src |= sync.LastWrite;
            dst |= reads;
            sync.Visible |= reads;
        }
        sync.ReadsSinceWrite |= reads;
    }

    private void RecordBarriers(string scopeName, BufferAccess bufferSrc, BufferAccess bufferDst)
    {
        if (_barriers.Count == 0 && bufferSrc == BufferAccess.None)
            return;

        if (_task.OpenTail is { } tail)
        {
            tail.RecordBarriers(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_barriers), bufferSrc, bufferDst);
            CommitBarrierStates();
            return;
        }

        _deferredBarriers = _barriers.ToArray();
        _deferredBufferSrc = bufferSrc;
        _deferredBufferDst = bufferDst;
        CommitBarrierStates();
    }

    private void FlushDeferredBarriers(string scopeName)
    {
        if (_deferredBarriers == null || _barrierHost != null)
            return;

        SubmitCommandBuffer(GetCommandBuffer($"{scopeName} Barriers"));
    }

    private void CheckDeclared(RenderResourceID id)
    {
        if (_currentAccesses == null)
            return;

        foreach (ResourceAccess access in _currentAccesses)
        {
            if (access.Id == id)
                return;
        }

        throw new InvalidOperationException(
            $"Pass '{_currentScopeName}' uses resource '{RenderResourceID.ToString(id)}' without declaring it in Setup.");
    }

    private sealed class BufferSync
    {
        public BufferAccess LastWrite = BufferAccess.AllWrites;
        public BufferAccess ReadsSinceWrite = BufferAccess.AllReads;
        public BufferAccess Visible = BufferAccess.None;
    }

    /// <summary>
    /// True if profiler wants metadata via RecordPassMetadata. Check before building one, it's wasted work otherwise.
    /// </summary>
    public bool WantsMetadata => Profiler?.RequestMetadata ?? false;

    /// <summary>
    /// Attaches caller metadata to the open pass. Only valid mid-pass; no-op otherwise.
    /// </summary>
    public void RecordPassMetadata(object metadata)
    {
        if (_currentPass is { } pass)
            Profiler?.RecordPassMetadata(pass, metadata);
    }

    /// <summary>
    /// Rents an extra command buffer, already begun, for passes that need more than the one Render receives. Submit via SubmitCommandBuffer. Do not begin or end it yourself.
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
        cb.GraphStates = _textureStates;
        _pendingCommandBuffers.Add(cb);

        if (_deferredBarriers != null && _barrierHost == null)
        {
            cb.RecordBarriers(_deferredBarriers, _deferredBufferSrc, _deferredBufferDst);
            _barrierHost = cb;
        }

        return cb;
    }

    internal CommandBuffer BeginPassCommandBuffer(string passName) => GetCommandBuffer(passName);

    internal void EndPassCommandBuffer(CommandBuffer cmd)
    {
        if (_pendingCommandBuffers.Contains(cmd))
            SubmitCommandBuffer(cmd);
    }

    /// <summary>Queues a command buffer rented here for this execution's submit. Do not record into it afterwards.</summary>
    /// <param name="cmd">Command buffer to submit.</param>
    public void SubmitCommandBuffer(CommandBuffer cmd)
    {
        if (_scopeTransitioned && _pendingCommandBuffers.Count > 0 && !ReferenceEquals(_pendingCommandBuffers[0], cmd))
        {
            throw new InvalidOperationException(
                $"Pass '{_currentScopeName}' called Transition, so its command buffers must be submitted in the order they were rented.");
        }

        if (_barrierHost != null)
        {
            if (!ReferenceEquals(_barrierHost, cmd))
            {
                throw new InvalidOperationException(
                    $"Pass '{_currentScopeName}' must submit its first rented command buffer first, because it carries the barriers the pass starts with.");
            }

            _barrierHost = null;
            _deferredBarriers = null;
        }

        _pendingCommandBuffers.Remove(cmd);
        _task.QueueOpen(cmd);
    }

    /// <summary>
    /// Warns and drops command buffers rented in this scope but never submitted. Ring disposes them on recycle. Called after each pass.
    /// </summary>
    /// <param name="scopeName">Pass name for the warning.</param>
    internal void ReclaimUnsubmittedCommandBuffers(string scopeName)
    {
        foreach (CommandBuffer cb in _pendingCommandBuffers)
        {
            _device.OnWarning?.Invoke(
                $"Command buffer '{cb.Name}' rented by pass '{scopeName}' was never submitted. " +
                "Rent a command buffer only when you intend to submit it through the render context.");
        }

        _pendingCommandBuffers.Clear();
        _barrierHost = null;
        FlushDeferredBarriers(scopeName);
    }

    /// <summary>
    /// Rents a transfer command buffer, copies only, already begun. Submit via SubmitTransferCommandBuffer. Pooled and reclaimed when the execution retires, don't dispose it.
    /// </summary>
    /// <param name="name">Optional debug name.</param>
    public TransferCommandBuffer GetTransferCommandBuffer(string name = "")
    {
        TransferCommandBuffer cb = _device.RentGraphTransferCommandBuffer();
        _task.TrackRentedTransferCommandBuffer(cb);
        cb.Name = name;
        cb.Begin();
        cb.GraphStates = _textureStates;
        return cb;
    }

    /// <summary>Submits a transfer command buffer, non-blocking.</summary>
    /// <param name="cmd">Transfer command buffer to submit.</param>
    public void SubmitTransferCommandBuffer(TransferCommandBuffer cmd)
    {
        if (_barrierHost != null)
        {
            throw new InvalidOperationException(
                $"Pass '{_currentScopeName}' must submit its first rented command buffer before a transfer, because it carries the barriers the pass starts with.");
        }

        FlushDeferredBarriers(_currentScopeName ?? "Transfer");
        _task.CloseTail();
        _task.FlushSubmissions();
        _device.SubmitTransfer(cmd);
    }

    /// <summary>Allocates a transient uniform buffer range from this execution's bump allocator.</summary>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    public DeviceBufferRange AllocateTransient(uint sizeInBytes) => _task.AllocateTransientInternal(sizeInBytes);

    /// <summary>Resolves a declared texture handle to its allocated render target.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle) => GetRenderTexture(handle, 0);

    /// <summary>
    /// Resolves a texture handle by age. 0 is current, higher values are older history copies up to declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder.</param>
    /// <param name="framesAgo">Executions back; 0 is current.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle, int framesAgo)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default texture handle.", nameof(handle));

        CheckDeclared(handle.Id);

        if (framesAgo == 0 && _resolved.TryGetValue(handle.Id, out RenderTexture? existing))
            return existing;

        if (!_graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Texture handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        switch (resource)
        {
            case GraphViewTargetResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "The view target has no history.");
                if (_view.TargetSwapchain && _view.TargetFramebuffer != null)
                    throw new InvalidOperationException($"View '{_view.Name}' sets both TargetFramebuffer and TargetSwapchain.");
                Framebuffer viewTarget = (_view.TargetSwapchain ? _device.SwapchainFramebuffer : _view.TargetFramebuffer)
                    ?? throw new InvalidOperationException($"A pass resolved the view target, but view '{_view.Name}' has none.");
                RenderTexture target = new(viewTarget);
                _resolved[handle.Id] = target;
                return target;

            case GraphImportedTextureResource imported:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "An imported texture has no history.");
                _resolved[handle.Id] = imported.Texture;
                return imported.Texture;

            case GraphTextureResource { HistoryDepth: 0 } textureResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Resource '{RenderResourceID.ToString(handle.Id)}' was not declared with history.");
                RenderTexture rented = _device.RentTransientRenderTexture(_task, ToTransientDesc(textureResource));
                _resolved[handle.Id] = rented;
                return rented;

            case GraphTextureResource historyResource:
                RenderTexture copy = historyResource.ResolveHistory(_device, _view.ViewId, _task.Id, framesAgo, ToTransientDesc(historyResource));
                if (framesAgo == 0)
                    _resolved[handle.Id] = copy;
                return copy;

            default:
                throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(handle.Id)}' is not a texture. Resolve it with GetRenderBuffer.");
        }
    }

    /// <summary>True once this view's history ring holds an earlier execution. False on a view's first execution and after a resize reallocates its ring.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public bool IsHistoryValid(TextureHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default texture handle.", nameof(handle));

        if (!_graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Texture handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        return resource is GraphTextureResource texture
            && texture.IsHistoryValid(_view.ViewId, _task.Id, ToTransientDesc(texture));
    }

    /// <summary>Resolves a declared buffer handle to its allocated device buffer.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle) => GetRenderBuffer(handle, 0);

    /// <summary>
    /// Resolves a buffer handle by age. 0 is current, higher values are older history copies up to declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder.</param>
    /// <param name="framesAgo">Executions back; 0 is current.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle, int framesAgo)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default buffer handle.", nameof(handle));

        CheckDeclared(handle.Id);

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

        DeviceBuffer copy = bufferResource.ResolveHistory(_device, _view.ViewId, _task.Id, framesAgo, bufferResource.Description.ToBufferDescription());
        if (framesAgo == 0)
            _resolvedBuffers[handle.Id] = copy;
        return copy;
    }

    /// <summary>True once this view's history ring holds an earlier execution. False on a view's first execution and after a resize reallocates its ring.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public bool IsHistoryValid(BufferHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default buffer handle.", nameof(handle));

        if (!_graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Buffer handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        return resource is GraphBufferResource buffer
            && buffer.IsHistoryValid(_view.ViewId, _task.Id, buffer.Description.ToBufferDescription());
    }

    internal bool IsTextureResource(RenderResourceID id)
        => _graph.Resources.TryGetValue(id, out GraphResource? resource)
            && resource is GraphTextureResource or GraphImportedTextureResource or GraphViewTargetResource;

    internal TargetLoadStoreOps GetTargetOps(RenderResourceID id)
    {
        if (_currentPassOutputs != null)
        {
            foreach (GraphResource declared in _currentPassOutputs)
            {
                if (declared.Id != id)
                    continue;

                switch (declared)
                {
                    case GraphTextureResource texture:
                        return texture.Ops;
                    case GraphImportedTextureResource imported:
                        return imported.Ops;
                    case GraphViewTargetResource viewTarget:
                        return viewTarget.Ops;
                }
            }
        }

        if (!_graph.Resources.TryGetValue(id, out GraphResource? resource))
            throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' was not declared by any pass in this graph.");

        return resource switch
        {
            GraphTextureResource texture => texture.Ops,
            GraphImportedTextureResource imported => imported.Ops,
            GraphViewTargetResource viewTarget => viewTarget.Ops,
            _ => throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' is not a render target.")
        };
    }

    /// <summary>Resolves a texture or buffer handle to what the profiler should see for a pass read.</summary>
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

    private RenderTextureDescription ToTransientDesc(GraphTextureResource resource)
    {
        GraphTextureDesc desc = resource.Description;
        (int width, int height) = desc.Resolve(_view.PixelWidth, _view.PixelHeight);
        return new RenderTextureDescription(
            (uint)width,
            (uint)height,
            desc.ColorFormats ?? Array.Empty<PixelFormat>(),
            desc.EnableDepth,
            TextureSampleCount.Count1,
            resource.Storage);
    }
}
