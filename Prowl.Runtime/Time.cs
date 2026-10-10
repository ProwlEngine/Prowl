// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Stopwatch = System.Diagnostics.Stopwatch;

namespace Prowl.Runtime;

public class TimeData
{
    public float UnscaledDeltaTime;
    public float UnscaledTotalTime;
    public float DeltaTime;
    public float Time;
    public float SmoothUnscaledDeltaTime;
    public float SmoothDeltaTime;

    private Stopwatch _stopwatch;

    public TimeData() { }

    public long FrameCount;

    /// <summary>The base time scale. Time runs at <see cref="EffectiveTimeScale"/>, this times every active modifier.</summary>
    public float TimeScale = 1f;
    public float TimeSmoothFactor = .25f;

    private readonly List<TimeScaleModifier> _modifiers = new();

    /// <summary>The scale time actually runs at: <see cref="TimeScale"/> times every active <see cref="TimeScaleModifier"/>.</summary>
    public float EffectiveTimeScale
    {
        get
        {
            float scale = TimeScale;
            foreach (TimeScaleModifier modifier in _modifiers) scale *= modifier.Scale;
            return scale;
        }
    }

    /// <summary>How many modifiers are active.</summary>
    public int ModifierCount => _modifiers.Count;

    /// <summary>Adds a modifier that multiplies the time scale until its owner removes it.</summary>
    public TimeScaleModifier AddModifier(float scale)
    {
        var modifier = new TimeScaleModifier(this, scale);
        _modifiers.Add(modifier);
        return modifier;
    }

    internal void RemoveModifier(TimeScaleModifier modifier) => _modifiers.Remove(modifier);

    public void Update()
    {
        _stopwatch ??= Stopwatch.StartNew();

        float dt = Prowl.Runtime.Time.LockedDeltaTime > 0f ? Prowl.Runtime.Time.LockedDeltaTime : (float)_stopwatch.Elapsed.TotalMilliseconds / 1000.0f;

        FrameCount++;

        UnscaledDeltaTime = dt;
        UnscaledTotalTime += UnscaledDeltaTime;

        float scale = EffectiveTimeScale;
        DeltaTime = dt * scale;
        Time += DeltaTime;

        SmoothUnscaledDeltaTime += (dt - SmoothUnscaledDeltaTime) * TimeSmoothFactor;
        SmoothDeltaTime = SmoothUnscaledDeltaTime * scale;

        _stopwatch.Restart();
    }
}

/// <summary>
/// A multiplier on the time scale, owned by whoever added it. Modifiers multiply together and each owner removes
/// only its own, so overlapping slow motion and hit stops can never leave time stuck at another owner's value.
/// </summary>
public sealed class TimeScaleModifier
{
    private readonly TimeData _time;

    /// <summary>The multiplier. Can be changed while active, for example to ease slow motion in and out.</summary>
    public float Scale;

    /// <summary>False once removed.</summary>
    public bool IsActive { get; private set; } = true;

    internal TimeScaleModifier(TimeData time, float scale)
    {
        _time = time;
        Scale = scale;
    }

    /// <summary>Removes the modifier. Removing it again does nothing.</summary>
    public void Remove()
    {
        if (!IsActive) return;
        IsActive = false;
        _time.RemoveModifier(this);
    }
}

public static class Time
{
    private static readonly TimeData s_defaultTime = new();

    public static Stack<TimeData> TimeStack { get; } = new();

    public static TimeData CurrentTime => TimeStack.TryPeek(out TimeData? time) && time != null ? time : s_defaultTime;

    public static float UnscaledDeltaTime => CurrentTime.UnscaledDeltaTime;
    public static float UnscaledTotalTime => CurrentTime.UnscaledTotalTime;

    public static float DeltaTime => CurrentTime.DeltaTime;
    public static float FixedDeltaTime = 1.0f / 60.0f;

    /// <summary>
    /// When above zero, every frame advances time by exactly this many seconds instead of the measured time, so a run
    /// plays out the same way every time, for reference captures and recordings.
    /// </summary>
    public static float LockedDeltaTime = 0f;
    public static int MaxFixedIterations = 3;

    /// <summary>
    /// How far the frame being drawn sits past the last fixed step, in seconds - the fixed loop
    /// leaves this holding whatever part of the frame its steps did not consume, so it is always
    /// under <see cref="FixedDeltaTime"/> by the time the scene's Update runs.
    ///
    /// This is the alpha physics interpolation has to render at. Counting time since the last step
    /// with a per-frame accumulator instead does not work: the step that resets such a counter
    /// happens partway through a frame, and the whole of that frame's delta then gets added on top
    /// of time the step already consumed, which reads back a pose the body has not reached yet.
    /// Owned by the game loop - read it, do not write it.
    /// </summary>
    public static float FixedAccumulator;

    /// <summary>How far drawing is from the last fixed step toward the next, 0 to 1.</summary>
    public static float FixedAlpha => FixedDeltaTime > 0f ? Math.Clamp(FixedAccumulator / FixedDeltaTime, 0f, 1f) : 1f;
    public static float TimeSinceStartup => CurrentTime.Time;

    public static float SmoothUnscaledDeltaTime => CurrentTime.SmoothUnscaledDeltaTime;
    public static float SmoothDeltaTime => CurrentTime.SmoothDeltaTime;

    public static long FrameCount => CurrentTime.FrameCount;

    /// <summary>The base time scale. Time runs at <see cref="EffectiveTimeScale"/>, this times every active modifier.</summary>
    public static float TimeScale
    {
        get => CurrentTime.TimeScale;
        set => CurrentTime.TimeScale = value;
    }

    /// <summary>The scale time actually runs at: <see cref="TimeScale"/> times every active <see cref="TimeScaleModifier"/>.</summary>
    public static float EffectiveTimeScale => CurrentTime.EffectiveTimeScale;

    /// <summary>
    /// Multiplies the time scale until the returned modifier is removed. Use this rather than setting <see cref="TimeScale"/>
    /// for temporary effects (slow motion, hit stop), since several can overlap and each removes only its own.
    /// Modifiers belong to the current time context, so play mode's end with it.
    /// </summary>
    public static TimeScaleModifier AddTimeScaleModifier(float scale) => CurrentTime.AddModifier(scale);

    public static float TimeSmoothFactor
    {
        get => CurrentTime.TimeSmoothFactor;
        set => CurrentTime.TimeSmoothFactor = value;
    }
}
