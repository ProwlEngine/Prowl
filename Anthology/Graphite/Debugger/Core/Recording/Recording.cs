using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>Groups, passes, and GPU timings of one execution. Pass it to the execution, then read it once done.</summary>
public sealed class Recording : IGraphProfiler, IGpuStatsProfiler
{
    private readonly GraphicsDevice? _device;
    private readonly object _gate = new();
    private readonly SortedDictionary<int, GroupBuilder> _groups = new();
    private readonly Dictionary<ulong, CommandBufferBuilder> _commandBuffers = new();
    private ulong _executionId;
    private string _graphName = "";
    private bool _resolved;
    private volatile bool _done;
    private EquatableArray<RecordedGroup> _builtGroups = EquatableArray<RecordedGroup>.Empty;
    private EquatableArray<RecordedCommandBuffer> _builtCommandBuffers = EquatableArray<RecordedCommandBuffer>.Empty;
    private GraphicsCountersSnapshot _startCounters;
    private RecordedCounters? _counters;

    /// <summary>Creates a recording for one execution on the device.</summary>
    /// <param name="device">Device the execution runs on.</param>
    public Recording(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        Backend = device.BackendType;
        DeviceName = device.DeviceName;
        Features = RecordedFeatures.From(device.Features);
    }

    internal Recording(
        GraphicsBackend backend,
        string deviceName,
        RecordedFeatures features,
        ulong executionId,
        string graphName,
        EquatableArray<RecordedGroup> groups,
        EquatableArray<RecordedCommandBuffer> commandBuffers,
        RecordedCounters? counters)
    {
        Backend = backend;
        DeviceName = deviceName;
        Features = features;
        _executionId = executionId;
        _graphName = graphName;
        _builtGroups = groups;
        _builtCommandBuffers = commandBuffers;
        _counters = counters;
        _done = true;
    }

    /// <summary>Backend of the recording device.</summary>
    public GraphicsBackend Backend { get; }

    /// <summary>Name of the recording device's adapter.</summary>
    public string DeviceName { get; }

    /// <summary>Features of the recording device.</summary>
    public RecordedFeatures Features { get; }

    /// <summary>Id of the recorded execution, or 0 before it is passed to one. Ids grow with start order.</summary>
    public ulong ExecutionId => Volatile.Read(ref _executionId);

    /// <summary>Debug name of the recorded execution's graph, or empty before it is passed to one.</summary>
    public string GraphName => Volatile.Read(ref _graphName);

    /// <summary>The groups of the execution in order. Passes added outside any group are in group -1, which comes first.</summary>
    public EquatableArray<RecordedGroup> Groups
    {
        get
        {
            RequireDone();
            return _builtGroups;
        }
    }

    /// <summary>GPU results per command buffer, in rental order.</summary>
    public EquatableArray<RecordedCommandBuffer> CommandBuffers
    {
        get
        {
            RequireDone();
            return _builtCommandBuffers;
        }
    }

    /// <summary>Counter deltas from the execution start to the end of its last root group or ungrouped pass, or null if it had none.</summary>
    /// <remarks>Work after the last group, such as submission and present, is not included.</remarks>
    public RecordedCounters? Counters
    {
        get
        {
            RequireDone();
            return _counters;
        }
    }

    /// <summary>True once the execution has resolved on the GPU.</summary>
    public bool IsDone
    {
        get
        {
            if (_done)
                return true;

            ulong executionId = ExecutionId;
            if (executionId == 0)
                return false;

            _device!.IsExecutionComplete(executionId);
            lock (_gate)
            {
                if (_done)
                    return true;

                if (!_resolved)
                    return false;

                Build();
                _done = true;
                return true;
            }
        }
    }

