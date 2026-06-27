// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Runs a supplied action during its Update, for reentrancy tests.</summary>
public sealed class UpdateActionComponent : MonoBehaviour
{
    public Action? Action;
    public int Updates;
    public override void Update() { Updates++; Action?.Invoke(); }
}

/// <summary>Minimal Update-only counter (no other callbacks) for clean assertions.</summary>
public sealed class PlainUpdateCounter : MonoBehaviour
{
    public int Updates;
    public override void Update() => Updates++;
}

/// <summary>Appends a tag on Update, for ordering tests.</summary>
public sealed class TagTick : MonoBehaviour
{
    public string Tag = "";
    public override void Update() => TickLog.Entries.Add(Tag);
}

/// <summary>
/// Stress and edge tests for the per-scene component registry, focused on the cases most likely to
/// break it: reentrant structural changes during a tick (enable/disable/destroy others and self),
/// register/unregister churn, ordering stability, and scale.
/// </summary>
public class RegistryStressTests : RuntimeTestBase
{
    private (Scene scene, GameObject go) NewSceneGo()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        return (scene, go);
    }

    [Fact]
    public void EnablingComponentDuringUpdate_TicksNextFrameNotThisFrame()
    {
        var (scene, go) = NewSceneGo();
        var target = go.AddComponent<PlainUpdateCounter>();
        target.Enabled = false; // not registered
        var driver = go.AddComponent<UpdateActionComponent>();
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
        var (scene, go) = NewSceneGo();
        var driver = go.AddComponent<UpdateActionComponent>(); // registered first -> runs first
        var target = go.AddComponent<PlainUpdateCounter>();
        driver.Action = () => target.Enabled = false;
        scene.Add(go);

        Update(scene);

        Assert.Equal(0, target.Updates); // disabled before its turn in the same frame
    }

    [Fact]
    public void DestroyingComponentDuringUpdate_SkippedWithoutCrash()
    {
        var (scene, go) = NewSceneGo();
        var driver = go.AddComponent<UpdateActionComponent>();
        var target = go.AddComponent<PlainUpdateCounter>();
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
        var (scene, go) = NewSceneGo();
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
        var (scene, go) = NewSceneGo();
        var driver = go.AddComponent<UpdateActionComponent>();
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
        var (scene, go) = NewSceneGo();
        var c = go.AddComponent<PlainUpdateCounter>();
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
        var (scene, go) = NewSceneGo();
        go.AddComponent<TagTick>().Tag = "a";
        go.AddComponent<TagTick>().Tag = "b";
        go.AddComponent<TagTick>().Tag = "c";
        scene.Add(go);

        Update(scene);

        Assert.Equal(["a", "b", "c"], TickLog.Entries);
    }

    [Fact]
    public void DisablingGameObjectMidUpdate_SkipsItsOtherComponents()
    {
        var (scene, go) = NewSceneGo();
        var driver = go.AddComponent<UpdateActionComponent>();
        var other = CreateGameObject("Other");
        var otherCounter = other.AddComponent<PlainUpdateCounter>();
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
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var c = go.AddComponent<PlainUpdateCounter>();
        scene.Add(go);

        scene.Disable(); // unregisters all
        Update(scene);

        Assert.Equal(0, c.Updates);
    }

    [Fact]
    public void ManyComponents_AllTickOncePerUpdate()
    {
        var (scene, go) = NewSceneGo();
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
        var (scene, go) = NewSceneGo();
        var driver = go.AddComponent<UpdateActionComponent>(); // c0 - runs first
        var victim = go.AddComponent<PlainUpdateCounter>();      // c1 - removed
        var survivor = go.AddComponent<PlainUpdateCounter>();    // c2 - must still tick
        driver.Action = () => go.RemoveComponent(victim);
        scene.Add(go);

        Update(scene);

        Assert.Equal(0, victim.Updates);
        Assert.Equal(1, survivor.Updates);
    }
}
