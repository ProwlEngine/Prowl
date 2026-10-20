using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

/// <summary>One dispatch of a graph: the texture and buffer states every instance shares, and each instance's context.</summary>
internal sealed class GraphExecution
{
    private readonly Dictionary<Texture, TextureState> _textureStates = new();
    private GraphTextureStates? _stateSnapshot;
    private GraphTextureStates? _passStates;
    private readonly Dictionary<DeviceBuffer, BufferSync> _bufferSyncs = new();
    private readonly List<TextureBarrier> _barriers = new();
    private readonly HashSet<Texture> _enteredTransients = new();
    private readonly HashSet<Texture> _discardedTextures = new();
    private readonly RenderContext?[] _contexts;
    private BufferAccess _pendingBufferSrc;
    private BufferAccess _pendingBufferDst;

    private static long s_nextCommandBufferRentalId;

    public GraphExecution(GraphicsDevice device, ExecutionTask task, RenderGraph graph, GraphInstances instances)
    {
        Device = device;
        Task = task;
        Graph = graph;
        Instances = instances;
        _contexts = new RenderContext?[instances.All.Count];
    }

    public GraphicsDevice Device { get; }

    public ExecutionTask Task { get; }

    public RenderGraph Graph { get; }

    public GraphInstances Instances { get; }

    public void RecordResizes()
    {
        if (!Graph.Store.HasResizes)
            return;

        CommandBuffer cb = BeginCommandBuffer("Graph Resizes", null);
        Graph.Store.RecordResizes(cb);
        EndCommandBuffer(cb);
    }

    public RenderContext ContextOf(GraphInstance instance)
    {
        RenderContext? context = _contexts[instance.Index];
        if (context != null)
            return context;

        context = instance.Parent == null ? new RenderContext(this, instance) : new InstanceContext(this, instance);
        _contexts[instance.Index] = context;
        context.Prepare();
        return context;
    }

    public GraphTextureStates? CurrentTextureStates => _textureStates.Count == 0 ? null : (_stateSnapshot ??= new GraphTextureStates(_textureStates));

    public void TransitionForAccesses(RenderContext context, ResourceAccess[] accesses)
    {
        _barriers.Clear();
        BufferAccess bufferSrc = BufferAccess.None;
        BufferAccess bufferDst = BufferAccess.None;

        for (int i = 0; i < accesses.Length; i++)
        {
            ResourceAccess access = accesses[i];
            if (access.IsTexture)
            {
                if (!access.IsOutput && context.IsTextureOutput(accesses, access.Id))
                    continue;

                RenderTexture texture = context.GetRenderTexture(new TextureHandle(access.Id));
                bool transient = context.IsTransient(access.Id);
                foreach (Texture color in texture.ColorTextures)
                    AddTextureTransition(color, access.Entry, transient && _enteredTransients.Add(color));
                if (texture.DepthTexture != null && access.DepthState(access.Entry) is TextureState depthTarget)
                    AddTextureTransition(texture.DepthTexture, depthTarget, transient && _enteredTransients.Add(texture.DepthTexture));
            }
            else
            {
                DeviceBuffer buffer = context.GetRenderBuffer(new BufferHandle(access.Id));
                AddBufferAccess(buffer, access.BufferUsage, ref bufferSrc, ref bufferDst);
            }
        }

        _pendingBufferSrc = bufferSrc;
        _pendingBufferDst = bufferDst;
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
        cb.PassSink = null;
        cb.ResetStats();
        cb.RentalId = (ulong)System.Threading.Interlocked.Increment(ref s_nextCommandBufferRentalId);
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;

        cb.Begin();
        cb.GraphStates = CurrentTextureStates;
        return cb;
    }

    public CommandBuffer BeginPassCommandBuffer(RenderContext context, string passName, PassInfo? pass)
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

        cb.GraphStates = PassTextureStates(context);
        _passStates = cb.GraphStates;
        return cb;
    }

    private GraphTextureStates? PassTextureStates(RenderContext context)
    {
        Dictionary<Texture, TextureState>? allowed = null;
        ResourceAccess[]? accesses = context.CurrentAccesses;
        if (accesses != null && HasSeveralStates(accesses))
        {
            foreach (ResourceAccess access in accesses)
            {
                if (!access.IsTexture || access.Id == GraphInstanceTargetResource.InstanceTargetId)
                    continue;

                RenderTexture texture = context.GetRenderTexture(new TextureHandle(access.Id));
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

    private static bool HasSeveralStates(ResourceAccess[] accesses)
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
                if (accesses[j].IsTexture && accesses[j].Id == access.Id && accesses[j].TextureUsage != access.TextureUsage)
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
