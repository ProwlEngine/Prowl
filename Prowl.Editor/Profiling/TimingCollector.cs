using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Writes GPU timing samples to the currently building frame.
/// </summary>
public sealed class TimingCollector
{
    // Keyed by pass name (a small, stable set), so the outer dictionary and each inner list persist
    // across frames - only their contents are cleared - instead of being discarded and rebuilt.
    private readonly List<string> _gpuGroupOrder = new();
    private readonly Dictionary<string, List<TimeSample>> _gpuGroups = new();
    private bool _hasGpuData;

    private readonly Dictionary<ulong, double> _commandBufferGpuMs = new();
    private readonly Dictionary<ulong, GpuVertexStats> _commandBufferVertexStats = new();

    public void OnFrameBegin()
    {
        _gpuGroupOrder.Clear();
        foreach (List<TimeSample> leaves in _gpuGroups.Values)
            leaves.Clear();
        _hasGpuData = false;

        _commandBufferGpuMs.Clear();
        _commandBufferVertexStats.Clear();
    }

    public void OnExecutionTime(in CommandBufferInfo info, bool isTransfer, double ms)
    {
        string key = info.Pass.HasValue ? info.Pass.Value.Name : "Transfer";
        if (!_gpuGroups.TryGetValue(key, out List<TimeSample>? leaves))
        {
            leaves = new List<TimeSample>();
            _gpuGroups[key] = leaves;
        }

        // leaves persists across frames and is only Clear()'d at OnFrameBegin, so an empty list here
        // means this key hasn't been touched yet this frame - not necessarily that it's brand new.
        if (leaves.Count == 0)
            _gpuGroupOrder.Add(key);
        leaves.Add(new TimeSample(info.Name, ms, isTransfer, []));
        _hasGpuData = true;

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

    public void FinalizeFrame(ProfiledFrame frame)
    {
        if (!_hasGpuData)
            return;

        var groups = new List<TimeSample>(_gpuGroupOrder.Count);
        double total = 0.0;
        foreach (string key in _gpuGroupOrder)
        {
            List<TimeSample> leaves = _gpuGroups[key];
            double sum = 0.0;
            foreach (TimeSample leaf in leaves)
                sum += leaf.InclusiveMilliseconds;
            groups.Add(new TimeSample(key, sum, key == "Transfer", leaves.ToArray()));
            total += sum;
        }

        frame.SetGpuRoot(new TimeSample("GPU", total, false, groups.ToArray()));
    }
}
