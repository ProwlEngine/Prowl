using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// The playable part of a clip shared by every storage format: timing, root motion, sync track,
/// events and secondary clips. Derived types only decide how key frames are stored and sampled, so
/// the graph and animators play an <see cref="AnimationClip"/> and a
/// <see cref="CompressedAnimationClip"/> the same way.
/// </summary>
public abstract class AnimationClipBase
{
    private readonly AnimationEvent[] _events;
    private readonly AnimationClipBase[] _secondaryClips;

    protected AnimationClipBase(
        Skeleton skeleton,
        int frameCount,
        float durationSeconds,
        bool isAdditive,
        RootMotion? rootMotion,
        SyncTrack? syncTrack,
        IReadOnlyList<AnimationEvent>? events,
        IReadOnlyList<AnimationClipBase>? secondaryClips)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (frameCount <= 0)
            throw new ArgumentException("A clip needs at least one key frame.", nameof(frameCount));
        if (!(durationSeconds > 0f) || !float.IsFinite(durationSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Clip duration must be positive.");

        Skeleton = skeleton;
        FrameCount = frameCount;
        Duration = durationSeconds;
        IsAdditive = isAdditive;
        RootMotion = rootMotion;
        SyncTrack = syncTrack ?? SyncTrack.Default;

        _events = events is null ? Array.Empty<AnimationEvent>() : events.ToArray();
        Array.Sort(_events, static (a, b) => a.StartTime.CompareTo(b.StartTime));
        _secondaryClips = secondaryClips is null ? Array.Empty<AnimationClipBase>() : secondaryClips.ToArray();
    }

    public Skeleton Skeleton { get; }

    /// <summary>Number of key frames.</summary>
    public int FrameCount { get; }

    /// <summary>Clip length in seconds.</summary>
    public float Duration { get; }

    /// <summary>Sampling rate (frames per second), 0 for a single frame clip.</summary>
    public float FramesPerSecond => FrameCount > 1 ? (FrameCount - 1) / Duration : 0f;

    /// <summary>True if this clip stores additive (delta) data rather than a full pose.</summary>
    public bool IsAdditive { get; }

    public bool HasRootMotion => RootMotion is not null;

    public RootMotion? RootMotion { get; }

    /// <summary>The sync track used for synchronized blending (a single event default if none was supplied).</summary>
    public SyncTrack SyncTrack { get; }

    /// <summary>The clip's events, sorted by start time.</summary>
    public IReadOnlyList<AnimationEvent> Events => _events;

    /// <summary>Secondary clips driving other skeletons alongside this one (e.g. a held weapon).</summary>
    public IReadOnlyList<AnimationClipBase> SecondaryClips => _secondaryClips;

    /// <summary>True if this clip carries secondary animations.</summary>
    public bool HasSecondaryClips => _secondaryClips.Length > 0;

    /// <summary>Average linear root speed (units per second) over the clip, 0 without root motion.</summary>
    public float AverageLinearSpeed => RootMotion is null ? 0f : Float3.Length(RootMotion.TotalDelta.position) / Duration;

    /// <summary>Returns the secondary clip authored for the given skeleton, or null if none.</summary>
    public AnimationClipBase? GetSecondaryForSkeleton(Skeleton skeleton)
    {
        foreach (AnimationClipBase clip in _secondaryClips)
            if (ReferenceEquals(clip.Skeleton, skeleton))
                return clip;
        return null;
    }

    /// <summary>Maps a normalized time in [0,1] to a <see cref="FrameTime"/>. A non finite time reads as 0.</summary>
    public FrameTime GetFrameTime(float percentageThrough)
    {
        if (FrameCount <= 1)
            return new FrameTime(0, 0f);

        float clamped = float.IsFinite(percentageThrough) ? Maths.Clamp(percentageThrough, 0f, 1f) : 0f;
        float frameValue = clamped * (FrameCount - 1);
        int index = (int)MathF.Floor(frameValue);
        if (index >= FrameCount - 1)
            return new FrameTime(FrameCount - 1, 0f);

        return new FrameTime(index, frameValue - index);
    }

    /// <summary>
    /// Samples the clip at a frame time into <paramref name="result"/>. With a
    /// <paramref name="mapping"/> the result is a pose of another skeleton, whose bones take the source
    /// bone of the same name and keep their reference pose when the clip has no bone for them.
    /// </summary>
    public abstract void GetPose(FrameTime frameTime, Pose result, SkeletonMapping? mapping);

    /// <summary>Samples the clip at a frame time into <paramref name="result"/>.</summary>
    public void GetPose(FrameTime frameTime, Pose result) => GetPose(frameTime, result, null);

    /// <summary>Samples the clip at a normalized time in [0,1] into <paramref name="result"/>.</summary>
    public void GetPose(float percentageThrough, Pose result) => GetPose(GetFrameTime(percentageThrough), result, null);

    /// <summary>Samples the clip at a normalized time in [0,1] onto another skeleton.</summary>
    public void GetPose(float percentageThrough, Pose result, SkeletonMapping? mapping) => GetPose(GetFrameTime(percentageThrough), result, mapping);

    /// <summary>Builds the mapping needed to play this clip on <paramref name="target"/>, or null when it is the clip's own skeleton.</summary>
    public SkeletonMapping? GetMappingTo(Skeleton target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ReferenceEquals(target, Skeleton) ? null : SkeletonMapping.Create(Skeleton, target);
    }

    /// <summary>Checks that a sampling target matches this clip, directly or through a mapping.</summary>
    protected void ValidateTarget(Pose result, SkeletonMapping? mapping)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (mapping is null)
        {
            if (result.BoneCount != Skeleton.BoneCount)
                throw new ArgumentException("Result pose bone count must match the clip skeleton.", nameof(result));
            return;
        }

        if (!ReferenceEquals(mapping.Source, Skeleton))
            throw new ArgumentException("The mapping does not start from this clip's skeleton.", nameof(mapping));
        if (result.BoneCount != mapping.Target.BoneCount)
            throw new ArgumentException("Result pose bone count must match the mapping target skeleton.", nameof(result));
        if (result.FloatChannelCount != mapping.Target.FloatChannelCount)
            throw new ArgumentException("Result pose channel count must match the mapping target skeleton.", nameof(result));
    }

