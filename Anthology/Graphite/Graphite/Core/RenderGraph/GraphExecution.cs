using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

internal sealed partial class GraphExecution
{
    private readonly Dictionary<Texture, TextureState> _textureStates = new();
    private GraphTextureStates? _stateSnapshot;
    private GraphTextureStates? _passStates;
    private readonly Dictionary<DeviceBuffer, BufferSync> _bufferSyncs = new();
    private readonly List<TextureBarrier> _barriers = new();
    private readonly HashSet<Texture> _enteredTransients = new();
    private readonly HashSet<Texture> _discardedTextures = new();
    private readonly RenderTexture?[] _textures;
    private readonly DeviceBuffer?[] _buffers;
    private BufferAccess _pendingBufferSrc;
    private BufferAccess _pendingBufferDst;

    private static long s_nextCommandBufferRentalId;

    public GraphExecution(GraphicsDevice device, ExecutionTask task, FrameGraph graph)
    {
        Device = device;
        Task = task;
        Graph = graph;
        _textures = new RenderTexture?[graph.ResourceCount];
        _buffers = new DeviceBuffer?[graph.ResourceCount];
        Validate = device.ValidationEnabled;
    }

    public GraphicsDevice Device { get; }

    public ExecutionTask Task { get; }

    public FrameGraph Graph { get; }

    public PassBindings Bindings { get; } = new();

    public bool Validate { get; }

    public void Prepare()
    {
        Graph.Store.Sweep(Task.Id);
        for (int i = 0; i < Graph.ResourceCount; i++)
        {
            FrameResource resource = Graph.ResourceAt(i);
            if (resource.IsRetained)
            {
                if (resource.IsTexture)
                    _textures[i] = resource.TextureRings!.Resolve(Device, resource.Scope, Task.Id, 0, Allocation(resource));
                else
                    _buffers[i] = resource.BufferRings!.Resolve(Device, resource.Scope, Task.Id, 0, resource.Buffer.ToBufferDescription());
            }
            else if (resource.Imported)
            {
                if (resource.IsTarget)
                    _textures[i] = ResolveTarget(resource);
                else if (resource.IsTexture)
                    _textures[i] = resource.ImportedTexture;
                else
                    _buffers[i] = resource.ImportedBuffer;
            }

            if (Validate)
                Know(i);
        }

        if (Graph.Store.HasResizes)
        {
            CommandBuffer cb = BeginCommandBuffer("Graph Resizes", null);
            Graph.Store.RecordResizes(cb);
            EndCommandBuffer(cb);
        }
    }

    private void Know(int index)
    {
        if (_textures[index] is { } texture)
            Bindings.Know(texture);
        else if (_buffers[index] is { } buffer)
            Bindings.Known.Add(buffer.ResourceId);
    }

    private static RenderTexture ResolveTarget(FrameResource resource)
    {
        Framebuffer target = resource.TargetFramebuffer!;
        if (resource.TargetDepth is PixelFormat depth)
            target.OwningSwapchain?.RequireDepth(depth);
        if (resource.TargetDepth != null && target.DepthTarget == null)
            throw new InvalidOperationException($"Target '{resource.Name}' was imported with a depth format, but its framebuffer has no depth attachment.");
        return new RenderTexture(target);
    }

    private static RenderTextureDescription Allocation(FrameResource resource)
    {
        GraphTextureDesc desc = resource.Texture;
        PixelFormat[] colors = desc.ColorFormats ?? Array.Empty<PixelFormat>();
        if (desc.EnableDepth && desc.DepthFormat is PixelFormat depthFormat)
            return new RenderTextureDescription((uint)desc.Width, (uint)desc.Height, colors, depthFormat, desc.SampleCount, resource.Storage) { ArrayLayers = desc.ArrayLayers, MipLevels = desc.MipLevels, Cube = desc.Cube };

        return new RenderTextureDescription((uint)desc.Width, (uint)desc.Height, colors, desc.EnableDepth, desc.SampleCount, resource.Storage) { ArrayLayers = desc.ArrayLayers, MipLevels = desc.MipLevels, Cube = desc.Cube };
    }

    public RenderTexture TextureAt(int index)
    {
        if (_textures[index] is { } existing)
            return existing;

        FrameResource resource = Graph.ResourceAt(index);
        if (!resource.IsTexture)
            throw new InvalidOperationException($"Resource '{resource.Name}' is not a texture.");

        RenderTexture rented = Device.RentGraphTransientRenderTexture(Task, Allocation(resource));
        _textures[index] = rented;
        if (Validate)
            Bindings.Know(rented);
        return rented;
    }

