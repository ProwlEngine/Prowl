// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A component that records how many times each per-frame callback ran, and the order they ran in.
/// </summary>
public sealed class CounterComponent : Component
{
    public int StartCount;
    public int UpdateCount;
    public int LateUpdateCount;
    public int FixedUpdateCount;

    public readonly List<string> Order = [];

    public override void Start() { StartCount++; Order.Add("Start"); }
    public override void Update() { UpdateCount++; Order.Add("Update"); }
    public override void LateUpdate() { LateUpdateCount++; Order.Add("LateUpdate"); }
    public override void FixedUpdate() { FixedUpdateCount++; Order.Add("FixedUpdate"); }
}

/// <summary>Records which lifecycle callbacks ran, in order.</summary>
public sealed class LifecycleRecorder : Component
{
    public readonly List<string> Calls = [];

    public override void OnEnable() => Calls.Add("OnEnable");
    public override void Start() => Calls.Add("Start");
    public override void Update() => Calls.Add("Update");
    public override void OnDisable() => Calls.Add("OnDisable");
}

/// <summary>A counter that runs even outside play mode.</summary>
[ExecuteAlways]
public sealed class ExecuteAlwaysCounter : Component
{
    public int UpdateCount;
    public override void Update() => UpdateCount++;
}

/// <summary>Shared log + components with distinct [ExecutionOrder] to verify update ordering.</summary>
public static class TickLog
{
    public static readonly List<string> Entries = [];
}

[ExecutionOrder(-50)]
public sealed class EarlyTick : Component { public override void Update() => TickLog.Entries.Add("early"); }

public sealed class MidTick : Component { public override void Update() => TickLog.Entries.Add("mid"); }

[ExecutionOrder(50)]
public sealed class LateTick : Component { public override void Update() => TickLog.Entries.Add("late"); }

