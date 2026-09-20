using System.Collections.Generic;

namespace Prowl.Motion;

/// <summary>A sync event: a named marker at a normalized start time in [0,1) along a clip, with its duration.</summary>
public readonly struct SyncEvent
{
    public SyncEvent(StringID id, float startTime, float duration = 0f)
    {
        Id = id;
        StartTime = startTime;
        Duration = duration;
    }

    public StringID Id { get; }
    public float StartTime { get; }

    /// <summary>Normalized length until the next event. The last event wraps around to the first.</summary>
    public float Duration { get; }
}

/// <summary>A position within a sync track: an event index plus the fraction through that event.</summary>
public readonly struct SyncTrackTime
{
    public SyncTrackTime(int eventIndex, float percentageThrough)
    {
        EventIndex = eventIndex;
        PercentageThrough = percentageThrough;
    }

    public int EventIndex { get; }
    public float PercentageThrough { get; }

    public float ToFloat() => EventIndex + PercentageThrough;
}

/// <summary>A start and end position on a sync track, used to drive synchronized updates.</summary>
public readonly struct SyncTrackTimeRange
{
    public SyncTrackTimeRange(SyncTrackTime start, SyncTrackTime end)
    {
        Start = start;
        End = end;
    }

    public SyncTrackTime Start { get; }
    public SyncTrackTime End { get; }
}

/// <summary>
/// Maps normalized clip time to "sync event time" (event index + fraction) and back, so two clips
/// can be blended in phase rather than by raw time. Events
/// cover the whole track: the last one wraps past the end back to the first. A clip with no sync
/// events behaves as a single event spanning the whole clip, identical to raw time playback.
/// </summary>
public sealed class SyncTrack
{
    /// <summary>A track with a single event covering [0,1): synchronized playback equals raw playback.</summary>
    public static readonly SyncTrack Default = new(new[] { new SyncEvent(StringID.Invalid, 0f) });

    private SyncEvent[] _events;
    private List<SyncEvent[]>? _buffers;
    private int _startEventOffset;
    private float _timeOffset;

    /// <summary>A scratch track owned by a blending node, filled by <see cref="SetToBlend"/>.</summary>
    internal SyncTrack() => _events = new[] { new SyncEvent(StringID.Invalid, 0f, 1f) };

    /// <summary>Builds a track from event markers (id and start time). Durations are derived from the gaps.</summary>
    public SyncTrack(IReadOnlyList<SyncEvent> markers, int startEventOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (markers.Count == 0)
            throw new ArgumentException("A sync track needs at least one event.", nameof(markers));

        var sorted = new SyncEvent[markers.Count];
        for (int i = 0; i < sorted.Length; i++)
        {
            float start = markers[i].StartTime;
            if (!float.IsFinite(start) || start < 0f || start >= 1f)
                throw new ArgumentOutOfRangeException(nameof(markers), "Sync event start times must be in [0,1).");
            sorted[i] = markers[i];
        }
        Array.Sort(sorted, static (a, b) => a.StartTime.CompareTo(b.StartTime));

        _events = new SyncEvent[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            float duration = i + 1 < sorted.Length
                ? sorted[i + 1].StartTime - sorted[i].StartTime
                : 1f - (sorted[i].StartTime - sorted[0].StartTime);
            if (duration <= 1e-6f)
                throw new ArgumentException("Sync events must have distinct start times.", nameof(markers));
            _events[i] = new SyncEvent(sorted[i].Id, sorted[i].StartTime, duration);
        }

        _startEventOffset = Wrap(startEventOffset);
    }

    /// <summary>
    /// Blends two tracks. The result has the lowest common multiple of the two event counts, each
    /// event's duration lerped by <paramref name="blendWeight"/>, normalized to cover the whole track.
    /// </summary>
    public SyncTrack(SyncTrack track0, SyncTrack track1, float blendWeight)
    {
        _events = Array.Empty<SyncEvent>();
        SetToBlend(track0, track1, blendWeight);
    }

