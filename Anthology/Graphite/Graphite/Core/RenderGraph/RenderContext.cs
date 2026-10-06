using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Per-view context for passes. Fresh each view. Holds command buffers, transient textures, resolved targets.
/// </summary>
public sealed class RenderContext
{
    private readonly GraphicsDevice _device;
    private readonly ExecutionTask _task;
    private readonly RenderGraph _graph;
    private readonly IRenderView _view;
    private readonly Dictionary<RenderResourceID, RenderTexture> _resolved = new();
    private readonly Dictionary<RenderResourceID, DeviceBuffer> _resolvedBuffers = new();
    private readonly Dictionary<Texture, TextureState> _textureStates = new();
    private GraphTextureStates? _stateSnapshot;
    private readonly Dictionary<DeviceBuffer, BufferSync> _bufferSyncs = new();
    private readonly List<TextureBarrier> _barriers = new();
    private readonly HashSet<RenderResourceID> _enteredTransients = new();
    private readonly HashSet<Texture> _discardedTextures = new();

    private PassInfo? _currentPass;
    private ResourceAccess[]? _currentAccesses;
    private string? _currentScopeName;
    private BufferAccess _pendingBufferSrc;
    private BufferAccess _pendingBufferDst;

    private static long s_nextCommandBufferRentalId;

    internal RenderContext(
        GraphicsDevice device,
        ExecutionTask task,
        RenderGraph graph,
        IRenderView view)
    {
        _device = device;
        _task = task;
        _graph = graph;
        _view = view;
    }

    /// <summary>Execution this context records into.</summary>
    public ExecutionTask Task => _task;

    internal Swapchain? PresentSwapchain => _graph.WritesViewTarget ? _view.Target?.OwningSwapchain : null;

    internal bool HasViewTarget => _view.Target != null;

    /// <summary>View being rendered.</summary>
    public IRenderView View => _view;

    /// <summary>View being rendered as its concrete type.</summary>
    public T ViewAs<T>() where T : IRenderView => (T)_view;

    internal IGraphProfiler? GraphProfiler => _device.GraphProfiler;

    internal void SetCurrentPass(in PassInfo? pass) => SetCurrentPass(pass, null, null);

    internal void SetCurrentPass(in PassInfo? pass, ResourceAccess[]? accesses, string? scopeName)
    {
        _currentPass = pass;
        _currentAccesses = accesses;
        _currentScopeName = scopeName;
    }

