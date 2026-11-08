// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Runs a supplied action during its Update, for reentrancy tests.</summary>
public sealed class UpdateActionComponent : Component
{
    public Action? Action;
    public int Updates;
    public override void Update() { Updates++; Action?.Invoke(); }
}

/// <summary>Minimal Update-only counter (no other callbacks) for clean assertions.</summary>
public sealed class PlainUpdateCounter : Component
{
    public int Updates;
    public override void Update() => Updates++;
}

/// <summary>Appends a tag on Update, for ordering tests.</summary>
public sealed class TagTick : Component
{
    public string Mark = "";
    public override void Update() => TickLog.Entries.Add(Mark);
}

/// <summary>
/// Stress and edge tests for the per-scene component registry, focused on the cases most likely to
/// break it: reentrant structural changes during a tick (enable/disable/destroy others and self),
/// register/unregister churn, ordering stability, and scale.
/// </summary>
public class RegistryStressTests : RuntimeTestBase
{
    private (SceneInstance scene, GameObject go) NewSceneGo()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        return (scene, go);
    }

    [Fact]
    public void EnablingComponentDuringUpdate_TicksNextFrameNotThisFrame()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        PlainUpdateCounter target = go.AddComponent<PlainUpdateCounter>();
        target.Enabled = false; // not registered
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>();
        driver.Action = () => target.Enabled = true;
        scene.Add(go);

        Update(scene); // driver enables target; snapshot for this frame is already fixed
        Assert.Equal(0, target.Updates);

        Update(scene);
        Assert.Equal(1, target.Updates);
    }

    [Fact]
    public void DisablingLaterComponentDuringUpdate_SkipsItThisFrame()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>(); // registered first -> runs first
        PlainUpdateCounter target = go.AddComponent<PlainUpdateCounter>();
        driver.Action = () => target.Enabled = false;
        scene.Add(go);

        Update(scene);

        Assert.Equal(0, target.Updates); // disabled before its turn in the same frame
    }

    [Fact]
    public void DestroyingComponentDuringUpdate_SkippedWithoutCrash()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>();
        PlainUpdateCounter target = go.AddComponent<PlainUpdateCounter>();
        driver.Action = () => go.RemoveComponent(target);
        scene.Add(go);

        Update(scene); // must not throw

        Assert.Equal(1, driver.Updates);
        Assert.True(target.IsDisposed);
        Assert.Equal(0, target.Updates);
    }

    [Fact]
    public void ComponentDestroyingItselfDuringUpdate_NoCrash()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        UpdateActionComponent? driver = null;
        driver = go.AddComponent<UpdateActionComponent>();
        driver.Action = () => go.RemoveComponent(driver!);
        scene.Add(go);

        Update(scene); // must not throw / stack-overflow
        Assert.Equal(1, driver.Updates);

        Update(scene); // already gone; must not tick again
        Assert.Equal(1, driver.Updates);
    }

    [Fact]
    public void AddingComponentDuringUpdate_TicksNextFrame()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>();
        PlainUpdateCounter? added = null;
        driver.Action = () => added ??= go.AddComponent<PlainUpdateCounter>();
        scene.Add(go);

        Update(scene);
        Assert.NotNull(added);
        Assert.Equal(0, added!.Updates); // not in this frame's snapshot

        Update(scene);
        Assert.Equal(1, added.Updates);
    }

    [Fact]
    public void EnableDisableEnable_DoesNotDoubleRegister()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        PlainUpdateCounter c = go.AddComponent<PlainUpdateCounter>();
        scene.Add(go);

        c.Enabled = false;
        c.Enabled = true;

        Update(scene);

        Assert.Equal(1, c.Updates); // exactly once, not twice
    }

    [Fact]
    public void EqualExecutionOrder_TicksInRegistrationOrder()
    {
        TickLog.Entries.Clear();
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        go.AddComponent<TagTick>().Mark = "a";
        go.AddComponent<TagTick>().Mark = "b";
        go.AddComponent<TagTick>().Mark = "c";
        scene.Add(go);

        Update(scene);

        Assert.Equal(["a", "b", "c"], TickLog.Entries);
    }

    [Fact]
    public void DisablingGameObjectMidUpdate_SkipsItsOtherComponents()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>();
        GameObject other = CreateGameObject("Other");
        PlainUpdateCounter otherCounter = other.AddComponent<PlainUpdateCounter>();
        driver.Action = () => other.Enabled = false;

        // 'go' is registered first so the driver runs before 'otherCounter' in the snapshot - it
        // disables the other GameObject before that GameObject's components would have ticked.
        scene.Add(go);
        scene.Add(other);

        Update(scene);

        Assert.Equal(0, otherCounter.Updates); // disabled mid-frame, before its turn this frame
    }

    [Fact]
    public void DisabledScene_DoesNotTick()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        PlainUpdateCounter c = go.AddComponent<PlainUpdateCounter>();
        scene.Add(go);

        scene.Disable(); // unregisters all
        Update(scene);

        Assert.Equal(0, c.Updates);
    }

    [Fact]
    public void ManyComponents_AllTickOncePerUpdate()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        var counters = new List<PlainUpdateCounter>();
        for (int i = 0; i < 200; i++)
            counters.Add(go.AddComponent<PlainUpdateCounter>());
        scene.Add(go);

        Update(scene);

        Assert.All(counters, c => Assert.Equal(1, c.Updates));
    }

    [Fact]
    public void RemovingOneComponentMidUpdate_OthersStillTick()
    {
        (SceneInstance? scene, GameObject? go) = NewSceneGo();
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>(); // c0 - runs first
        PlainUpdateCounter victim = go.AddComponent<PlainUpdateCounter>();      // c1 - removed
        PlainUpdateCounter survivor = go.AddComponent<PlainUpdateCounter>();    // c2 - must still tick
        driver.Action = () => go.RemoveComponent(victim);
        scene.Add(go);

        Update(scene);

        Assert.Equal(0, victim.Updates);
        Assert.Equal(1, survivor.Updates);
    }
}
