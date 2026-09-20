namespace Prowl.Motion;

/// <summary>
/// Shared helpers for synchronized (phase locked) blending: working out the sync range a blend
/// plays this update and driving children over it so their clips stay aligned.
/// </summary>
internal static class GraphSync
{
    /// <summary>
    /// The sync range a blend node plays this update: the parent's range when a parent drives it,
    /// otherwise its own clock advanced by delta time over <paramref name="duration"/> along
    /// <paramref name="track"/>.
    /// </summary>
    public static SyncTrackTimeRange UpdateRange(GraphContext context, SyncTrack track, float currentTime, float duration, bool loop = true)
        => UpdateRange(context, track, currentTime, duration, loop, out _);

    /// <summary>As <see cref="UpdateRange(GraphContext, SyncTrack, float, float, bool)"/>, also returning the node's new normalized time.</summary>
    public static SyncTrackTimeRange UpdateRange(GraphContext context, SyncTrack track, float currentTime, float duration, bool loop, out float newTime)
    {
        if (context.SyncRange is { } parentRange)
        {
            newTime = track.GetPercentageThrough(parentRange.End);
            return parentRange;
        }

        float delta = duration > 1e-6f ? context.DeltaTime / duration : 0f;
        if (!float.IsFinite(delta))
            delta = 0f;

        float to = currentTime + delta;
        if (!loop)
            to = Math.Clamp(to, 0f, 1f);
        newTime = loop ? to - MathF.Floor(to) : to;
        return Range(track, currentTime, to);
    }

    /// <summary>
    /// The sync range from <paramref name="from"/> to <paramref name="to"/>, where <paramref name="to"/>
    /// may lie outside [0,1]. The end index counts on across loops, so the range keeps its direction and
    /// every whole loop for children whose tracks have other event counts.
    /// </summary>
    public static SyncTrackTimeRange Range(SyncTrack track, float from, float to)
    {
        SyncTrackTime start = track.GetUnwrappedTime(from);
        SyncTrackTime end = track.GetUnwrappedTime(to);
        int count = track.EventCount;
        int shift = (int)MathF.Floor((float)start.EventIndex / count) * count;
        return new SyncTrackTimeRange(new SyncTrackTime(start.EventIndex - shift, start.PercentageThrough), new SyncTrackTime(end.EventIndex - shift, end.PercentageThrough));
    }

    /// <summary>The sync range of a step already played from one normalized time to another in a known direction (at most one wrap).</summary>
    public static SyncTrackTimeRange DirectedRange(SyncTrack track, float previous, float current, bool backward)
    {
        float to = current;
        if (!backward && current < previous)
            to += 1f;
        else if (backward && current > previous)
            to -= 1f;
        return Range(track, previous, to);
    }

    /// <summary>
    /// How a node on <paramref name="track"/> plays a sync range: the normalized start and end times, the
    /// direction, and how many times it wraps (whole loops of the range included).
    /// </summary>
    public static void Resolve(SyncTrack track, SyncTrackTimeRange range, out float from, out float to, out bool backward, out int wraps)
    {
        from = track.GetPercentageThrough(range.Start);
        to = track.GetPercentageThrough(range.End);
        float events = range.End.ToFloat() - range.Start.ToFloat();
        backward = events < 0f;
        wraps = (int)(MathF.Abs(events) / track.EventCount);
        if (backward ? to > from : to < from)
            wraps++;
    }

    /// <summary>
    /// The weight to blend two children's timing by: a child with no duration (a static pose) has no
    /// clock, so the timed child drives the blend alone.
    /// </summary>
    public static float TimingWeight(float duration0, float duration1, float weight)
    {
        if (duration0 <= 1e-6f && duration1 > 1e-6f)
            return 1f;
        if (duration1 <= 1e-6f && duration0 > 1e-6f)
            return 0f;
        return weight;
    }

    /// <summary>Updates a child over a sync range, restoring the context afterwards.</summary>
    public static void UpdateSynchronized(GraphContext context, PoseNodeInstance child, SyncTrackTimeRange range)
    {
        SyncTrackTimeRange? saved = context.SyncRange;
        context.SyncRange = range;
        child.Update(context);
        context.SyncRange = saved;
    }

    /// <summary>Updates a child at zero time (produces its pose without advancing), restoring the context afterwards.</summary>
    public static void UpdateFrozen(GraphContext context, PoseNodeInstance child)
    {
        float saved = context.DeltaTime;
        context.DeltaTime = 0f;
        child.Update(context);
        context.DeltaTime = saved;
    }

    /// <summary>Updates a child as the losing side of a transition, restoring the branch state afterwards.</summary>
    public static void UpdateInactive(GraphContext context, PoseNodeInstance child, SyncTrackTimeRange? range)
    {
        BranchState savedBranch = context.BranchState;
        SyncTrackTimeRange? savedRange = context.SyncRange;
        context.BranchState = BranchState.Inactive;
        if (range.HasValue)
            context.SyncRange = range;
        child.Update(context);
        context.BranchState = savedBranch;
        context.SyncRange = savedRange;
    }

    /// <summary>
    /// Finds the bracketing pair (and blend weight) in a sorted 1D blend space. A non finite value
    /// reads as the lowest entry.
    /// </summary>
    public static void ResolvePair((int Child, float Threshold)[] entries, float value, out int low, out int high, out float t)
    {
        int n = entries.Length;
        if (n == 1 || !float.IsFinite(value) || value <= entries[0].Threshold)
        {
            low = high = 0;
            t = 0f;
            return;
        }
        if (value >= entries[n - 1].Threshold)
        {
            low = high = n - 1;
            t = 0f;
            return;
        }

        int i = 0;
        while (i < n - 1 && value > entries[i + 1].Threshold)
            i++;

        float span = entries[i + 1].Threshold - entries[i].Threshold;
        low = i;
        high = i + 1;
        t = span > 1e-6f ? (value - entries[i].Threshold) / span : 0f;
        if (t >= 1f)
        {
            low = high;
            t = 0f;
        }
    }
}
