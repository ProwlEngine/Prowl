using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Motion;

/// <summary>Which foot and phase a <see cref="FootEvent"/> marks.</summary>
public enum FootPhase : byte
{
    LeftFootDown,
    RightFootDown,
    LeftFootPassing,
    RightFootPassing
}

/// <summary>
/// A time-ranged animation event on a clip, in normalized clip time [0,1]. Immediate events have
/// zero duration and fire as a point in time; duration events are "active" across a range.
/// </summary>
public abstract class AnimationEvent
{
    protected AnimationEvent(float startTime, float duration)
    {
        StartTime = Math.Clamp(startTime, 0f, 1f);
        Duration = MathF.Max(0f, duration);
    }

    public float StartTime { get; }
    public float Duration { get; }
    public float EndTime => StartTime + Duration;
    public bool IsImmediate => Duration <= 0f;

    /// <summary>True if a duration event is active at the given normalized time.</summary>
    public bool IsActiveAt(float normalizedTime)
        => !IsImmediate && ((normalizedTime >= StartTime && normalizedTime < EndTime) || normalizedTime < EndTime - 1f);
}

/// <summary>A named gameplay marker (footstep sound, hit frame, etc.).</summary>
public sealed class IdEvent : AnimationEvent
{
    public IdEvent(StringID id, float startTime, float duration = 0f) : base(startTime, duration) => Id = id;
    public StringID Id { get; }
}

/// <summary>A foot contact/passing marker used for grounding and locomotion sync.</summary>
public sealed class FootEvent : AnimationEvent
{
    public FootEvent(FootPhase phase, float startTime, float duration = 0f) : base(startTime, duration) => Phase = phase;
    public FootPhase Phase { get; }
    public bool IsLeft => Phase is FootPhase.LeftFootDown or FootPhase.LeftFootPassing;
    public bool IsFootDown => Phase is FootPhase.LeftFootDown or FootPhase.RightFootDown;

    private FootEvent? _mirrored;

    /// <summary>The same event for the other foot (created once and shared).</summary>
    public FootEvent Mirrored
    {
        get
        {
            if (_mirrored is null)
            {
                FootPhase phase = Phase switch
                {
                    FootPhase.LeftFootDown => FootPhase.RightFootDown,
                    FootPhase.RightFootDown => FootPhase.LeftFootDown,
                    FootPhase.LeftFootPassing => FootPhase.RightFootPassing,
                    _ => FootPhase.LeftFootPassing,
                };
                _mirrored = new FootEvent(phase, StartTime, Duration) { _mirrored = this };
            }
            return _mirrored;
        }
    }
}

/// <summary>A foot-phase query used by foot-event condition nodes; the last two group two phases.</summary>
public enum FootPhaseCondition : byte
{
    LeftFootDown,
    RightFootDown,
    LeftFootPassing,
    RightFootPassing,
    /// <summary>The left support phase: right foot passing OR left foot down.</summary>
    LeftPhase,
    /// <summary>The right support phase: left foot passing OR right foot down.</summary>
    RightPhase,
}

/// <summary>How restrictive a <see cref="TransitionEvent"/> region is. Higher value is more restrictive.</summary>
public enum TransitionRule : byte
{
    AllowTransition,
    ConditionallyAllowTransition,
    BlockTransition,
}

/// <summary>The question a transition-event condition asks of the most restrictive rule found.</summary>
public enum TransitionRuleCondition : byte
{
    AnyAllowed,
    FullyAllowed,
    ConditionallyAllowed,
    Blocked,
}

/// <summary>
/// Marks a region of a clip with a rule controlling whether the state machine may transition away
/// from it. Consumed by transition-event condition nodes.
/// </summary>
public sealed class TransitionEvent : AnimationEvent
{
    public TransitionEvent(TransitionRule rule, float startTime, float duration, StringID optionalId = default)
        : base(startTime, duration)
    {
        Rule = rule;
        OptionalId = optionalId;
    }

    public TransitionRule Rule { get; }

    /// <summary>An optional id letting a condition node filter to specific markers.</summary>
    public StringID OptionalId { get; }
}

/// <summary>A pure marker over the region where orientation warping applies.</summary>
public sealed class OrientationWarpEvent : AnimationEvent
{
    public OrientationWarpEvent(float startTime, float duration) : base(startTime, duration) { }
}