/// <summary>
/// Tests for the per-frame update loop driven through <see cref="Resources.Scene.Update"/> and
/// <see cref="Resources.Scene.FixedUpdate"/>: Start/Update/LateUpdate/FixedUpdate dispatch, ordering,
/// and gating by play mode.
/// </summary>
public class UpdateLoopTests : RuntimeTestBase
{
    private (Resources.Scene scene, CounterComponent comp) MakeRunningCounter()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        CounterComponent comp = go.AddComponent<CounterComponent>();
        scene.Add(go);
        return (scene, comp);
    }

    [Fact]
    public void Start_IsCalledOnce_OnFirstUpdate()
    {
        (Scene? scene, CounterComponent? comp) = MakeRunningCounter();
        Assert.Equal(0, comp.StartCount);

        Update(scene);
        Assert.Equal(1, comp.StartCount);

        Update(scene, 5);
        Assert.Equal(1, comp.StartCount); // never called again
    }

    [Fact]
    public void Update_IsCalledEveryFrame()
    {
        (Scene? scene, CounterComponent? comp) = MakeRunningCounter();

        Update(scene, 10);

        Assert.Equal(10, comp.UpdateCount);
        Assert.Equal(10, comp.LateUpdateCount);
    }

    [Fact]
    public void Start_RunsBeforeUpdateAndLateUpdate_OnFirstFrame()
    {
        (Scene? scene, CounterComponent? comp) = MakeRunningCounter();

        Update(scene);

        Assert.Equal(["Start", "Update", "LateUpdate"], comp.Order);
    }

    [Fact]
    public void LateUpdate_RunsAfterUpdate_AcrossComponents()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        CounterComponent a = go.AddComponent<CounterComponent>();
        CounterComponent b = go.AddComponent<CounterComponent>();
        scene.Add(go);

        // Scene runs all Updates, then all LateUpdates, so both Updates precede either LateUpdate.
        scene.Update();

        Assert.Equal("Update", a.Order[1]);
        Assert.Equal("Update", b.Order[1]);
        Assert.Equal("LateUpdate", a.Order[^1]);
        Assert.Equal("LateUpdate", b.Order[^1]);
    }

    [Fact]
    public void FixedUpdate_IsCalledOnPhysicsStep_NotOnUpdate()
    {
        (Scene? scene, CounterComponent? comp) = MakeRunningCounter();

        Update(scene, 3);
        Assert.Equal(0, comp.FixedUpdateCount);

        StepPhysics(scene, 4);
        Assert.Equal(4, comp.FixedUpdateCount);
    }

    [Fact]
    public void DisabledComponent_DoesNotUpdate()
    {
        (Scene? scene, CounterComponent? comp) = MakeRunningCounter();
        comp.Enabled = false;

        Update(scene, 5);

        Assert.Equal(0, comp.UpdateCount);
        Assert.Equal(0, comp.StartCount);
    }

    [Fact]
    public void DisabledGameObject_DoesNotUpdate()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        CounterComponent comp = go.AddComponent<CounterComponent>();
        scene.Add(go);
        go.Enabled = false;

        Update(scene, 5);

        Assert.Equal(0, comp.UpdateCount);
    }

    [Fact]
    public void NotPlaying_GameplayCallbacksDoNotRun()
    {
        // Edit-mode: a plain component's gameplay callbacks are gated off.
        Application.IsPlaying = false;

        (Scene? scene, CounterComponent? comp) = MakeRunningCounter();

        Update(scene, 5);

        Assert.Equal(0, comp.StartCount);
        Assert.Equal(0, comp.UpdateCount);
    }

    [Fact]
    public void Update_RunsComponentsInExecutionOrder_RegardlessOfAddOrder()
    {
        TickLog.Entries.Clear();
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        go.AddComponent<LateTick>();   // added first, runs last
        go.AddComponent<EarlyTick>();
        go.AddComponent<MidTick>();
        scene.Add(go);

        Update(scene);

        Assert.Equal(["early", "mid", "late"], TickLog.Entries);
    }

    // Entering play mode flips IsPlaying while the edit scene is still current until the end of the frame.
    [Fact]
    public void ASceneEnabledOutsidePlay_GetsNoGameplayCallbacks_WhenPlayStartsUnderIt()
    {
        Application.IsPlaying = false;
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        LifecycleRecorder comp = go.AddComponent<LifecycleRecorder>();
        scene.Add(go);

        Application.IsPlaying = true;
        Update(scene, 2);
        scene.Disable();

        Assert.Empty(comp.Calls);
    }

    [Fact]
    public void OnDisable_PairsWithOnEnable_WhenPlayStopsUnderIt()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        LifecycleRecorder comp = go.AddComponent<LifecycleRecorder>();
        scene.Add(go);
        Update(scene);

        Application.IsPlaying = false;
        Update(scene);
        scene.Disable();

        Assert.Equal(["OnEnable", "Start", "Update", "OnDisable"], comp.Calls);
    }

    [Fact]
    public void ExecuteAlways_RunsEvenWhenNotPlaying()
    {
        Application.IsPlaying = false;

        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        ExecuteAlwaysCounter comp = go.AddComponent<ExecuteAlwaysCounter>();
        scene.Add(go);

        Update(scene, 5);

        Assert.Equal(5, comp.UpdateCount);
    }

    [Fact]
    public void Paused_NoGameplayCallbacksRun()
    {
        (Scene scene, CounterComponent comp) = MakeRunningCounter();
        GameObject go = CreateGameObject();
        ExecuteAlwaysCounter always = go.AddComponent<ExecuteAlwaysCounter>();
        scene.Add(go);
        Application.IsPaused = true;

        Update(scene, 3);

        Assert.Equal(0, comp.StartCount);
        Assert.Equal(0, comp.UpdateCount);
        Assert.Equal(0, comp.LateUpdateCount);
        Assert.Equal(0, always.UpdateCount);
    }

    [Fact]
    public void Paused_StepRunsOneFrame()
    {
        (Scene scene, CounterComponent comp) = MakeRunningCounter();
        Application.IsPaused = true;
        Update(scene);

        Application.StepRequested = true;
        Update(scene);
        Application.StepRequested = false;
        Update(scene);

        Assert.Equal(1, comp.StartCount);
        Assert.Equal(1, comp.UpdateCount);
        Assert.Equal(1, comp.LateUpdateCount);
    }

    [Fact]
    public void Unpaused_UpdatesResume()
    {
        (Scene scene, CounterComponent comp) = MakeRunningCounter();
        Application.IsPaused = true;
        Update(scene, 2);

        Application.IsPaused = false;
        Update(scene, 2);

        Assert.Equal(1, comp.StartCount);
        Assert.Equal(2, comp.UpdateCount);
    }
}