    /// <summary>
    /// Adds every event triggered while advancing forward from <paramref name="fromNormalized"/> to
    /// <paramref name="toNormalized"/> (both in [0,1]) into <paramref name="buffer"/>. The window
    /// wraps past the end when <paramref name="toNormalized"/> is before <paramref name="fromNormalized"/>
    /// or <paramref name="looped"/> is set. Immediate events fire when their time is crossed, duration
    /// events are included while they overlap the window.
    /// </summary>
    public void SampleEvents(float fromNormalized, float toNormalized, SampledEventsBuffer buffer, bool looped = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var span = new PlaybackSpan(fromNormalized, toNormalized, looped || toNormalized < fromNormalized ? 1 : 0, false, false);
        SampleEvents(span, buffer, true, -1);
    }

    /// <summary>Adds the events crossed by a playback span, tagged with the branch state and producing node.</summary>
    public void SampleEvents(in PlaybackSpan span, SampledEventsBuffer buffer, bool isFromActiveBranch, int nodeIndex)
    {
        if (_events.Length == 0)
            return;

        Span<bool> added = _events.Length <= 256 ? stackalloc bool[_events.Length] : new bool[_events.Length];
        float from = Mirror(span.From, span.Backward);
        float to = Mirror(span.To, span.Backward);

        if (span.Wraps == 0)
        {
            AddEventsInWindow(from, to, span.IncludeStart, span, buffer, added, isFromActiveBranch, nodeIndex);
            return;
        }

        AddEventsInWindow(from, 1f, span.IncludeStart, span, buffer, added, isFromActiveBranch, nodeIndex);
        for (int loop = 1; loop < span.Wraps; loop++)
        {
            added.Clear();
            AddEventsInWindow(0f, 1f, true, span, buffer, added, isFromActiveBranch, nodeIndex);
        }
        if (span.Wraps > 1)
            added.Clear();
        AddEventsInWindow(0f, to, true, span, buffer, added, isFromActiveBranch, nodeIndex);
    }

