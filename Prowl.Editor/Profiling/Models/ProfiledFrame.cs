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

    /// <summary>Driver-reported VRAM budget/usage, polled once per frame from
    /// GraphicsDevice.GetMemoryBudget() - see MemoryBudgetInfo. False if the backend/device can't
    /// report it (e.g. VK_EXT_memory_budget unavailable); Budget/UsedBytes both read 0 in that case.</summary>
    public bool HasVramBudget { get; internal set; }
    public ulong VramBudgetBytes { get; internal set; }
    public ulong VramUsedBytes { get; internal set; }

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

    /// <summary>Sum of every view's GpuMilliseconds plus every free command buffer's.</summary>
    public double GpuMilliseconds
    {
        get
        {
            double sum = 0.0;
            foreach (ProfiledView view in _activeViews)
                sum += view.GpuMilliseconds;
            foreach (ProfiledCommandBuffer cb in _activeFreeCommandBuffers)
                sum += cb.GpuMilliseconds;
            return sum;
        }
    }

    /// <summary>Sum of every view's TrianglesDrawn plus every free command buffer's ClippingPrimitives.</summary>
    public ulong TrianglesDrawn
    {
        get
        {
            ulong sum = 0;
            foreach (ProfiledView view in _activeViews)
                sum += view.TrianglesDrawn;
            foreach (ProfiledCommandBuffer cb in _activeFreeCommandBuffers)
                sum += cb.ClippingPrimitives;
            return sum;
        }
    }

    /// <summary>Sum of every view's InputAssemblyVertices plus every free command buffer's.</summary>
    public ulong InputAssemblyVertices
    {
        get
        {
            ulong sum = 0;
            foreach (ProfiledView view in _activeViews)
                sum += view.InputAssemblyVertices;
            foreach (ProfiledCommandBuffer cb in _activeFreeCommandBuffers)
                sum += cb.InputAssemblyVertices;
            return sum;
        }
    }

    /// <summary>Sum of every view's DrawCallCount.</summary>
    public int DrawCallCount
    {
        get
        {
            int sum = 0;
            foreach (ProfiledView view in _activeViews)
                sum += view.DrawCallCount;
            return sum;
        }
    }

    /// <summary>Sum of every view's DispatchCallCount.</summary>
    public int DispatchCallCount
    {
        get
        {
            int sum = 0;
            foreach (ProfiledView view in _activeViews)
                sum += view.DispatchCallCount;
            return sum;
        }
    }

    /// <summary>Sum of every view's PipelineSwitchCount plus every free command buffer's.</summary>
    public int PipelineSwitchCount
    {
        get
        {
            int sum = 0;
            foreach (ProfiledView view in _activeViews)
                sum += view.PipelineSwitchCount;
            foreach (ProfiledCommandBuffer cb in _activeFreeCommandBuffers)
                sum += cb.PipelineSwitchCount;
            return sum;
        }
    }

    public int ViewCount => _activeViews.Count;

    /// <summary>Sum of every view's pass count.</summary>
    public int PassCount
    {
        get
        {
            int sum = 0;
            foreach (ProfiledView view in _activeViews)
                sum += view.Passes.Count;
            return sum;
        }
    }

    /// <summary>Sum of every pass's command buffer count plus FreeCommandBuffers - also the frame's total submit count, since one submit maps to exactly one command buffer.</summary>
    public int CommandBufferCount
    {
        get
        {
            int sum = _activeFreeCommandBuffers.Count;
            foreach (ProfiledView view in _activeViews)
                foreach (ProfiledPass pass in view.Passes)
                    sum += pass.CommandBuffers.Count;
            return sum;
        }
    }

    internal IReadOnlyList<int> TimelineEntries => _timelineEntryTracker;

    internal void Reset(long frameIndex, bool hasCaptureDepth)
    {
        FrameIndex = frameIndex;
        FrameMilliseconds = 0;
        Fps = 0;
        HasCaptureDepth = hasCaptureDepth;
        HasVramBudget = false;
        VramBudgetBytes = 0;
        VramUsedBytes = 0;
        _activeViews.Clear();
        foreach (ProfiledView view in _views.Values)
            view.Reset();
        _freeCommandBufferPool.AddRange(_activeFreeCommandBuffers);
        _freeCommandBuffers.Clear();
        _activeFreeCommandBuffers.Clear();
        _timelineEntryTracker.Clear();
    }

    internal void SetVramBudget(in MemoryBudgetInfo budget)
    {
        HasVramBudget = budget.IsSupported;
        VramBudgetBytes = budget.BudgetBytes;
        VramUsedBytes = budget.UsageBytes;
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
            HasVramBudget = HasVramBudget,
            VramBudgetBytes = VramBudgetBytes,
            VramUsedBytes = VramUsedBytes,
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