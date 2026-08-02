// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Records the physics callbacks it receives, so fan-out can be asserted without a real contact.</summary>
public sealed class PhysicsListener : MonoBehaviour
{
    public string Mark = "";
    public int Begins, Ends, Enters, Stays, Exits;

    public override void OnCollisionBegin(Rigidbody3D other, Rigidbody3D.ContactInfo contact)
    {
        Begins++;
        PhysicsLog.Entries.Add(Mark);
    }

    public override void OnCollisionEnd(Rigidbody3D other) => Ends++;
    public override void OnTriggerEnter(Rigidbody3D other) => Enters++;
    public override void OnTriggerStay(Rigidbody3D other) => Stays++;
    public override void OnTriggerExit(Rigidbody3D other) => Exits++;
}

/// <summary>Overrides nothing the per-frame loops dispatch, so it is never registered for ticking.</summary>
public sealed class PhysicsOnlyListener : MonoBehaviour
{
    public int Begins;
    public override void OnCollisionBegin(Rigidbody3D other, Rigidbody3D.ContactInfo contact) => Begins++;
}

/// <summary>Mutates its GameObject from inside a physics callback, to exercise the snapshot path.</summary>
public sealed class SelfRemovingListener : MonoBehaviour
{
    public int Begins;

    public override void OnCollisionBegin(Rigidbody3D other, Rigidbody3D.ContactInfo contact)
    {
        Begins++;
        GameObject.RemoveComponent(this);
    }
}

public static class PhysicsLog
{
    public static readonly List<string> Entries = new();
}

/// <summary>
/// The parts of <see cref="SceneDispatcher"/> that the merge changed: constant time unregistration, which
/// moves entries around in the registration array, and physics fan-out, which no longer resolves handlers
/// through a per-type lookup and no longer copies anything for the common recipient counts.
/// </summary>
public class SceneDispatcherTests : RuntimeTestBase
{
    private static Rigidbody3D.ContactInfo NoContact => default;

