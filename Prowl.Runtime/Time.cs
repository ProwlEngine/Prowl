// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

using Stopwatch = System.Diagnostics.Stopwatch;

namespace Prowl.Runtime;

/// <summary>The time settings every new clock starts with, from the project time settings.</summary>
public sealed class SceneClockSettings
{
    public float TimeScale = 1f;

    public SceneClockSettings Clone() => (SceneClockSettings)MemberwiseClone();
}

/// <summary>
/// A world's clock: its time scale and modifiers, the scaled time it has run, and its own fixed step accumulator. It
/// moves only while its world ticks, so a paused world's time stands still.
/// </summary>
public sealed class SceneClock
{
    private static SceneClockSettings s_defaults = new();

    private readonly List<TimeScaleModifier> _modifiers = [];

    /// <summary>The project time settings. A new clock copies them.</summary>
    public static SceneClockSettings Defaults
    {
        get => s_defaults;
        set => s_defaults = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
    }

    public SceneClock() => TimeScale = s_defaults.TimeScale;

    /// <summary>The base time scale. Time runs at <see cref="EffectiveTimeScale"/>, this times every active modifier.</summary>
    public float TimeScale { get; set; }

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

    /// <summary>The scaled time this world has run, in seconds.</summary>
    public double TimeSinceStartup { get; private set; }

    /// <summary>The scaled time of this world's current frame, zero on a frame it did not tick.</summary>
    public float DeltaTime { get; private set; }

    public float SmoothDeltaTime { get; private set; }

    /// <summary>
    /// How far the frame being drawn sits past the last fixed step, in seconds: what the fixed steps of the frame did not
    /// consume, so always under <see cref="Time.FixedDeltaTime"/> once the frame's Update runs. Physics interpolation
    /// renders at this.
    /// </summary>
    public float FixedAccumulator { get; private set; }

    /// <summary>How far drawing is from the last fixed step toward the next, 0 to 1.</summary>
    public float FixedAlpha => Time.FixedDeltaTime > 0f ? Math.Clamp(FixedAccumulator / Time.FixedDeltaTime, 0f, 1f) : 1f;

    /// <summary>How many modifiers are active.</summary>
    public int ModifierCount => _modifiers.Count;

    /// <summary>
    /// Multiplies the time scale until the returned modifier is removed. Use this rather than setting <see cref="TimeScale"/>
    /// for temporary effects (slow motion, hit stop), since several can overlap and each removes only its own.
    /// </summary>
    public TimeScaleModifier AddTimeScaleModifier(float scale)
    {
        var modifier = new TimeScaleModifier(this, scale);
        _modifiers.Add(modifier);
        return modifier;
    }

    internal void RemoveModifier(TimeScaleModifier modifier) => _modifiers.Remove(modifier);

    /// <summary>Moves the clock on by one frame of unscaled time.</summary>
    internal void Advance(float unscaledDelta)
    {
        float scale = EffectiveTimeScale;
        DeltaTime = unscaledDelta * scale;
        TimeSinceStartup += DeltaTime;
        SmoothDeltaTime = Time.SmoothUnscaledDeltaTime * scale;
    }

    /// <summary>A frame the world did not tick: no time passes.</summary>
    internal void Hold()
    {
        DeltaTime = 0f;
        SmoothDeltaTime = 0f;
    }

    /// <summary>
    /// How many fixed steps this frame's scaled time yields. Scale changes how many steps run, never their size. A
    /// backlog beyond the iteration cap is dropped rather than replayed over the next frames.
    /// </summary>
    internal int TakeFixedSteps()
    {
        FixedAccumulator += DeltaTime;
        int steps = 0;
        while (FixedAccumulator >= Time.FixedDeltaTime && steps < Time.MaxFixedIterations)
        {
            FixedAccumulator -= Time.FixedDeltaTime;
            steps++;
        }
        if (FixedAccumulator >= Time.FixedDeltaTime) FixedAccumulator = 0f;
        return steps;
    }
}

