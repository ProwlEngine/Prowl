// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

    public float TimeScale = 1f;
    public float TimeSmoothFactor = .25f;

    public void Update()
    {
        _stopwatch ??= Stopwatch.StartNew();

        float dt = (float)_stopwatch.Elapsed.TotalMilliseconds / 1000.0f;

        FrameCount++;

        UnscaledDeltaTime = dt;
        UnscaledTotalTime += UnscaledDeltaTime;

        DeltaTime = dt * TimeScale;
        Time += DeltaTime;

        SmoothUnscaledDeltaTime += (dt - SmoothUnscaledDeltaTime) * TimeSmoothFactor;
        SmoothDeltaTime = SmoothUnscaledDeltaTime * TimeScale;

        _stopwatch.Restart();
    }
}

public static class Time
{
    private static readonly TimeData s_defaultTime = new();

    public static Stack<TimeData> TimeStack { get; } = new();

    public static TimeData CurrentTime => TimeStack.Count > 0 ? TimeStack.Peek() : s_defaultTime;

    public static float UnscaledDeltaTime => CurrentTime.UnscaledDeltaTime;
    public static float UnscaledTotalTime => CurrentTime.UnscaledTotalTime;

    public static float DeltaTime => CurrentTime.DeltaTime;
    public static float FixedDeltaTime = 1.0f / 60.0f;
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
    public static float TimeSinceStartup => CurrentTime.Time;

    public static float SmoothUnscaledDeltaTime => CurrentTime.SmoothUnscaledDeltaTime;
    public static float SmoothDeltaTime => CurrentTime.SmoothDeltaTime;

    public static long FrameCount => CurrentTime.FrameCount;

    public static float TimeScale
    {
        get => CurrentTime.TimeScale;
        set => CurrentTime.TimeScale = value;
    }

    public static float TimeSmoothFactor
    {
        get => CurrentTime.TimeSmoothFactor;
        set => CurrentTime.TimeSmoothFactor = value;
    }
}
