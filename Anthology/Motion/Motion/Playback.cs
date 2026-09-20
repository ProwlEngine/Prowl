using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>How a clip behaves when its time passes the end.</summary>
public enum PlayMode : byte
{
    /// <summary>Wrap back to the start.</summary>
    Loop,
    /// <summary>Hold the final frame.</summary>
    ClampForever,
    /// <summary>Play once and hold (same clamping as ClampForever; the controller decides when to stop).</summary>
    PlayOnce
}

/// <summary>Helpers for turning a clip time in seconds into a normalized [0,1] sample position.</summary>
public static class ClipPlayback
{
    public static float NormalizeLoop(float timeSeconds, float durationSeconds)
    {
        if (durationSeconds <= 0f)
            return 0f;
        float n = timeSeconds / durationSeconds;
        n -= MathF.Floor(n);
        return n < 0f ? n + 1f : n;
    }

    public static float NormalizeClamp(float timeSeconds, float durationSeconds)
    {
        if (durationSeconds <= 0f)
            return 0f;
        return Math.Clamp(timeSeconds / durationSeconds, 0f, 1f);
    }

    public static float Normalize(float timeSeconds, float durationSeconds, PlayMode mode)
        => mode == PlayMode.Loop ? NormalizeLoop(timeSeconds, durationSeconds) : NormalizeClamp(timeSeconds, durationSeconds);

    /// <summary>
    /// True if a looped clip wrapped between two normalized times: past the end going forward, or
    /// past the start going backward.
    /// </summary>
    public static bool CrossedLoop(float previousNormalized, float currentNormalized, bool backward = false)
        => backward ? currentNormalized > previousNormalized : currentNormalized < previousNormalized;
}

/// <summary>A clip playhead: normalized time plus the number of loops completed.</summary>
public struct ClipCursor
{
    public float Time;
    public int LoopCount;

    /// <summary>
    /// The most loops one step is allowed to report. A stalled frame can hand us a delta worth
    /// thousands of loops, and events and root motion are walked once per loop.
    /// </summary>
    public const int MaxWrapsPerStep = 256;

    /// <summary>
    /// Moves the playhead by <paramref name="deltaSeconds"/> (negative plays backward) and returns the
    /// step as a span, counting every wrap so root motion and events cover whole skipped loops. A step
    /// worth more than <see cref="MaxWrapsPerStep"/> loops still lands on the right time, it just stops
    /// claiming every loop it passed.
    /// </summary>
    public PlaybackSpan Advance(float deltaSeconds, float durationSeconds, bool loop, bool includeStart)
    {
        float from = Time;
        float delta = durationSeconds > 0f ? deltaSeconds / durationSeconds : 0f;
        if (!float.IsFinite(delta))
            delta = 0f;
        bool backward = delta < 0f;

        if (!loop)
        {
            Time = Math.Clamp(from + delta, 0f, 1f);
            return new PlaybackSpan(from, Time, 0, backward, includeStart);
        }

        float raw = from + delta;
        float crossings = raw >= 1f ? MathF.Floor(raw) : (raw < 0f ? MathF.Ceiling(-raw) : 0f);
        int wraps = (int)MathF.Min(crossings, MaxWrapsPerStep);

        // Taking the fraction directly keeps the result in range whatever the magnitude of the step.
        float wrapped = raw - MathF.Floor(raw);
        Time = wrapped >= 1f ? 0f : wrapped;
        LoopCount += backward ? -wraps : wraps;
        return new PlaybackSpan(from, Time, wraps, backward, includeStart);
    }

    /// <summary>Jumps to a normalized time, producing an empty span (nothing between is sampled).</summary>
    public PlaybackSpan Seek(float normalizedTime)
    {
        Time = float.IsFinite(normalizedTime) ? Math.Clamp(normalizedTime, 0f, 1f) : 0f;
        return new PlaybackSpan(Time, Time, 0, false, true);
    }
}

/// <summary>Applies a root-motion delta to a world transform to move a character.</summary>
public static class RootMotionUtil
{
    /// <summary>Moves <paramref name="worldTransform"/> by a character-space root-motion <paramref name="delta"/>.</summary>
    public static Transform3D Apply(Transform3D worldTransform, Transform3D delta) => TransformOps.Combine(worldTransform, delta);
}