/// <summary>
/// A multiplier on a clock's time scale, owned by whoever added it. Modifiers multiply together and each owner removes
/// only its own, so overlapping slow motion and hit stops can never leave time stuck at another owner's value.
/// </summary>
public sealed class TimeScaleModifier
{
    private readonly SceneClock _clock;

    /// <summary>The multiplier. Can be changed while active, for example to ease slow motion in and out.</summary>
    public float Scale;

    /// <summary>False once removed.</summary>
    public bool IsActive { get; private set; } = true;

    internal TimeScaleModifier(SceneClock clock, float scale)
    {
        _clock = clock;
        Scale = scale;
    }

    /// <summary>Removes the modifier. Removing it again does nothing.</summary>
    public void Remove()
    {
        if (!IsActive) return;
        IsActive = false;
        _clock.RemoveModifier(this);
    }
}

/// <summary>
/// Frame time. The unscaled values and the frame count are the engine's, shared by every world. The scaled values are
/// <see cref="SceneInstance.Active"/>'s <see cref="SceneClock"/>, and read as a stopped clock when no world is bound.
/// </summary>
public static class Time
{
    private static Stopwatch? s_stopwatch;

    public static float UnscaledDeltaTime { get; private set; }
    public static float UnscaledTotalTime { get; private set; }
    public static float SmoothUnscaledDeltaTime { get; private set; }
    public static long FrameCount { get; private set; }

    public static float TimeSmoothFactor = .25f;

    /// <summary>The fixed step, unscaled.</summary>
    public static float FixedDeltaTime = 1.0f / 60.0f;

    /// <summary>
    /// When above zero, every frame advances time by exactly this many seconds instead of the measured time, so a run
    /// plays out the same way every time, for reference captures and recordings.
    /// </summary>
    public static float LockedDeltaTime = 0f;
    public static int MaxFixedIterations = 3;

    /// <summary>Starts a frame: measures the unscaled time since the last one.</summary>
    internal static void AdvanceFrame()
    {
        s_stopwatch ??= Stopwatch.StartNew();
        float dt = LockedDeltaTime > 0f ? LockedDeltaTime : (float)s_stopwatch.Elapsed.TotalSeconds;
        s_stopwatch.Restart();
        AdvanceFrame(dt);
    }

    /// <summary>Starts a frame of a given unscaled length.</summary>
    internal static void AdvanceFrame(float dt)
    {
        FrameCount++;
        UnscaledDeltaTime = dt;
        UnscaledTotalTime += dt;
        SmoothUnscaledDeltaTime += (dt - SmoothUnscaledDeltaTime) * TimeSmoothFactor;
    }

    private static SceneClock? Clock => SceneInstance.ActiveClock;

    public static float DeltaTime => Clock?.DeltaTime ?? 0f;
    public static float TimeSinceStartup => (float)(Clock?.TimeSinceStartup ?? 0d);
    public static float SmoothDeltaTime => Clock?.SmoothDeltaTime ?? 0f;

    /// <inheritdoc cref="SceneClock.FixedAccumulator"/>
    public static float FixedAccumulator => Clock?.FixedAccumulator ?? 0f;

    /// <inheritdoc cref="SceneClock.FixedAlpha"/>
    public static float FixedAlpha => Clock?.FixedAlpha ?? 1f;

    /// <summary>The base time scale of the active world. Time runs at <see cref="EffectiveTimeScale"/>, this times every active modifier.</summary>
    public static float TimeScale
    {
        get => Clock?.TimeScale ?? SceneClock.Defaults.TimeScale;
        set => RequireClock().TimeScale = value;
    }

    /// <summary>The scale the active world's time actually runs at.</summary>
    public static float EffectiveTimeScale => Clock?.EffectiveTimeScale ?? SceneClock.Defaults.TimeScale;

    /// <inheritdoc cref="SceneClock.AddTimeScaleModifier"/>
    public static TimeScaleModifier AddTimeScaleModifier(float scale) => RequireClock().AddTimeScaleModifier(scale);

    private static SceneClock RequireClock()
        => Clock ?? throw new InvalidOperationException("No world is bound, so there is no clock to change. Change a world's Clock, or SceneClock.Defaults for new worlds.");
}
