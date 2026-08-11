using System;
using System.Collections.Generic;

using Prowl.Editor.Profiling;

namespace Prowl.Editor.GUI.RenderProfiler.Data;

// Frames/Counter both truncate the ring buffer's history to whatever frame is currently
// selected - the cutoff logic lives here once instead of being repeated at every call site.
public sealed class LiveHistory : IProfilerHistory
{
    private readonly EditorProfiler _profiler;
    private readonly long? _cutoffFrameIndex;

    public LiveHistory(EditorProfiler profiler, long? cutoffFrameIndex)
    {
        _profiler = profiler;
        _cutoffFrameIndex = cutoffFrameIndex;
    }

    public IReadOnlyList<ProfiledFrame> Frames
    {
        get
        {
            IReadOnlyList<ProfiledFrame> history = _profiler.History;
            if (!_cutoffFrameIndex.HasValue)
                return history;

            var values = new List<ProfiledFrame>(history.Count);
            foreach (ProfiledFrame frame in history)
            {
                if (frame.FrameIndex > _cutoffFrameIndex.Value)
                    break;
                values.Add(frame);
            }
            return values;
        }
    }

    public IReadOnlyList<double> Counter(string name)
    {
        IReadOnlyList<double> full = _profiler.CounterHistory(name);
        if (!_cutoffFrameIndex.HasValue)
            return full;

        IReadOnlyList<ProfiledFrame> history = _profiler.History;
        int count = 0;
        foreach (ProfiledFrame frame in history)
        {
            if (frame.FrameIndex > _cutoffFrameIndex.Value)
                break;
            count++;
        }

        count = Math.Min(count, full.Count);
        if (count >= full.Count)
            return full;

        var truncated = new List<double>(count);
        for (int i = 0; i < count; i++)
            truncated.Add(full[i]);
        return truncated;
    }
}