/// <summary>How a <see cref="SnapToFrameEvent"/> region snaps the sampled time to a frame.</summary>
public enum FrameSnapMode : byte { Floor, Round }

/// <summary>Marks a region that should be sampled discretely (snapped to a frame).</summary>
public sealed class SnapToFrameEvent : AnimationEvent
{
    public SnapToFrameEvent(FrameSnapMode mode, float startTime, float duration) : base(startTime, duration) => Mode = mode;
    public FrameSnapMode Mode { get; }
}

/// <summary>
/// Marks a region where a root-motion-override node should suspend its override (let the clip's own
/// root motion play through).
/// </summary>
public sealed class RootMotionEvent : AnimationEvent
{
    public RootMotionEvent(float startTime, float duration, float blendTime = 0.1f) : base(startTime, duration) => BlendTime = blendTime;
    public float BlendTime { get; }
}

/// <summary>
/// Which axes a <see cref="TargetWarpEvent"/> region warps. XY is the horizontal (XZ) plane and Z the
/// vertical (Y) axis, named after the ground plane rather than this library's Y up axes.
/// </summary>
public enum TargetWarpRule : byte { WarpXY, WarpZ, WarpXYZ, RotationOnly }

/// <summary>Marks a region where target (position) warping applies.</summary>
public sealed class TargetWarpEvent : AnimationEvent
{
    public TargetWarpEvent(TargetWarpRule rule, float startTime, float duration) : base(startTime, duration) => Rule = rule;
    public TargetWarpRule Rule { get; }
}

/// <summary>An event sampled during an update, with how strongly and from which branch it was sampled.</summary>
public struct SampledEvent
{
    public SampledEvent(AnimationEvent e, float percentageThrough, bool isFromActiveBranch, int sourceNodeIndex)
    {
        Event = e;
        PercentageThrough = percentageThrough;
        IsFromActiveBranch = isFromActiveBranch;
        SourceNodeIndex = sourceNodeIndex;
        Weight = 1f;
        IsIgnored = false;
    }

    public AnimationEvent Event;

    /// <summary>How far through a duration event playback was when sampled (1 for immediate events).</summary>
    public float PercentageThrough;

    /// <summary>The blend weight of the branch that produced the event (scaled by every blend above it).</summary>
    public float Weight;

    /// <summary>False when the event came from the losing side of a transition.</summary>
    public bool IsFromActiveBranch;

    /// <summary>True when a node chose to ignore the event (for example a layer set to ignore events).</summary>
    public bool IsIgnored;

    /// <summary>The graph node that sampled the event, or -1 outside a graph.</summary>
    public int SourceNodeIndex;
}

/// <summary>A contiguous half open range [Start, End) of events in a <see cref="SampledEventsBuffer"/>.</summary>
public readonly struct SampledEventRange
{
    public SampledEventRange(int start, int end)
    {
        Start = start;
        End = end;
    }

    public int Start { get; }
    public int End { get; }
    public int Length => End - Start;
    public bool IsEmpty => End <= Start;
}

/// <summary>
/// Collects the events sampled over a single update window and answers simple queries. Reusable
/// without the animation graph (foot events for grounding, id events for gameplay hooks). Graph
/// nodes record the range of events each subtree produced so blends can weight them.
/// </summary>
public sealed class SampledEventsBuffer : IReadOnlyList<SampledEvent>
{
    private readonly List<SampledEvent> _events = new();

    public int Count => _events.Count;

    /// <summary>
    /// Goes up with every change to the buffer: an event added, or one reweighted, ignored, mirrored or
    /// moved to the inactive branch. A reader that cached an answer can tell it may be out of date.
    /// </summary>
    public int Version { get; private set; }

    public SampledEvent this[int index] => _events[index];

    /// <summary>An empty range at the current end of the buffer, where the next events will be added.</summary>
    public SampledEventRange EmptyRangeAtEnd => new(_events.Count, _events.Count);

    public void Clear()
    {
        _events.Clear();
        Version++;
    }

    /// <summary>Adds an event sampled at full weight from the active branch.</summary>
    public void Add(AnimationEvent e) => Add(e, 1f, true, -1);