    internal void TransitionForAccesses(ResourceAccess[] accesses)
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
                bool fromUndefined = IsTransient(access.Id) && _enteredTransients.Add(access.Id);
                foreach (Texture color in texture.ColorTextures)
                    AddTextureTransition(color, access.TextureUsage, fromUndefined);
                if (texture.DepthTexture != null && access.DepthState(access.TextureUsage) is TextureState depthTarget)
                    AddTextureTransition(texture.DepthTexture, depthTarget, fromUndefined);
            }
            else
            {
                DeviceBuffer buffer = GetRenderBuffer(new BufferHandle(access.Id));
                AddBufferAccess(buffer, access.BufferUsage, ref bufferSrc, ref bufferDst);
            }
        }

        _pendingBufferSrc = bufferSrc;
        _pendingBufferDst = bufferDst;
    }

    internal void RestoreRestingStates(string scopeName)
    {
        _barriers.Clear();
        foreach ((Texture texture, TextureState state) in _textureStates)
        {
            if (!_discardedTextures.Contains(texture))
                _barriers.Add(new TextureBarrier(texture, state, null));
        }

        _textureStates.Clear();
        _discardedTextures.Clear();
        _enteredTransients.Clear();
        _stateSnapshot = null;
        _pendingBufferSrc = BufferAccess.None;
        _pendingBufferDst = BufferAccess.None;
        if (_barriers.Count == 0)
            return;

        CommandBuffer cb = BeginCommandBuffer($"{scopeName} Barriers");
        cb.RecordBarriers(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_barriers), BufferAccess.None, BufferAccess.None);
        _barriers.Clear();
        EndCommandBuffer(cb);
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

    private bool IsTransient(RenderResourceID id)
        => _graph.Resources.TryGetValue(id, out GraphResource? resource) && resource is GraphTextureResource { HistoryDepth: 0 };

    private void AddTextureTransition(Texture texture, TextureState target, bool fromUndefined)
    {
        TextureState? current = null;
        if (fromUndefined)
            _discardedTextures.Add(texture);
        else if (_textureStates.TryGetValue(texture, out TextureState state))
            current = state;

        bool writes = target is TextureState.Storage or TextureState.Attachment or TextureState.TransferDst;
        if (current == target && !writes)
            return;

        _barriers.Add(new TextureBarrier(texture, current, target, fromUndefined));
    }

    private void CommitBarrierStates()
    {
        foreach (TextureBarrier barrier in _barriers)
        {
            if (barrier.After is TextureState after)
                _textureStates[barrier.Texture] = after;
            else
                _textureStates.Remove(barrier.Texture);
        }
        if (_barriers.Count > 0)
            _stateSnapshot = null;
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

    internal CommandBuffer BeginCommandBuffer(string name)
    {
        CommandBuffer cb = _device.RentGraphCommandBuffer(_task);

        cb.Execution = _task;
        cb.Pass = _currentPass;
        cb.RentalId = (ulong)System.Threading.Interlocked.Increment(ref s_nextCommandBufferRentalId);
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        cb.Begin();
        cb.GraphStates = _textureStates.Count == 0 ? null : (_stateSnapshot ??= new GraphTextureStates(_textureStates));
        return cb;
    }

    internal CommandBuffer BeginPassCommandBuffer(string passName)
    {
        CommandBuffer cb = BeginCommandBuffer(passName);
        if (_barriers.Count == 0 && _pendingBufferSrc == BufferAccess.None)
            return cb;

        cb.RecordBarriers(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_barriers), _pendingBufferSrc, _pendingBufferDst);
        _pendingBufferSrc = BufferAccess.None;
        _pendingBufferDst = BufferAccess.None;
        CommitBarrierStates();
        cb.GraphStates = _textureStates.Count == 0 ? null : (_stateSnapshot ??= new GraphTextureStates(_textureStates));
        return cb;
    }

    internal void BindDeclaredTarget(CommandBuffer cmd, ResourceAccess[] accesses)
    {
        RenderResourceID target = default;
        bool found = false;
        foreach (ResourceAccess access in accesses)
        {
            if (!access.IsTexture || !access.IsOutput || access.TextureUsage != TextureState.Attachment)
                continue;

            if (found && access.Id != target)
                return;

            target = access.Id;
            found = true;
        }

        if (!found)
            return;

        cmd.SetFramebuffer(GetRenderTexture(new TextureHandle(target)).Framebuffer, GetTargetOps(target));
    }

    internal void EndCommandBuffer(CommandBuffer cmd) => _task.SubmitRecorded(cmd);

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
            case GraphViewTargetResource viewTargetResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "The view target has no history.");
                Framebuffer viewTarget = _view.Target
                    ?? throw new InvalidOperationException($"A pass resolved the view target, but view '{_view.Name}' has none.");
                if (viewTargetResource.DepthFormat is PixelFormat requiredDepth)
                    viewTarget.OwningSwapchain?.RequireDepth(requiredDepth);
                if (viewTargetResource.DepthFormat != null && viewTarget.DepthTarget == null)
                    throw new InvalidOperationException($"A pass declared a view target depth attachment, but view '{_view.Name}' has a Target without one.");
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
                RenderTexture rented = _device.RentGraphTransientRenderTexture(_task, ToTransientDesc(textureResource));
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
        if (_currentAccesses != null)
        {
            foreach (ResourceAccess access in _currentAccesses)
            {
                if (access.Id != id || access.Description is not { } declared)
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
        PixelFormat[] colors = desc.ColorFormats ?? Array.Empty<PixelFormat>();
        if (desc.EnableDepth && desc.DepthFormat is PixelFormat depthFormat)
            return new RenderTextureDescription((uint)width, (uint)height, colors, depthFormat, TextureSampleCount.Count1, resource.Storage);

        return new RenderTextureDescription(
            (uint)width,
            (uint)height,
            colors,
            desc.EnableDepth,
            TextureSampleCount.Count1,
            resource.Storage);
    }
}
