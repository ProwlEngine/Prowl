using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;


public sealed class ProfiledFrame
{
    public long FrameIndex { get; internal set; }
    public double FrameMilliseconds { get; set; }
    public double Fps { get; set; }

    public bool HasCaptureDepth { get; internal set; }

    private CounterValue[] _counters = Array.Empty<CounterValue>();

    private readonly Dictionary<string, ProfiledView> _views = new();
    private readonly List<ProfiledView> _activeViews = new();

    // CommandBuffer.Id is constantly-incrementing so it must be treated differently than the other ids.
    private readonly Dictionary<ulong, ProfiledCommandBuffer> _freeCommandBuffers = new();
    private readonly List<ProfiledCommandBuffer> _activeFreeCommandBuffers = new();
    private readonly List<ProfiledCommandBuffer> _freeCommandBufferPool = new();

    private readonly List<int> _timelineEntryTracker = new();

    public IReadOnlyList<CounterValue> Counters => _counters;
    public IReadOnlyList<ProfiledView> Views => _activeViews;
    public IReadOnlyList<ProfiledCommandBuffer> FreeCommandBuffers => _activeFreeCommandBuffers;

    public int TimelineElementCount => _timelineEntryTracker.Count;

    internal IReadOnlyList<int> TimelineEntries => _timelineEntryTracker;

    internal void Reset(long frameIndex, bool hasCaptureDepth)
    {
        FrameIndex = frameIndex;
        FrameMilliseconds = 0;
        Fps = 0;
        HasCaptureDepth = hasCaptureDepth;
        _activeViews.Clear();
        foreach (ProfiledView view in _views.Values)
            view.Reset();
        _freeCommandBufferPool.AddRange(_activeFreeCommandBuffers);
        _freeCommandBuffers.Clear();
        _activeFreeCommandBuffers.Clear();
        _timelineEntryTracker.Clear();
    }

    public void SetCounterValues(double[] values)
    {
        IReadOnlyList<CounterDef> registry = CountersCollector.Registry;
        if (_counters.Length != values.Length)
            _counters = new CounterValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            CounterDef def = registry[i];
            _counters[i] = new CounterValue(def.Name, def.Category, def.Unit, values[i]);
        }
    }

    internal void SetCounters(IReadOnlyList<CounterValue> counters)
    {
        _counters = new CounterValue[counters.Count];
        for (int i = 0; i < counters.Count; i++)
            _counters[i] = counters[i];
    }

    public ProfiledView View(string name)
    {
        if (!_views.TryGetValue(name, out ProfiledView? view))
        {
            view = new ProfiledView(name);
            _views[name] = view;
        }
        if (view.MarkTouched())
        {
            _activeViews.Add(view);
            _timelineEntryTracker.Add(_activeViews.Count - 1);
        }
        return view;
    }

    public ProfiledCommandBuffer FreeCommandBuffer(ulong id, string name)
    {
        if (!_freeCommandBuffers.TryGetValue(id, out ProfiledCommandBuffer? cb))
        {
            if (_freeCommandBufferPool.Count > 0)
            {
                cb = _freeCommandBufferPool[^1];
                _freeCommandBufferPool.RemoveAt(_freeCommandBufferPool.Count - 1);
                cb.ResetForReuse(id, name);
            }
            else
            {
                cb = new ProfiledCommandBuffer(id, name);
            }
            _freeCommandBuffers[id] = cb;
            _activeFreeCommandBuffers.Add(cb);
            _timelineEntryTracker.Add(~(_activeFreeCommandBuffers.Count - 1));
        }
        else if (!string.IsNullOrEmpty(name))
        {
            cb.SetName(name);
        }
        return cb;
    }

    public void GetTimelineElement(int index, out ProfiledView? view, out ProfiledCommandBuffer? buffer)
    {
        int entry = _timelineEntryTracker[index];
        view = entry >= 0 ? _activeViews[entry] : null;
        buffer = entry < 0 ? _activeFreeCommandBuffers[~entry] : null;
    }

    public IEnumerator<(ProfiledView? View, ProfiledCommandBuffer? Buffer)> EnumerateTimeline()
    {
        for (int i = 0; i < _timelineEntryTracker.Count; i++)
        {
            GetTimelineElement(i, out ProfiledView? view, out ProfiledCommandBuffer? buffer);
            yield return (view, buffer);
        }
    }

    internal void SetTimeline(IReadOnlyList<int> entries)
    {
        _timelineEntryTracker.Clear();
        _timelineEntryTracker.AddRange(entries);
    }

    /// <summary>Deep, fully independent copy - the only place this frame's data is ever duplicated.
    /// Used exclusively when a capture is armed, since a Snapshot must survive indefinitely (saved to
    /// disk, held by the user) while this frame's ring slot keeps getting reset and reused.</summary>
    internal ProfiledFrame Clone()
    {
        var clone = new ProfiledFrame
        {
            FrameIndex = FrameIndex,
            FrameMilliseconds = FrameMilliseconds,
            Fps = Fps,
            HasCaptureDepth = HasCaptureDepth,
        };
        clone.SetCounters(_counters);
        foreach (ProfiledView view in _activeViews)
        {
            ProfiledView viewClone = view.Clone();
            clone._views[viewClone.Name] = viewClone;
            clone._activeViews.Add(viewClone);
        }
        foreach (ProfiledCommandBuffer cb in _activeFreeCommandBuffers)
        {
            ProfiledCommandBuffer cbClone = cb.Clone();
            clone._freeCommandBuffers[cbClone.Id] = cbClone;
            clone._activeFreeCommandBuffers.Add(cbClone);
        }
        clone._timelineEntryTracker.AddRange(_timelineEntryTracker);
        return clone;
    }
}