    public void Add(AnimationEvent e, float percentageThrough, bool isFromActiveBranch, int sourceNodeIndex)
    {
        ArgumentNullException.ThrowIfNull(e);
        _events.Add(new SampledEvent(e, percentageThrough, isFromActiveBranch, sourceNodeIndex));
        Version++;
    }

    /// <summary>Adds every event of another buffer (used by sub graphs), returning the range they occupy.</summary>
    public SampledEventRange Append(SampledEventsBuffer other)
    {
        int start = _events.Count;
        _events.AddRange(other._events);
        Version++;
        return new SampledEventRange(start, _events.Count);
    }

    /// <summary>A range from <paramref name="start"/> to the current end of the buffer.</summary>
    public SampledEventRange RangeFrom(int start) => new(start, _events.Count);

    /// <summary>Multiplies the weight of every event in the range.</summary>
    public void UpdateWeights(SampledEventRange range, float multiplier)
    {
        Span<SampledEvent> span = CollectionsMarshal.AsSpan(_events);
        for (int i = range.Start; i < range.End; i++)
            span[i].Weight *= multiplier;
        Version++;
    }

    /// <summary>Marks every event in the range as ignored.</summary>
    public void MarkIgnored(SampledEventRange range)
    {
        Span<SampledEvent> span = CollectionsMarshal.AsSpan(_events);
        for (int i = range.Start; i < range.End; i++)
            span[i].IsIgnored = true;
        Version++;
    }

    /// <summary>Swaps every foot event in the range for the other foot's (used when a pose is mirrored).</summary>
    public void MirrorFootEvents(SampledEventRange range)
    {
        Span<SampledEvent> span = CollectionsMarshal.AsSpan(_events);
        for (int i = range.Start; i < range.End; i++)
            if (span[i].Event is FootEvent foot)
                span[i].Event = foot.Mirrored;
        Version++;
    }

    /// <summary>Marks every event in the range as coming from an inactive branch.</summary>
    public void MarkFromInactiveBranch(SampledEventRange range)
    {
        Span<SampledEvent> span = CollectionsMarshal.AsSpan(_events);
        for (int i = range.Start; i < range.End; i++)
            span[i].IsFromActiveBranch = false;
        Version++;
    }

    /// <summary>
    /// Weights two adjacent ranges by a blend (the first by 1 - weight, the second by weight) and
    /// returns the range spanning both.
    /// </summary>
    public SampledEventRange BlendRanges(SampledEventRange first, SampledEventRange second, float weight)
    {
        UpdateWeights(first, 1f - weight);
        UpdateWeights(second, weight);
        if (first.IsEmpty)
            return second;
        if (second.IsEmpty)
            return first;
        return new SampledEventRange(Math.Min(first.Start, second.Start), Math.Max(first.End, second.End));
    }

    /// <summary>True when a non ignored id event with that id was sampled.</summary>
    public bool ContainsId(StringID id, bool onlyFromActiveBranch = false)
    {
        foreach (SampledEvent e in _events)
            if (Accept(e, onlyFromActiveBranch) && e.Event is IdEvent idEvent && idEvent.Id == id)
                return true;
        return false;
    }

    /// <summary>True when a non ignored event of type <typeparamref name="T"/> was sampled.</summary>
    public bool Contains<T>(bool onlyFromActiveBranch = false) where T : AnimationEvent
    {
        foreach (SampledEvent e in _events)
            if (Accept(e, onlyFromActiveBranch) && e.Event is T)
                return true;
        return false;
    }

    /// <summary>The non ignored foot events sampled this update.</summary>
    public IEnumerable<FootEvent> FootEvents()
    {
        foreach (SampledEvent e in _events)
            if (!e.IsIgnored && e.Event is FootEvent foot)
                yield return foot;
    }

    public List<SampledEvent>.Enumerator GetEnumerator() => _events.GetEnumerator();
    IEnumerator<SampledEvent> IEnumerable<SampledEvent>.GetEnumerator() => _events.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _events.GetEnumerator();

    private static bool Accept(in SampledEvent e, bool onlyFromActiveBranch) => !e.IsIgnored && (!onlyFromActiveBranch || e.IsFromActiveBranch);
}