    public DeviceBuffer BufferAt(int index)
    {
        if (_buffers[index] is { } existing)
            return existing;

        FrameResource resource = Graph.ResourceAt(index);
        if (resource.IsTexture)
            throw new InvalidOperationException($"Resource '{resource.Name}' is not a buffer.");

        DeviceBuffer rented = Device.RentTransientBuffer(Task, resource.Buffer.ToBufferDescription());
        _buffers[index] = rented;
        if (Validate)
            Bindings.Known.Add(rented.ResourceId);
        return rented;
    }

    public RenderTexture? HistoryTexture(int index, int framesAgo)
    {
        FrameResource resource = Graph.ResourceAt(index);
        CheckHistory(resource, framesAgo);
        RenderTextureDescription desc = Allocation(resource);
        if (!resource.TextureRings!.IsValid(resource.Scope, desc))
            return null;

        RenderTexture texture = resource.TextureRings.Resolve(Device, resource.Scope, Task.Id, framesAgo, desc);
        if (Validate)
            Bindings.Know(texture);
        return texture;
    }

    public DeviceBuffer? HistoryBuffer(int index, int framesAgo)
    {
        FrameResource resource = Graph.ResourceAt(index);
        CheckHistory(resource, framesAgo);
        BufferDescription desc = resource.Buffer.ToBufferDescription();
        if (!resource.BufferRings!.IsValid(resource.Scope, desc))
            return null;

        DeviceBuffer buffer = resource.BufferRings.Resolve(Device, resource.Scope, Task.Id, framesAgo, desc);
        if (Validate)
            Bindings.Known.Add(buffer.ResourceId);
        return buffer;
    }

    private static void CheckHistory(FrameResource resource, int framesAgo)
    {
        if (resource.Imported || resource.Lifetime.HistoryFrames == 0)
            throw new InvalidOperationException($"Resource '{resource.Name}' was not created with history.");
        if (framesAgo < 1 || framesAgo > resource.Lifetime.HistoryFrames)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [1, {resource.Lifetime.HistoryFrames}] for resource '{resource.Name}'.");
    }

    public bool IsHistoryValid(int index)
    {
        FrameResource resource = Graph.ResourceAt(index);
        if (!resource.IsRetained)
            return false;

        return resource.IsTexture
            ? resource.TextureRings!.IsValid(resource.Scope, Allocation(resource))
            : resource.BufferRings!.IsValid(resource.Scope, resource.Buffer.ToBufferDescription());
    }

    public int HistorySlot(int index)
    {
        FrameResource resource = Graph.ResourceAt(index);
        if (resource.Lifetime.HistoryFrames == 0 || resource.Imported)
            return -1;

        return resource.IsTexture
            ? resource.TextureRings!.CurrentSlot(resource.Scope)
            : resource.BufferRings!.CurrentSlot(resource.Scope);
    }

    public GraphTextureStates? CurrentTextureStates => _textureStates.Count == 0 ? null : (_stateSnapshot ??= new GraphTextureStates(_textureStates));

    public void TransitionForAccesses(ReadOnlySpan<ResourceAccess> accesses)
    {
        _barriers.Clear();
        BufferAccess bufferSrc = BufferAccess.None;
        BufferAccess bufferDst = BufferAccess.None;

        for (int i = 0; i < accesses.Length; i++)
        {
            ResourceAccess access = accesses[i];
            if (access.IsTexture)
            {
                if (!access.IsOutput && IsTextureOutput(accesses, access.Resource))
                    continue;

                RenderTexture texture = TextureAt(access.Resource);
                bool transient = Graph.ResourceAt(access.Resource).IsTransient;
                foreach (Texture color in texture.ColorTextures)
                    AddTextureTransition(color, access.Entry, transient && _enteredTransients.Add(color));
                if (texture.DepthTexture != null && access.DepthState(access.Entry) is TextureState depthTarget)
                    AddTextureTransition(texture.DepthTexture, depthTarget, transient && _enteredTransients.Add(texture.DepthTexture));
            }
            else
            {
                DeviceBuffer buffer = BufferAt(access.Resource);
                AddBufferAccess(buffer, access.BufferUsage, ref bufferSrc, ref bufferDst);
            }
        }

        _pendingBufferSrc = bufferSrc;
        _pendingBufferDst = bufferDst;
    }

    private static bool IsTextureOutput(ReadOnlySpan<ResourceAccess> accesses, int resource)
    {
        foreach (ResourceAccess access in accesses)
        {
            if (access.IsTexture && access.IsOutput && access.Resource == resource)
                return true;
        }
        return false;
    }

    public void RestoreRestingStates(string scopeName)
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