    private (Scene scene, GameObject go) NewSceneGo()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        return (scene, go);
    }

    // Unregistering moves the array's tail into the hole, so ordering has to come from the registration
    // sequence rather than from where an entry happens to sit.
    [Fact]
    public void Unregistering_DoesNotDisturbTheOrderOfWhatRemains()
    {
        TickLog.Entries.Clear();

        var (scene, go) = NewSceneGo();
        var a = go.AddComponent<TagTick>(); a.Mark = "a";
        var b = go.AddComponent<TagTick>(); b.Mark = "b";
        var c = go.AddComponent<TagTick>(); c.Mark = "c";
        scene.Add(go);

        b.Enabled = false; // c gets swapped into b's slot
        Update(scene);

        Assert.Equal(new[] { "a", "c" }, TickLog.Entries);
    }

    [Fact]
    public void ReEnabledComponent_TicksLastAmongEqualExecutionOrder()
    {
        TickLog.Entries.Clear();

        var (scene, go) = NewSceneGo();
        var a = go.AddComponent<TagTick>(); a.Mark = "a";
        var b = go.AddComponent<TagTick>(); b.Mark = "b";
        var c = go.AddComponent<TagTick>(); c.Mark = "c";
        scene.Add(go);

        a.Enabled = false;
        a.Enabled = true; // re-registered, so it is now the newest

        Update(scene);

        Assert.Equal(new[] { "b", "c", "a" }, TickLog.Entries);
    }

    [Fact]
    public void ChurningManyComponents_KeepsEveryoneTickingExactlyOnce()
    {
        var (scene, go) = NewSceneGo();
        var all = new List<PlainUpdateCounter>();

        for (int i = 0; i < 200; i++)
            all.Add(go.AddComponent<PlainUpdateCounter>());

        scene.Add(go);

        // Disable every third, then bring half of those back, so the array is thoroughly shuffled.
        for (int i = 0; i < all.Count; i += 3) all[i].Enabled = false;
        for (int i = 0; i < all.Count; i += 6) all[i].Enabled = true;

        Update(scene);

        foreach (var c in all)
            Assert.Equal(c.EnabledInHierarchy ? 1 : 0, c.Updates);
    }

    [Fact]
    public void Start_RunsOnceEvenThoughStartedComponentsLeaveLazily()
    {
        var (scene, go) = NewSceneGo();
        var c = go.AddComponent<StartCounter>();
        scene.Add(go);

        Update(scene);
        Update(scene);
        Update(scene);

        Assert.Equal(1, c.Starts);
    }

    // ---- physics fan-out -----------------------------------------------------------------------------

    [Fact]
    public void PhysicsEvent_WithNoHandlers_DoesNothing()
    {
        var (scene, go) = NewSceneGo();
        go.AddComponent<PlainUpdateCounter>(); // overrides nothing physics dispatches
        scene.Add(go);

        SceneDispatcher.CollisionBegin(go, null!, NoContact); // must not throw
    }

    [Fact]
    public void PhysicsEvent_WithOneHandler_Fires()
    {
        var (scene, go) = NewSceneGo();
        var listener = go.AddComponent<PhysicsListener>();
        scene.Add(go);

        SceneDispatcher.CollisionBegin(go, null!, NoContact);
        SceneDispatcher.CollisionEnd(go, null!);
        SceneDispatcher.TriggerEnter(go, null!);
        SceneDispatcher.TriggerStay(go, null!);
        SceneDispatcher.TriggerExit(go, null!);

        Assert.Equal(1, listener.Begins);
        Assert.Equal(1, listener.Ends);
        Assert.Equal(1, listener.Enters);
        Assert.Equal(1, listener.Stays);
        Assert.Equal(1, listener.Exits);
    }

    [Fact]
    public void PhysicsEvent_WithSeveralHandlers_FiresAllInComponentOrder()
    {
        PhysicsLog.Entries.Clear();

        var (scene, go) = NewSceneGo();
        var a = go.AddComponent<PhysicsListener>(); a.Mark = "a";
        var b = go.AddComponent<PhysicsListener>(); b.Mark = "b";
        var c = go.AddComponent<PhysicsListener>(); c.Mark = "c";
        scene.Add(go);

        SceneDispatcher.CollisionBegin(go, null!, NoContact);

        Assert.Equal(new[] { "a", "b", "c" }, PhysicsLog.Entries);
        Assert.Equal(1, a.Begins);
        Assert.Equal(1, b.Begins);
        Assert.Equal(1, c.Begins);
    }

    [Fact]
    public void PhysicsEvent_SkipsDisabledHandlers()
    {
        var (scene, go) = NewSceneGo();
        var enabled = go.AddComponent<PhysicsListener>();
        var disabled = go.AddComponent<PhysicsListener>();
        scene.Add(go);

        disabled.Enabled = false;
        SceneDispatcher.CollisionBegin(go, null!, NoContact);

        Assert.Equal(1, enabled.Begins);
        Assert.Equal(0, disabled.Begins);
    }

    // A component with no per-frame callback is never registered for ticking, but its physics callbacks
    // still have to arrive: they are dispatched from the component's own mask, not from the registry.
    [Fact]
    public void PhysicsOnlyComponent_StillReceivesEvents()
    {
        var (scene, go) = NewSceneGo();
        var listener = go.AddComponent<PhysicsOnlyListener>();
        scene.Add(go);

        SceneDispatcher.CollisionBegin(go, null!, NoContact);

        Assert.Equal(1, listener.Begins);
    }

    [Fact]
    public void PhysicsHandler_RemovingItselfMidDispatch_DoesNotDisturbTheOthers()
    {
        var (scene, go) = NewSceneGo();
        var remover = go.AddComponent<SelfRemovingListener>();
        var survivor = go.AddComponent<PhysicsListener>();
        scene.Add(go);

        SceneDispatcher.CollisionBegin(go, null!, NoContact);

        Assert.Equal(1, remover.Begins);
        Assert.Equal(1, survivor.Begins); // dispatched off a snapshot, so the removal cannot skip it
    }

    [Fact]
    public void PhysicsEvent_OnNullGameObject_IsIgnored()
    {
        SceneDispatcher.CollisionBegin(null!, null!, NoContact);
        SceneDispatcher.TriggerExit(null!, null!);
    }
}

/// <summary>Counts Start calls, to prove a started component leaves the channel for good.</summary>
public sealed class StartCounter : MonoBehaviour
{
    public int Starts;
    public override void Start() => Starts++;
}
