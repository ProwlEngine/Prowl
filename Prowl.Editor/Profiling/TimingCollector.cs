using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Writes GPU timing samples to the currently building frame.
/// </summary>
public sealed class TimingCollector
{
    private readonly Dictionary<ulong, double> _commandBufferGpuMs = new();
    private readonly Dictionary<ulong, GpuVertexStats> _commandBufferVertexStats = new();

    public void OnFrameBegin()
    {
        _commandBufferGpuMs.Clear();
        _commandBufferVertexStats.Clear();
    }

    public void OnExecutionTime(in CommandBufferInfo info, bool isTransfer, double ms)
    {
        if (info.Id != 0)
        {
            _commandBufferGpuMs.TryGetValue(info.Id, out double existing);
            _commandBufferGpuMs[info.Id] = existing + ms;
        }
    }

    public double GetCommandBufferGpuMs(ulong commandBufferId) => _commandBufferGpuMs.TryGetValue(commandBufferId, out double ms) ? ms : 0.0;

    // A command buffer id is only ever reported once per frame (one query per rental), unlike GPU
    // timing which can accumulate multiple execution-time reports under the same id - so this is a
    // plain last-write, not a running sum.
    public void OnGpuVertexStats(in CommandBufferInfo info, in GpuVertexStats stats)
    {
        if (info.Id != 0)
            _commandBufferVertexStats[info.Id] = stats;
    }

    public GpuVertexStats GetCommandBufferVertexStats(ulong commandBufferId)
        => _commandBufferVertexStats.TryGetValue(commandBufferId, out GpuVertexStats stats) ? stats : default;
}
