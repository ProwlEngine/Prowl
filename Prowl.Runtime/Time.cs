using System.Collections.Generic;

namespace Prowl.Runtime;

public class TimeData
{
    public double unscaledDeltaTime;
    public double unscaledTotalTime;
    public double deltaTime;
    public double time;
    public double smoothUnscaledDeltaTime;
    public double smoothDeltaTime;
    public long frameCount;

    public double timeScale = 1f;
    public double timeSmoothFactor = .25f;

    public TimeData() { }

    public void Update(double dt)
    {
        frameCount++;

        unscaledDeltaTime = dt;
        unscaledTotalTime += unscaledDeltaTime;

        deltaTime = dt * timeScale;
        time += deltaTime;

        smoothUnscaledDeltaTime = smoothUnscaledDeltaTime + (dt - smoothUnscaledDeltaTime) * timeSmoothFactor;
        smoothDeltaTime = smoothUnscaledDeltaTime * dt;
    }
}

public static class Time {

    public static Stack<TimeData> TimeStack { get; } = new();

    public static TimeData CurrentTime => TimeStack.Peek();

    public static double unscaledDeltaTime => CurrentTime.unscaledDeltaTime;
    public static double unscaledTotalTime => CurrentTime.unscaledTotalTime;

    public static double deltaTime => CurrentTime.deltaTime;
    public static float deltaTimeF => (float)deltaTime;
    public static double fixedDeltaTime => 1.0 / (PhysicsSetting.Instance?.TargetFrameRate ?? 50);
    public static double time => CurrentTime.time;

    public static double smoothUnscaledDeltaTime => CurrentTime.smoothUnscaledDeltaTime;
    public static double smoothDeltaTime => CurrentTime.smoothDeltaTime;

    public static long frameCount => CurrentTime.frameCount;

    public static double timeScale {
        get => CurrentTime.timeScale;
        set => CurrentTime.timeScale = value;
    }

    public static float timeScaleF {
        get => (float)timeScale;
        set => timeScale = value;
    }

    public static double timeSmoothFactor {
        get => CurrentTime.timeSmoothFactor;
        set => CurrentTime.timeSmoothFactor = value;
    }
}