    /// <summary>Blocks until the execution has resolved. Throws if the recording was never passed to an execution.</summary>
    public void Wait()
    {
        if (IsDone)
            return;

        ulong executionId = ExecutionId;
        if (executionId == 0)
            throw new InvalidOperationException("The recording was never passed to an execution.");

        while (!IsDone)
        {
            if (!_device!.WaitForExecution(executionId, 5_000_000))
                Thread.Sleep(1);
        }
    }

    internal void RequireDone()
    {
        if (!IsDone)
            throw new InvalidOperationException("The recording is not done. Check IsDone or call Wait() first.");
    }

    void IProfiler.BeginExecution(ulong executionId, string graphName)
    {
        lock (_gate)
        {
            if (_device == null || _executionId != 0)
                throw new InvalidOperationException("A recording observes exactly one execution.");

            _startCounters = _device.Counters.Snapshot();
            Volatile.Write(ref _graphName, graphName);
            Volatile.Write(ref _executionId, executionId);
        }
    }

    void IProfiler.EndExecution()
    {
        lock (_gate)
            _resolved = true;
    }

    void IGraphProfiler.BeginGroup(in GroupInfo group)
    {
        lock (_gate)
            _groups[group.Index] = new GroupBuilder(group);
    }

    void IGraphProfiler.EndGroup(in GroupInfo group)
    {
        if (group.ParentIndex == -1)
            SnapshotCounters();
    }

    void IGraphProfiler.BeginPass(in PassInfo pass) { }

    void IGraphProfiler.EndPass(in PassInfo pass, in PassStats stats)
    {
        lock (_gate)
        {
            if (!_groups.TryGetValue(pass.GroupIndex, out GroupBuilder? group))
                _groups[pass.GroupIndex] = group = new GroupBuilder(new GroupInfo("", pass.GroupIndex, -1));

            group.Passes.Add(new RecordedPass(pass.Name, pass.Index, stats));
        }

        if (pass.GroupIndex == -1)
            SnapshotCounters();
    }

    private void SnapshotCounters()
    {
        GraphicsCountersSnapshot end = _device!.Counters.Snapshot();
        MemoryBudgetInfo budget = _device.GetMemoryBudget();
        lock (_gate)
            _counters = RecordedCounters.Delta(_startCounters, end, budget);
    }

    void IGpuStatsProfiler.RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds)
    {
        lock (_gate)
            CommandBuffer(commandBuffer).Milliseconds = milliseconds;
    }

    void IGpuStatsProfiler.RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
    {
        lock (_gate)
            CommandBuffer(commandBuffer).VertexStats = stats;
    }

    private CommandBufferBuilder CommandBuffer(in CommandBufferInfo info)
    {
        if (!_commandBuffers.TryGetValue(info.Id, out CommandBufferBuilder? builder))
            _commandBuffers[info.Id] = builder = new CommandBufferBuilder(info);
        return builder;
    }

    private void Build()
    {
        _builtGroups = _groups.Values.Select(g => g.Build()).ToEquatableArray();
        _builtCommandBuffers = _commandBuffers.OrderBy(c => c.Key).Select(c => c.Value.Build()).ToEquatableArray();
        _groups.Clear();
        _commandBuffers.Clear();
    }

    private sealed class GroupBuilder(in GroupInfo info)
    {
        private readonly GroupInfo _info = info;
        public readonly List<RecordedPass> Passes = new();

        public RecordedGroup Build() => new(_info.Name, _info.Index, _info.ParentIndex, Passes.ToEquatableArray());
    }

    private sealed class CommandBufferBuilder(in CommandBufferInfo info)
    {
        private readonly ulong _id = info.Id;
        private readonly string _name = info.Name;
        private readonly int _groupIndex = info.Pass?.GroupIndex ?? -1;
        private readonly int _passIndex = info.Pass?.Index ?? -1;

        public double? Milliseconds;
        public GpuVertexStats? VertexStats;

        public RecordedCommandBuffer Build()
            => new(_id, _name, _groupIndex, _passIndex, Milliseconds, VertexStats);
    }
}
