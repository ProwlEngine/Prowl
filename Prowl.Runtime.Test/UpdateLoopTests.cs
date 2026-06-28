// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A component that records how many times each per-frame callback ran, and the order they ran in.
/// </summary>
public sealed class CounterComponent : MonoBehaviour
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

/// <summary>A counter that runs even outside play mode.</summary>
[ExecuteAlways]
public sealed class ExecuteAlwaysCounter : MonoBehaviour
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
public sealed class EarlyTick : MonoBehaviour { public override void Update() => TickLog.Entries.Add("early"); }

public sealed class MidTick : MonoBehaviour { public override void Update() => TickLog.Entries.Add("mid"); }

[ExecutionOrder(50)]
public sealed class LateTick : MonoBehaviour { public override void Update() => TickLog.Entries.Add("late"); }

/// <summary>
/// Tests for the per-frame update loop driven through <see cref="Resources.Scene.Update"/> and
/// <see cref="Resources.Scene.FixedUpdate"/>: Start/Update/LateUpdate/FixedUpdate dispatch, ordering,
/// and gating by play mode.
/// </summary>
public class UpdateLoopTests : RuntimeTestBase
{
    private (Resources.Scene scene, CounterComponent comp) MakeRunningCounter()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var comp = go.AddComponent<CounterComponent>();
        scene.Add(go);
        return (scene, comp);
    }

    [Fact]
    public void Start_IsCalledOnce_OnFirstUpdate()
    {
        var (scene, comp) = MakeRunningCounter();
        Assert.Equal(0, comp.StartCount);

        Update(scene);
        Assert.Equal(1, comp.StartCount);

        Update(scene, 5);
        Assert.Equal(1, comp.StartCount); // never called again
    }

    [Fact]
    public void Update_IsCalledEveryFrame()
    {
        var (scene, comp) = MakeRunningCounter();

        Update(scene, 10);

        Assert.Equal(10, comp.UpdateCount);
        Assert.Equal(10, comp.LateUpdateCount);
    }

    [Fact]
    public void Start_RunsBeforeUpdateAndLateUpdate_OnFirstFrame()
    {
        var (scene, comp) = MakeRunningCounter();

        Update(scene);

        Assert.Equal(["Start", "Update", "LateUpdate"], comp.Order);
    }

    [Fact]
    public void LateUpdate_RunsAfterUpdate_AcrossComponents()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var a = go.AddComponent<CounterComponent>();
        var b = go.AddComponent<CounterComponent>();
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
        var (scene, comp) = MakeRunningCounter();

        Update(scene, 3);
        Assert.Equal(0, comp.FixedUpdateCount);

        StepPhysics(scene, 4);
        Assert.Equal(4, comp.FixedUpdateCount);
    }

    [Fact]
    public void DisabledComponent_DoesNotUpdate()
    {
        var (scene, comp) = MakeRunningCounter();
        comp.Enabled = false;

        Update(scene, 5);

        Assert.Equal(0, comp.UpdateCount);
        Assert.Equal(0, comp.StartCount);
    }

    [Fact]
    public void DisabledGameObject_DoesNotUpdate()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var comp = go.AddComponent<CounterComponent>();
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

        var (scene, comp) = MakeRunningCounter();

        Update(scene, 5);

        Assert.Equal(0, comp.StartCount);
        Assert.Equal(0, comp.UpdateCount);
    }

    [Fact]
    public void Update_RunsComponentsInExecutionOrder_RegardlessOfAddOrder()
    {
        TickLog.Entries.Clear();
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        go.AddComponent<LateTick>();   // added first, runs last
        go.AddComponent<EarlyTick>();
        go.AddComponent<MidTick>();
        scene.Add(go);

        Update(scene);

        Assert.Equal(["early", "mid", "late"], TickLog.Entries);
    }

    [Fact]
    public void ExecuteAlways_RunsEvenWhenNotPlaying()
    {
        Application.IsPlaying = false;

        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var comp = go.AddComponent<ExecuteAlwaysCounter>();
        scene.Add(go);

        Update(scene, 5);

        Assert.Equal(5, comp.UpdateCount);
    }
}