    /// <summary>
    /// The root motion delta produced by a playback span, including every full loop it wraps across
    /// and backward playback. Identity when the clip has no root motion.
    /// </summary>
    public Transform3D GetRootMotionDelta(in PlaybackSpan span)
    {
        if (RootMotion is null)
            return Transform3D.Identity;

        if (span.Wraps == 0)
            return RootMotion.SampleDelta(span.From, span.To);

        float end = span.Backward ? 0f : 1f;
        float start = span.Backward ? 1f : 0f;
        Transform3D delta = RootMotion.SampleDelta(span.From, end);
        Transform3D fullLoop = RootMotion.SampleDelta(start, end);
        for (int loop = 1; loop < span.Wraps; loop++)
            delta = TransformOps.Combine(delta, fullLoop);
        return TransformOps.Combine(delta, RootMotion.SampleDelta(start, span.To));
    }

    private void AddEventsInWindow(float from, float to, bool includeStart, in PlaybackSpan span, SampledEventsBuffer buffer, Span<bool> added, bool isFromActiveBranch, int nodeIndex)
    {
        for (int i = 0; i < _events.Length; i++)
        {
            if (added[i])
                continue;

            AnimationEvent e = _events[i];
            bool triggered;
            float percentage = 1f;

            if (e.IsImmediate)
            {
                float t = span.Backward ? 1f - e.StartTime : e.StartTime;
                triggered = (includeStart ? t >= from : t > from) && t <= to;
            }
            else
            {
                triggered = Overlaps(e, from, to, span.Backward);
                float through = PercentageThrough(e, Mirror(span.To, span.Backward), span.Backward);
                percentage = span.Backward ? 1f - through : through;
            }

            if (!triggered)
                continue;

            added[i] = true;
            buffer.Add(e, percentage, isFromActiveBranch, nodeIndex);
        }
    }

    // Duration events may run past the end of the clip, so the tail wraps back to the start.
    private static bool Overlaps(AnimationEvent e, float from, float to, bool backward)
    {
        (float start, float end) = EventRange(e, backward);
        for (float shift = -1f; shift <= 1f; shift += 1f)
        {
            float s = start + shift;
            float en = end + shift;
            if (from == to ? s <= from && en > from : s < to && en > from)
                return true;
        }
        return false;
    }

    // How far through a duration event playback is at the final time of the step (1 once it has passed).
    private static float PercentageThrough(AnimationEvent e, float time, bool backward)
    {
        (float start, float end) = EventRange(e, backward);
        for (float shift = -1f; shift <= 1f; shift += 1f)
            if (time >= start + shift && time < end + shift)
                return Math.Clamp((time - start - shift) / e.Duration, 0f, 1f);
        return 1f;
    }

    private static (float Start, float End) EventRange(AnimationEvent e, bool backward)
        => backward ? (1f - e.EndTime, 1f - e.StartTime) : (e.StartTime, e.EndTime);

    private static float Mirror(float t, bool backward) => backward ? 1f - t : t;
}

/// <summary>
/// A normalized playback step on a clip: the start and end times plus how many times playback
/// wrapped (past the end going forward, past the start going backward) in between.
/// </summary>
public readonly struct PlaybackSpan
{
    public PlaybackSpan(float from, float to, int wraps, bool backward, bool includeStart)
    {
        From = from;
        To = to;
        Wraps = wraps;
        Backward = backward;
        IncludeStart = includeStart;
    }

    public float From { get; }
    public float To { get; }
    public int Wraps { get; }
    public bool Backward { get; }

    /// <summary>True on the first step after playback starts, so an event exactly at the start fires.</summary>
    public bool IncludeStart { get; }
}
