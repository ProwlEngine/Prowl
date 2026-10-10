using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>Views, passes, and GPU timings of one execution. Pass it to the execution, then read it once done.</summary>
public sealed class Recording : IGraphProfiler, IGpuStatsProfiler
{
    private readonly GraphicsDevice? _device;
    private readonly object _gate = new();
    private readonly SortedDictionary<int, ViewBuilder> _views = new();
    private readonly Dictionary<ulong, CommandBufferBuilder> _commandBuffers = new();
    private ulong _executionId;
    private string _graphName = "";
    private bool _resolved;
    private volatile bool _done;
    private EquatableArray<RecordedView> _builtViews = EquatableArray<RecordedView>.Empty;
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
        EquatableArray<RecordedView> views,
        EquatableArray<RecordedCommandBuffer> commandBuffers,
        RecordedCounters? counters)
    {
        Backend = backend;
        DeviceName = deviceName;
        Features = features;
        _executionId = executionId;
        _graphName = graphName;
        _builtViews = views;
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

    /// <summary>The views of the execution in order.</summary>
    public EquatableArray<RecordedView> Views
    {
        get
        {
            RequireDone();
            return _builtViews;
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

    /// <summary>Counter deltas from the execution start to the end of its last view, or null if it had no views.</summary>
    /// <remarks>Work after the last view, such as submission and present, is not included.</remarks>
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

    void IGraphProfiler.BeginView(in ViewInfo view)
    {
        lock (_gate)
            _views[view.Index] = new ViewBuilder(view.Name, view.Index, view.PixelWidth, view.PixelHeight);
    }

    void IGraphProfiler.EndView(in ViewInfo view)
    {
        GraphicsCountersSnapshot end = _device!.Counters.Snapshot();
        MemoryBudgetInfo budget = _device.GetMemoryBudget();
        lock (_gate)
            _counters = RecordedCounters.Delta(_startCounters, end, budget);
    }

    void IGraphProfiler.BeginPass(in PassInfo pass) { }

    void IGraphProfiler.SkipPass(string name, int viewIndex, PassSkipReason reason)
    {
        lock (_gate)
        {
            if (_views.TryGetValue(viewIndex, out ViewBuilder? view))
                view.Skipped.Add(new RecordedSkippedPass(name, reason));
        }
    }

    void IGraphProfiler.EndPass(in PassInfo pass, in PassStats stats)
    {
        lock (_gate)
        {
            if (_views.TryGetValue(pass.ViewIndex, out ViewBuilder? view))
                view.Passes.Add(new RecordedPass(pass.Name, pass.Index, stats));
        }
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
        _builtViews = _views.Values.Select(v => new RecordedView(v.Name, v.Index, v.PixelWidth, v.PixelHeight, v.Passes.ToEquatableArray(), v.Skipped.ToEquatableArray())).ToEquatableArray();
        _builtCommandBuffers = _commandBuffers.OrderBy(c => c.Key).Select(c => c.Value.Build()).ToEquatableArray();
        _views.Clear();
        _commandBuffers.Clear();
    }

    private sealed class ViewBuilder(string name, int index, uint pixelWidth, uint pixelHeight)
    {
        public readonly string Name = name;
        public readonly int Index = index;
        public readonly uint PixelWidth = pixelWidth;
        public readonly uint PixelHeight = pixelHeight;
        public readonly List<RecordedPass> Passes = new();
        public readonly List<RecordedSkippedPass> Skipped = new();
    }

    private sealed class CommandBufferBuilder(in CommandBufferInfo info)
    {
        private readonly ulong _id = info.Id;
        private readonly string _name = info.Name;
        private readonly int _viewIndex = info.Pass?.ViewIndex ?? -1;
        private readonly int _passIndex = info.Pass?.Index ?? -1;

        public double? Milliseconds;
        public GpuVertexStats? VertexStats;

        public RecordedCommandBuffer Build()
            => new(_id, _name, _viewIndex, _passIndex, Milliseconds, VertexStats);
    }
}