    /// <summary>Overwrites this track with the blend of two tracks, reusing its event buffer when possible.</summary>
    internal void SetToBlend(SyncTrack track0, SyncTrack track1, float blendWeight)
    {
        ArgumentNullException.ThrowIfNull(track0);
        ArgumentNullException.ThrowIfNull(track1);
        float weight = float.IsFinite(blendWeight) ? Math.Clamp(blendWeight, 0f, 1f) : 0f;

        int count0 = track0.EventCount;
        int count1 = track1.EventCount;
        int lcm = LowestCommonMultiple(count0, count1);
        float scale0 = (float)count0 / lcm;
        float scale1 = (float)count1 / lcm;

        bool aliased = ReferenceEquals(track0, this) || ReferenceEquals(track1, this);
        SyncEvent[] events = !aliased && _events.Length == lcm ? _events : TakeBuffer(lcm);
        float start = 0f;
        for (int i = 0; i < lcm; i++)
        {
            SyncEvent e0 = track0.GetEvent(i);
            SyncEvent e1 = track1.GetEvent(i);
            float duration = e0.Duration * scale0 + (e1.Duration * scale1 - e0.Duration * scale0) * weight;
            events[i] = new SyncEvent(weight > 0.5f ? e1.Id : e0.Id, start, duration);
            start += duration;
        }

        float normalize = start > 0f ? 1f / start : 1f;
        for (int i = 0; i < lcm; i++)
            events[i] = new SyncEvent(events[i].Id, events[i].StartTime * normalize, events[i].Duration * normalize);
        events[lcm - 1] = new SyncEvent(events[lcm - 1].Id, events[lcm - 1].StartTime, 1f - events[lcm - 1].StartTime);

        float first0 = track0.FirstEventTime;
        float first1 = track1.FirstEventTime;
        float offsetDelta = first1 - first0;
        offsetDelta -= MathF.Round(offsetDelta);

        if (!ReferenceEquals(events, _events) && _events.Length > 0)
            (_buffers ??= new List<SyncEvent[]>()).Add(_events);
        _events = events;
        _startEventOffset = 0;
        _timeOffset = first0 + offsetDelta * weight;
        _timeOffset -= MathF.Floor(_timeOffset);
    }

    // Where the start event begins in clip time, so a blend of offset tracks stays aligned with its clips.
    private float FirstEventTime
    {
        get
        {
            float t = GetEvent(0).StartTime + _timeOffset;
            return t - MathF.Floor(t);
        }
    }

    /// <summary>The blended duration (seconds) of two tracks played in sync, before their event rates are matched.</summary>
    public static float CalculateDurationSynchronized(float duration0, float duration1, int eventCount0, int eventCount1, int blendedEventCount, float blendWeight)
    {
        float scaled0 = duration0 * ((float)blendedEventCount / Math.Max(1, eventCount0));
        float scaled1 = duration1 * ((float)blendedEventCount / Math.Max(1, eventCount1));
        return scaled0 + (scaled1 - scaled0) * Math.Clamp(blendWeight, 0f, 1f);
    }

    public int EventCount => _events.Length;

    /// <summary>The event the track treats as its first (a track can start part way through its events).</summary>
    public int StartEventOffset => _startEventOffset;

    /// <summary>The event at an index relative to the start offset (wraps).</summary>
    public SyncEvent GetEvent(int index) => _events[Wrap(index + _startEventOffset)];

    /// <summary>Normalized duration of an event (relative to the start offset).</summary>
    public float GetEventDuration(int index) => GetEvent(index).Duration;

    /// <summary>
    /// Maps a normalized time to its sync position. Values above 1 count whole loops into the event
    /// index. A time before the first event lies in the wrapped last event.
    /// </summary>
    public SyncTrackTime GetTime(float normalizedTime)
    {
        if (!float.IsFinite(normalizedTime))
            normalizedTime = 0f;

        int loops = 0;
        float t = normalizedTime - _timeOffset;
        if (t != 1f)
        {
            loops = (int)MathF.Floor(t);
            t -= loops;
        }

        int count = _events.Length;
        int index;
        float fraction;
        if (t < _events[0].StartTime)
        {
            index = count - 1;
            SyncEvent last = _events[index];
            fraction = (t + 1f - last.StartTime) / last.Duration;
        }
        else
        {
            index = count - 1;
            for (int i = 0; i < count; i++)
            {
                if (_events[i].StartTime + _events[i].Duration >= t)
                {
                    index = i;
                    break;
                }
            }
            fraction = (t - _events[index].StartTime) / _events[index].Duration;
        }

        return new SyncTrackTime(Wrap(index + loops * count - _startEventOffset), Math.Clamp(fraction, 0f, 1f));
    }

    /// <summary>Maps a sync position back to a normalized time in [0,1].</summary>
    public float GetPercentageThrough(SyncTrackTime time)
    {
        SyncEvent e = _events[Wrap(time.EventIndex + _startEventOffset)];
        float t = e.StartTime + e.Duration * Math.Clamp(time.PercentageThrough, 0f, 1f) + _timeOffset;
        while (t > 1f)
            t -= 1f;
        if (t >= 1f && _events[0].StartTime + _timeOffset > 0f)
            t = 0f;
        return t;
    }

