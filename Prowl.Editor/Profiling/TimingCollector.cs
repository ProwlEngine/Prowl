using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Tracks command buffers awaiting their late-arriving GPU results (execution time + pipeline
/// statistics), keyed by <see cref="CommandBufferInfo.Id"/> - a rental id that never repeats (see
/// PROFILING_MODEL.md). A result is stamped directly onto the exact <see cref="ProfiledCommandBuffer"/>
/// that issued it the moment it arrives, however many frames later that turns out to be, instead of
/// being matched against whatever frame happens to be "live" when it shows up - a match that, given ids
/// never repeat, essentially never succeeds once the result is more than zero frames late.
/// </summary>
public sealed class TimingCollector
{
    // Generous slack beyond the "usually one frame, sometimes more" GPU round-trip - only exists to
    // bound the dictionary if a query never resolves at all (feature unsupported, disabled mid-flight),
    // not because results are expected to take this long.
    private const long MaxPendingAgeFrames = 16;

    private readonly Dictionary<ulong, (ProfiledCommandBuffer CommandBuffer, long FrameIndex)> _pending = new();
    private readonly List<ulong> _staleScratch = new();

    public void OnFrameBegin(long frameIndex)
    {
        if (_pending.Count == 0)
            return;

        _staleScratch.Clear();
        foreach (KeyValuePair<ulong, (ProfiledCommandBuffer CommandBuffer, long FrameIndex)> entry in _pending)
        {
            if (frameIndex - entry.Value.FrameIndex > MaxPendingAgeFrames)
                _staleScratch.Add(entry.Key);
        }
        foreach (ulong id in _staleScratch)
            _pending.Remove(id);
    }

    /// <summary>Registers a command buffer as awaiting its GPU results - called the moment a node is
    /// created/touched for a given rental id, so a late result always has the right place to land.
    /// Safe to call more than once for the same id (first caller wins).</summary>
    public void Track(ulong id, long frameIndex, ProfiledCommandBuffer commandBuffer)
    {
        if (id != 0 && !_pending.ContainsKey(id))
            _pending[id] = (commandBuffer, frameIndex);
    }

    // A command buffer id can report execution time more than once (see the historical accumulate
    // behaviour this preserves), so this deliberately does not consume the pending entry - only
    // OnGpuVertexStats (guaranteed exactly-once per rental) does.
    public void OnExecutionTime(in CommandBufferInfo info, bool isTransfer, double ms)
    {
        if (info.Id == 0 || !_pending.TryGetValue(info.Id, out var entry) || entry.CommandBuffer.Id != info.Id)
            return;

        entry.CommandBuffer.AddGpuMs(ms);
    }

    // A command buffer id is only ever reported once per rental (one query per rental), so this both
    // stamps the result and retires the pending entry.
    public void OnGpuVertexStats(in CommandBufferInfo info, in GpuVertexStats stats)
    {
        if (info.Id == 0 || !_pending.TryGetValue(info.Id, out var entry) || entry.CommandBuffer.Id != info.Id)
            return;

        entry.CommandBuffer.SetGpuVertexStats(stats);
        _pending.Remove(info.Id);
    }
}
