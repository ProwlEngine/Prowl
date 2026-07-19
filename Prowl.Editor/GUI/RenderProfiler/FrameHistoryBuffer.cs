using System.Collections.Generic;

using Prowl.Runtime.Rendering;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Fixed-capacity ring buffer of <see cref="RenderFrameReport"/> pulled from the selected camera each
/// editor frame. New reports are only appended when their <see cref="RenderFrameReport.FrameIndex"/>
/// differs from the last stored one, so a paused or non-rendering camera does not flood the buffer with
/// duplicates. The live viewer reads <see cref="Latest"/>; the scrub control indexes <see cref="At"/>.
/// </summary>
public sealed class FrameHistoryBuffer
{
    private readonly RenderFrameReport[] _items;
    private int _start;
    private int _count;
    private long _lastFrameIndex = long.MinValue;

    public FrameHistoryBuffer(int capacity)
    {
        _items = new RenderFrameReport[capacity < 1 ? 1 : capacity];
    }

    public int Capacity => _items.Length;
    public int Count => _count;

    /// <summary>The most recently stored report, or null when the buffer is empty.</summary>
    public RenderFrameReport? Latest => _count == 0 ? null : _items[(_start + _count - 1) % _items.Length];

    /// <summary>Index 0 is the oldest retained frame; <see cref="Count"/> - 1 is the newest.</summary>
    public RenderFrameReport? At(int index)
    {
        if (index < 0 || index >= _count) return null;
        return _items[(_start + index) % _items.Length];
    }

    /// <summary>
    /// Append a report if it represents a new frame. Returns true when a new frame was stored.
    /// </summary>
    public bool Push(RenderFrameReport report)
    {
        if (report == null || report.FrameIndex == _lastFrameIndex) return false;
        _lastFrameIndex = report.FrameIndex;

        int writeIndex = (_start + _count) % _items.Length;
        _items[writeIndex] = report;
        if (_count < _items.Length)
            _count++;
        else
            _start = (_start + 1) % _items.Length;
        return true;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
        _lastFrameIndex = long.MinValue;
        for (int i = 0; i < _items.Length; i++) _items[i] = null!;
    }

    /// <summary>Copy the buffer oldest-to-newest into a list (reused across calls when non-null).</summary>
    public void CopyTo(List<RenderFrameReport> dst)
    {
        dst.Clear();
        for (int i = 0; i < _count; i++)
            dst.Add(_items[(_start + i) % _items.Length]);
    }
}