    /// <summary>
    /// The sync position of a time that may lie outside [0,1], with the event index counted on across
    /// loops (it grows by the event count each time playback passes the start event). The difference of
    /// two such positions is the distance played, in events, including direction and whole loops.
    /// </summary>
    internal SyncTrackTime GetUnwrappedTime(float time)
    {
        if (!float.IsFinite(time))
            time = 0f;
        float local = time - MathF.Floor(time);
        if (local == 0f && time > 0f)
            local = 1f;
        int loops = (int)MathF.Round(time - local);
        if (local < FirstEventTime)
            loops--;
        SyncTrackTime wrapped = GetTime(local);
        return new SyncTrackTime(wrapped.EventIndex + loops * _events.Length, wrapped.PercentageThrough);
    }

    /// <summary>Maps a sync position back to a normalized time in [0,1] (same as <see cref="GetPercentageThrough"/>).</summary>
    public float GetNormalizedTime(SyncTrackTime time) => GetPercentageThrough(time);

    /// <summary>The sync position reached by advancing <paramref name="deltaPercentage"/> of the track from a position.</summary>
    public SyncTrackTime UpdateEventTime(SyncTrackTime start, float deltaPercentage) => GetTime(GetPercentageThrough(start) + deltaPercentage);

    /// <summary>The fraction of the whole track covered moving forward from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public float CalculatePercentageCovered(SyncTrackTime start, SyncTrackTime end)
    {
        int count = _events.Length;
        int startIndex = Wrap(start.EventIndex);
        int endIndex = Wrap(end.EventIndex);

        float distance;
        if (startIndex == endIndex && start.PercentageThrough <= end.PercentageThrough)
        {
            distance = end.PercentageThrough - start.PercentageThrough;
        }
        else
        {
            distance = 1f - start.PercentageThrough;
            for (int i = Wrap(startIndex + 1); i != endIndex; i = Wrap(i + 1))
                distance += 1f;
            distance += end.PercentageThrough;
        }
        return distance / count;
    }

    /// <summary>The fraction of the whole track covered by a sync range.</summary>
    public float CalculatePercentageCovered(SyncTrackTimeRange range) => CalculatePercentageCovered(range.Start, range.End);

    /// <summary>The first event (relative to the start offset) with the given id, or the start event if none match.</summary>
    public int GetEventIndexForId(StringID id)
    {
        for (int i = 0; i < _events.Length; i++)
            if (_events[i].Id == id)
                return Wrap(i - _startEventOffset);
        return 0;
    }

    /// <summary>The event (relative to the start offset) with the given id closest to <paramref name="time"/>, or the start event if none match.</summary>
    public int GetClosestEventIndexForId(SyncTrackTime time, StringID id)
    {
        int count = _events.Length;
        int origin = Wrap(time.EventIndex + _startEventOffset);
        if (_events[origin].Id == id)
            return Wrap(origin - _startEventOffset);

        for (int distance = 1; distance <= count / 2; distance++)
        {
            int lower = Wrap(origin - distance);
            if (_events[lower].Id == id)
                return Wrap(lower - _startEventOffset);
            int upper = Wrap(origin + distance);
            if (_events[upper].Id == id)
                return Wrap(upper - _startEventOffset);
        }
        return 0;
    }

    /// <summary>
    /// Maps a sync position on this track to a normalized time on <paramref name="other"/>. Matches the
    /// event by id when possible (the closest matching event), otherwise by index, so the two clips
    /// stay phase aligned.
    /// </summary>
    public float RemapTo(SyncTrackTime position, SyncTrack other)
    {
        ArgumentNullException.ThrowIfNull(other);

        StringID id = GetEvent(position.EventIndex).Id;
        int matched = id.IsValid && other.HasEventId(id)
            ? other.GetClosestEventIndexForId(new SyncTrackTime(position.EventIndex, position.PercentageThrough), id)
            : position.EventIndex;
        return other.GetPercentageThrough(new SyncTrackTime(matched, position.PercentageThrough));
    }

    private SyncEvent[] TakeBuffer(int length)
    {
        if (_buffers is not null)
        {
            for (int i = 0; i < _buffers.Count; i++)
            {
                if (_buffers[i].Length != length)
                    continue;
                SyncEvent[] buffer = _buffers[i];
                _buffers.RemoveAt(i);
                return buffer;
            }
        }
        return new SyncEvent[length];
    }

    private bool HasEventId(StringID id)
    {
        foreach (SyncEvent e in _events)
            if (e.Id == id)
                return true;
        return false;
    }

    private int Wrap(int index)
    {
        int count = _events.Length;
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private static int LowestCommonMultiple(int a, int b)
    {
        int x = a, y = b;
        while (y != 0)
            (x, y) = (y, x % y);
        return a / x * b;
    }
}
