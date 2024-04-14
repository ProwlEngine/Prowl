namespace Prowl.Runtime;

public static class Time {

    public static double unscaledDeltaTime { get; private set; }
    public static double unscaledTotalTime { get; private set; }

    public static double deltaTime { get; private set; }
    public static float deltaTimeF => (float)deltaTime;
    public static double fixedDeltaTime { get; set; } = 1.0 / PhysicsSetting.Instance.TargetFrameRate;
    public static double time { get; private set; }

    public static double smoothUnscaledDeltaTime { get; private set; }
    public static double smoothDeltaTime { get; private set; }

    public static long frameCount { get; private set; }

    public static double timeScale { get; set; } = 1f;
    public static float timeScaleF => (float)timeScale;
    public static double timeSmoothFactor { get; set; } = .25f;



    public static void Update(double dt)
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
