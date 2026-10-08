using System.Collections.Generic;
using System.Linq;

namespace Prowl.Graphite.Debugger;

internal sealed class RecordingSink : IGraphProfiler, IGpuStatsProfiler
{
    private readonly object _gate = new();
    private readonly Dictionary<ulong, ExecutionBuilder> _executions = new();
    private bool _closed;

    public ulong? LastExecutionId
    {
        get
        {
            lock (_gate)
                return _executions.Count == 0 ? null : _executions.Keys.Max();
        }
    }

    public ulong? FirstUnresolvedExecutionId
    {
        get
        {
            lock (_gate)
            {
                ulong? first = null;
                foreach (KeyValuePair<ulong, ExecutionBuilder> entry in _executions)
                {
                    if (!entry.Value.Resolved && (first is null || entry.Key < first))
                        first = entry.Key;
                }

                return first;
            }
        }
    }

    public bool AllResolved
    {
        get
        {
            lock (_gate)
                return _executions.Values.All(e => e.Resolved);
        }
    }

    public void Close()
    {
        lock (_gate)
            _closed = true;
    }

    public IReadOnlyList<RecordedExecution> Build()
    {
        lock (_gate)
            return _executions.OrderBy(e => e.Key).Select(e => e.Value.Build(e.Key)).ToArray();
    }

    public void BeginView(in ViewInfo view)
    {
        lock (_gate)
        {
            if (!_executions.TryGetValue(view.ExecutionId, out ExecutionBuilder? execution))
            {
                if (_closed)
                    return;

                _executions[view.ExecutionId] = execution = new ExecutionBuilder();
            }

            execution.Views[view.Index] = new ViewBuilder(view.Name, view.Index, view.PixelWidth, view.PixelHeight);
        }
    }

    public void EndView(in ViewInfo view) { }

    public void BeginPass(in PassInfo pass) { }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(pass.ExecutionId, out ExecutionBuilder? execution) && execution.Views.TryGetValue(pass.ViewIndex, out ViewBuilder? view))
                view.Passes.Add(new RecordedPass(pass.Name, pass.Index, stats));
        }
    }

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(commandBuffer.ExecutionId, out ExecutionBuilder? execution))
            {
                CommandBufferBuilder builder = execution.CommandBuffer(commandBuffer);
                builder.IsTransfer = isTransfer;
                builder.Milliseconds = milliseconds;
            }
        }
    }

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(commandBuffer.ExecutionId, out ExecutionBuilder? execution))
                execution.CommandBuffer(commandBuffer).VertexStats = stats;
        }
    }

    public void RecordExecutionResolved(ulong executionId)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(executionId, out ExecutionBuilder? execution))
                execution.Resolved = true;
        }
    }

    private sealed class ExecutionBuilder
    {
        public readonly SortedDictionary<int, ViewBuilder> Views = new();
        public readonly Dictionary<ulong, CommandBufferBuilder> CommandBuffers = new();
        public bool Resolved;

        public CommandBufferBuilder CommandBuffer(in CommandBufferInfo info)
        {
            if (!CommandBuffers.TryGetValue(info.Id, out CommandBufferBuilder? builder))
                CommandBuffers[info.Id] = builder = new CommandBufferBuilder(info);
            return builder;
        }

        public RecordedExecution Build(ulong id)
            => new(
                id,
                Views.Values.Select(v => new RecordedView(v.Name, v.Index, v.PixelWidth, v.PixelHeight, v.Passes.ToArray())).ToArray(),
                CommandBuffers.OrderBy(c => c.Key).Select(c => c.Value.Build()).ToArray());
    }

    private sealed class ViewBuilder(string name, int index, uint pixelWidth, uint pixelHeight)
    {
        public readonly string Name = name;
        public readonly int Index = index;
        public readonly uint PixelWidth = pixelWidth;
        public readonly uint PixelHeight = pixelHeight;
        public readonly List<RecordedPass> Passes = new();
    }

    private sealed class CommandBufferBuilder(in CommandBufferInfo info)
    {
        private readonly ulong _id = info.Id;
        private readonly string _name = info.Name;
        private readonly int _viewIndex = info.Pass?.ViewIndex ?? -1;
        private readonly int _passIndex = info.Pass?.Index ?? -1;

        public bool IsTransfer;
        public double? Milliseconds;
        public GpuVertexStats? VertexStats;

        public RecordedCommandBuffer Build()
            => new(_id, _name, _viewIndex, _passIndex, IsTransfer, Milliseconds, VertexStats);
    }
}
