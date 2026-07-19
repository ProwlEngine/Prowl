using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Fixed-capacity ring buffer of <see cref="ProfileSnapshot"/>, sampled directly from
/// <see cref="GraphicsDevice.GetProfile"/> once per live editor frame. Graphite counters are device-global
/// (not tied to a camera or a <see cref="Prowl.Runtime.Rendering.RenderFrameReport"/>), so this is kept as
/// its own small history alongside <see cref="FrameHistoryBuffer"/> rather than folded into the render
/// report.
/// </summary>
public sealed class GraphiteSnapshotHistory
{
    private readonly ProfileSnapshot[] _items;
    private int _start;
    private int _count;

    public GraphiteSnapshotHistory(int capacity)
    {
        _items = new ProfileSnapshot[capacity < 1 ? 1 : capacity];
    }

    public int Count => _count;

    public void Push(ProfileSnapshot snapshot)
    {
        int writeIndex = (_start + _count) % _items.Length;
        _items[writeIndex] = snapshot;
        if (_count < _items.Length)
            _count++;
        else
            _start = (_start + 1) % _items.Length;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    /// <summary>Copy the buffer oldest-to-newest into a list (reused across calls when non-null).</summary>
    public void CopyTo(List<ProfileSnapshot> dst)
    {
        dst.Clear();
        for (int i = 0; i < _count; i++)
            dst.Add(_items[(_start + i) % _items.Length]);
    }
}