        CommandBuffer cb = BeginCommandBuffer($"{scopeName} Barriers", null);
        cb.RecordBarriers(CollectionsMarshal.AsSpan(_barriers), BufferAccess.None, BufferAccess.None);
        _barriers.Clear();
        EndCommandBuffer(cb);
    }

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

    private sealed class BufferSync
    {
        public BufferAccess LastWrite = BufferAccess.AllWrites;
        public BufferAccess ReadsSinceWrite = BufferAccess.AllReads;
        public BufferAccess Visible = BufferAccess.None;
    }

    public CommandBuffer BeginCommandBuffer(string name, PassInfo? pass)
    {
        CommandBuffer cb = Device.RentGraphCommandBuffer(Task);

        cb.Execution = Task;
        cb.Pass = pass;
        cb.Bindings = null;
        cb.PassSink = null;
        cb.ResetStats();
        cb.RentalId = (ulong)System.Threading.Interlocked.Increment(ref s_nextCommandBufferRentalId);
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        cb.Begin();
        cb.GraphStates = CurrentTextureStates;
        return cb;
    }

    public CommandBuffer BeginPassCommandBuffer(string passName, PassInfo? pass, ReadOnlySpan<ResourceAccess> accesses)
    {
        CommandBuffer cb = BeginCommandBuffer(passName, pass);
        if (Task.Profilers.CommandStream is { } profiler && cb.Pass is { } info)
            cb.PassSink = profiler.BeginPassCommands(in info);

        if (_barriers.Count > 0 || _pendingBufferSrc != BufferAccess.None)
        {
            uint emitted = cb.RecordBarriers(CollectionsMarshal.AsSpan(_barriers), _pendingBufferSrc, _pendingBufferDst);
            cb.AddBarrierStats(emitted);
            _pendingBufferSrc = BufferAccess.None;
            _pendingBufferDst = BufferAccess.None;
            CommitBarrierStates();
        }

        cb.GraphStates = PassTextureStates(accesses);
        _passStates = cb.GraphStates;
        return cb;
    }

    private GraphTextureStates? PassTextureStates(ReadOnlySpan<ResourceAccess> accesses)
    {
        Dictionary<Texture, TextureState>? allowed = null;
        if (HasSeveralStates(accesses))
        {
            foreach (ResourceAccess access in accesses)
            {
                if (!access.IsTexture)
                    continue;

                RenderTexture texture = TextureAt(access.Resource);
                foreach (Texture color in texture.ColorTextures)
                    Allow(ref allowed, color, access.TextureUsage);
                if (texture.DepthTexture != null && access.DepthAllowed != 0)
                    Allow(ref allowed, texture.DepthTexture, access.DepthAllowed);
            }
        }

        if (allowed == null)
            return CurrentTextureStates;

        foreach ((Texture texture, TextureState states) in allowed)
        {
            if ((states & (states - 1)) == 0)
                allowed.Remove(texture);
        }

        return allowed.Count == 0 ? CurrentTextureStates : new GraphTextureStates(_textureStates, allowed);
    }

    private static bool HasSeveralStates(ReadOnlySpan<ResourceAccess> accesses)
    {
        for (int i = 0; i < accesses.Length; i++)
        {
            ResourceAccess access = accesses[i];
            if (!access.IsTexture)
                continue;
            if (access.TextureUsage != access.Entry || (access.DepthAllowed & (access.DepthAllowed - 1)) != 0)
                return true;
            for (int j = 0; j < i; j++)
            {
                if (accesses[j].IsTexture && accesses[j].Resource == access.Resource && accesses[j].TextureUsage != access.TextureUsage)
                    return true;
            }
        }
        return false;
    }

    private static void Allow(ref Dictionary<Texture, TextureState>? allowed, Texture texture, TextureState states)
    {
        allowed ??= new Dictionary<Texture, TextureState>();
        allowed[texture] = allowed.TryGetValue(texture, out TextureState existing) ? existing | states : states;
    }

    public void AdoptPassStates(CommandBuffer cmd)
    {
        GraphTextureStates? final = cmd.GraphStates;
        GraphTextureStates? begun = _passStates;
        _passStates = null;
        if (final == null || ReferenceEquals(final, begun))
            return;

        final.CopyTo(_textureStates);
        _stateSnapshot = null;
    }

    public void EndCommandBuffer(CommandBuffer cmd)
    {
        if (cmd.PassSink is { } sink)
        {
            cmd.PassSink = null;
            sink.End();
        }

        Task.SubmitRecorded(cmd);
    }

    public void EndCommandBufferAhead(CommandBuffer cmd, CommandBuffer before) => Task.SubmitRecordedAhead(cmd, before);
}
