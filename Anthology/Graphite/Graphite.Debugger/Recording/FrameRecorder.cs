using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Recording;

public sealed class FrameRecorder : IGraphProfiler, IGpuStatsProfiler
{
    public const int DefaultCapacity = 120;

    private readonly object _lock = new();
    private readonly SortedDictionary<ulong, PendingFrame> _pending = new();
    private readonly LightFrame?[] _ring;
    private int _head;
    private int _count;

    public FrameRecorder(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new LightFrame?[capacity];
    }

    public int Capacity => _ring.Length;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public LightFrame? Latest
    {
        get
        {
            lock (_lock)
            {
                return _count == 0 ? null : _ring[(_head - 1 + _ring.Length) % _ring.Length];
            }
        }
    }

    public IReadOnlyList<LightFrame> Frames()
    {
        lock (_lock)
        {
            LightFrame[] frames = new LightFrame[_count];
            int start = (_head - _count + _ring.Length) % _ring.Length;
            for (int i = 0; i < _count; i++)
            {
                frames[i] = _ring[(start + i) % _ring.Length]!;
            }

            return frames;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_ring);
            _head = 0;
            _count = 0;
            _pending.Clear();
        }
    }

    public void BeginView(in ViewInfo view)
    {
        lock (_lock)
        {
            PendingFrame frame = Pending(view.ExecutionId);
            frame.Views[view.Index] = new PendingView(view.Name, view.Index, view.PixelWidth, view.PixelHeight);
        }
    }

    public void EndView(in ViewInfo view)
    {
    }

    public void BeginPass(in PassInfo pass)
    {
        lock (_lock)
        {
            PendingView? view = ViewOf(pass);
            view?.Passes.Add(new PendingPass(pass));
        }
    }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        lock (_lock)
        {
            PendingView? view = ViewOf(pass);
            if (view is null)
            {
                return;
            }

            foreach (PendingPass candidate in view.Passes)
            {
                if (candidate.Index == pass.Index)
                {
                    candidate.Stats = stats;
                    return;
                }
            }
        }
    }

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
    }

    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
    }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
    {
        lock (_lock)
        {
            PendingCommandBuffer entry = CommandBufferOf(commandBuffer);
            entry.IsTransfer = isTransfer;
            entry.GpuMilliseconds += milliseconds;
        }
    }

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
    {
        lock (_lock)
        {
            CommandBufferOf(commandBuffer).VertexStats = stats;
        }
    }

    public void RecordExecutionResolved(ulong executionId)
    {
        lock (_lock)
        {
            if (!_pending.Remove(executionId, out PendingFrame? pending))
            {
                return;
            }

            _ring[_head] = pending.Build(executionId);
            _head = (_head + 1) % _ring.Length;
            _count = Math.Min(_count + 1, _ring.Length);
        }
    }

    private PendingFrame Pending(ulong executionId)
    {
        if (_pending.TryGetValue(executionId, out PendingFrame? frame))
        {
            return frame;
        }

        frame = new PendingFrame();
        _pending[executionId] = frame;
        while (_pending.Count > _ring.Length)
        {
            using SortedDictionary<ulong, PendingFrame>.Enumerator enumerator = _pending.GetEnumerator();
            enumerator.MoveNext();
            _pending.Remove(enumerator.Current.Key);
        }

        return frame;
    }

    private PendingView? ViewOf(in PassInfo pass)
    {
        return Pending(pass.ExecutionId).Views.GetValueOrDefault(pass.ViewIndex);
    }

    private PendingCommandBuffer CommandBufferOf(in CommandBufferInfo info)
    {
        PendingFrame frame = Pending(info.ExecutionId);
        if (!frame.CommandBuffers.TryGetValue(info.Id, out PendingCommandBuffer? entry))
        {
            entry = new PendingCommandBuffer(info.Id, info.Name, info.Pass?.ViewIndex, info.Pass?.Index);
            frame.CommandBuffers[info.Id] = entry;
            frame.CommandBufferOrder.Add(entry);
        }

        return entry;
    }

    private static EquatableArray<string> Names(ReadOnlyMemory<RenderResourceID> ids)
    {
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>(ids.Length);
        foreach (RenderResourceID id in ids.Span)
        {
            names.Add(RenderResourceID.ToString(id) ?? string.Empty);
        }

        return names.MoveToImmutable();
    }

    private sealed class PendingFrame
    {
        public readonly SortedDictionary<int, PendingView> Views = new();
        public readonly Dictionary<ulong, PendingCommandBuffer> CommandBuffers = new();
        public readonly List<PendingCommandBuffer> CommandBufferOrder = new();

        public LightFrame Build(ulong executionId)
        {
            ImmutableArray<LightView>.Builder views = ImmutableArray.CreateBuilder<LightView>(Views.Count);
            foreach (PendingView view in Views.Values)
            {
                views.Add(view.Build());
            }

            ImmutableArray<LightCommandBuffer>.Builder commandBuffers = ImmutableArray.CreateBuilder<LightCommandBuffer>(CommandBufferOrder.Count);
            foreach (PendingCommandBuffer commandBuffer in CommandBufferOrder)
            {
                commandBuffers.Add(commandBuffer.Build());
            }

            return new LightFrame(executionId, views.MoveToImmutable(), commandBuffers.MoveToImmutable());
        }
    }

    private sealed class PendingView
    {
        public readonly List<PendingPass> Passes = new();
        private readonly string _name;
        private readonly int _index;
        private readonly uint _width;
        private readonly uint _height;

        public PendingView(string name, int index, uint width, uint height)
        {
            _name = name;
            _index = index;
            _width = width;
            _height = height;
        }

        public LightView Build()
        {
            ImmutableArray<LightPass>.Builder passes = ImmutableArray.CreateBuilder<LightPass>(Passes.Count);
            foreach (PendingPass pass in Passes)
            {
                passes.Add(pass.Build());
            }

            return new LightView(_name, _index, _width, _height, passes.MoveToImmutable());
        }
    }

    private sealed class PendingPass
    {
        public readonly int Index;
        public PassStats Stats;
        private readonly string _name;
        private readonly int _viewIndex;
        private readonly EquatableArray<string> _inputs;
        private readonly EquatableArray<string> _outputs;

        public PendingPass(in PassInfo pass)
        {
            Index = pass.Index;
            _name = pass.Name;
            _viewIndex = pass.ViewIndex;
            _inputs = Names(pass.Inputs);
            _outputs = Names(pass.Outputs);
        }

        public LightPass Build() => new(_name, Index, _viewIndex, Stats, _inputs, _outputs);
    }

    private sealed class PendingCommandBuffer
    {
        public bool IsTransfer;
        public double GpuMilliseconds;
        public GpuVertexStats? VertexStats;
        private readonly ulong _id;
        private readonly string _name;
        private readonly int? _viewIndex;
        private readonly int? _passIndex;

        public PendingCommandBuffer(ulong id, string name, int? viewIndex, int? passIndex)
        {
            _id = id;
            _name = name;
            _viewIndex = viewIndex;
            _passIndex = passIndex;
        }

        public LightCommandBuffer Build() => new(_id, _name, IsTransfer, _viewIndex, _passIndex, GpuMilliseconds, VertexStats);
    }
}
