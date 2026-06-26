// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Test;

/// <summary>
/// Base class for tests that spin up a live instance of the runtime (scenes, GameObjects,
/// components and physics) to verify behaviour like MonoBehaviour lifecycles and the simulation.
///
/// It puts the runtime into "play mode" (<see cref="Application.IsPlaying"/>) so gameplay
/// callbacks (OnEnable/Start/Update/FixedUpdate/...) actually fire, gives a deterministic
/// delta-time, tracks every Scene/GameObject it creates and tears them down afterwards, and
/// exposes <see cref="Tick"/> helpers to advance the simulation by hand.
///
/// Tests in this project run without parallelization (see TestAssemblyConfig) because the runtime
/// relies on global static state (<see cref="Application"/>, <see cref="Time"/>).
/// </summary>
public abstract class RuntimeTestBase : IDisposable
{
    private readonly bool _prevIsPlaying;
    private readonly bool _prevIsEditor;
    private readonly bool _prevIsPaused;
    private readonly TimeData _time;

    private readonly List<Scene> _scenes = [];
    private readonly List<GameObject> _gameObjects = [];

    protected RuntimeTestBase()
    {
        // Capture and override global state so each test starts from a known, play-mode setup.
        _prevIsPlaying = Application.IsPlaying;
        _prevIsEditor = Application.IsEditor;
        _prevIsPaused = Application.IsPaused;

        Application.IsPlaying = true;
        Application.IsEditor = false;
        Application.IsPaused = false;

        // Push a deterministic time source so Time.DeltaTime is stable instead of being driven
        // by a wall-clock stopwatch. Physics uses the static Time.FixedDeltaTime (default 1/60).
        _time = new TimeData { DeltaTime = Time.FixedDeltaTime };
        Time.TimeStack.Push(_time);
    }

    /// <summary> The fixed timestep used by physics, mirrored from <see cref="Time.FixedDeltaTime"/>. </summary>
    protected static float FixedDeltaTime => Time.FixedDeltaTime;

    /// <summary>
    /// Creates a tracked scene. Pass <paramref name="enable"/> to immediately enable it (play mode),
    /// otherwise it starts disabled so component OnEnable is deferred until <see cref="Scene.Enable"/>.
    /// </summary>
    protected Scene CreateScene(bool enable = false)
    {
        var scene = new Scene();
        _scenes.Add(scene);
        if (enable)
            scene.Enable();
        return scene;
    }

    /// <summary> Creates a tracked GameObject that will be disposed when the test ends. </summary>
    protected GameObject CreateGameObject(string name = "TestObject")
    {
        var go = new GameObject(name);
        _gameObjects.Add(go);
        return go;
    }

    /// <summary>
    /// Advances one full frame: steps physics (<see cref="Scene.FixedUpdate"/>) then runs the
    /// per-frame update (<see cref="Scene.Update"/>). This mirrors the order the real game loop uses
    /// so components that read back physics state in Update (e.g. Rigidbody3D) stay in sync.
    /// </summary>
    protected void Tick(Scene scene, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            scene.FixedUpdate();
            scene.Update();
        }
    }

    /// <summary> Runs <see cref="Scene.Update"/> the given number of times (no physics step). </summary>
    protected void Update(Scene scene, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
            scene.Update();
    }

    /// <summary> Steps physics via <see cref="Scene.FixedUpdate"/> the given number of times. </summary>
    protected void StepPhysics(Scene scene, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
            scene.FixedUpdate();
    }

    public virtual void Dispose()
    {
        foreach (var scene in _scenes)
        {
            if (scene.IsDisposed) continue;
            if (scene.IsActive)
                scene.Disable();
            scene.Dispose();
        }
        _scenes.Clear();

        foreach (var go in _gameObjects)
        {
            if (!go.IsDisposed)
                go.Dispose();
        }
        _gameObjects.Clear();

        // Restore global state for any test that doesn't go through this base.
        if (Time.TimeStack.Count > 0 && Time.TimeStack.Peek() == _time)
            Time.TimeStack.Pop();

        Application.IsPlaying = _prevIsPlaying;
        Application.IsEditor = _prevIsEditor;
        Application.IsPaused = _prevIsPaused;

        GC.SuppressFinalize(this);
    }
}
