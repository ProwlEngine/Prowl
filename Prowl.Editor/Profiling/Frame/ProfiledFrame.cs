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

    public TimeSample? GpuRoot { get; internal set; }

    private CounterValue[] _counters = Array.Empty<CounterValue>();
    private readonly List<SubmitRecord> _submits = new();

    private readonly Dictionary<string, ProfiledView> _views = new();
    private readonly List<ProfiledView> _activeViews = new();

    public IReadOnlyList<CounterValue> Counters => _counters;
    public IReadOnlyList<ProfiledView> Views => _activeViews;
    public IReadOnlyList<SubmitRecord> Submits => _submits;

    internal void Reset(long frameIndex, bool hasCaptureDepth)
    {
        FrameIndex = frameIndex;
        FrameMilliseconds = 0;
        Fps = 0;
        HasCaptureDepth = hasCaptureDepth;
        GpuRoot = null;
        _submits.Clear();
        _activeViews.Clear();
        foreach (ProfiledView view in _views.Values)
            view.Reset();
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
            _activeViews.Add(view);
        return view;
    }

    public void SetGpuRoot(TimeSample root) => GpuRoot = root;
    public void AddSubmit(SubmitRecord s) => _submits.Add(s);

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
            GpuRoot = GpuRoot,
        };
        clone.SetCounters(_counters);
        clone._submits.AddRange(_submits);
        foreach (ProfiledView view in _activeViews)
        {
            ProfiledView viewClone = view.Clone();
            clone._views[viewClone.Name] = viewClone;
            clone._activeViews.Add(viewClone);
        }
        return clone;
    }
}