// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for Scene object management (Add/Remove/Clear/Flush, collection views, Find*) and the
/// built-in static scene manager (Load/Unload/Current). Lifecycle ordering is covered separately
/// by <see cref="LifecycleTests"/>.
/// </summary>
public class SceneManagementTests : RuntimeTestBase
{
    // Disposing a scene must actually dispose its GameObjects (roots and children).
    [Fact]
    public void Dispose_MarksGameObjectsDisposed()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("root");
        GameObject child = CreateGameObject("child");
        child.SetParent(root);
        scene.Add(root);

        scene.Dispose();

        Assert.True(root.IsDisposed, "Root GameObject should be disposed.");
        Assert.True(child.IsDisposed, "Child GameObject should be disposed.");
    }

    // ---- Add / Remove ----

    [Fact]
    public void Add_RegistersObject_AndSetsScene()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();

        scene.Add(go);

        Assert.Same(scene, go.Scene);
        Assert.Equal(1, scene.Count);
        Assert.Contains(go, scene.AllObjects);
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();

        scene.Add(go);
        scene.Add(go);

        Assert.Equal(1, scene.Count);
    }

    [Fact]
    public void Add_RegistersChildrenRecursively()
    {
        Scene scene = CreateScene();
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        scene.Add(parent);

        Assert.Equal(2, scene.Count);
        Assert.Same(scene, child.Scene);
    }

    [Fact]
    public void Remove_UnregistersObject_AndClearsScene()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        scene.Add(go);

        scene.Remove(go);

        Assert.Null(go.Scene);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void Remove_UnregistersChildrenRecursively()
    {
        Scene scene = CreateScene();
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);
        scene.Add(parent);

        scene.Remove(parent);

        Assert.Equal(0, scene.Count);
        Assert.Null(child.Scene);
    }

    [Fact]
    public void Add_MovesObjectFromPreviousScene()
    {
        Scene scene1 = CreateScene();
        Scene scene2 = CreateScene();
        GameObject go = CreateGameObject();
        scene1.Add(go);

        scene2.Add(go);

        Assert.Same(scene2, go.Scene);
        Assert.DoesNotContain(go, scene1.AllObjects);
        Assert.Contains(go, scene2.AllObjects);
    }

    [Fact]
    public void Clear_RemovesAllObjects()
    {
        Scene scene = CreateScene();
        scene.Add(CreateGameObject("A"));
        scene.Add(CreateGameObject("B"));

        scene.Clear();

        Assert.True(scene.IsEmpty);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void Flush_DropsDisposedObjects()
    {
        Scene scene = CreateScene();
        GameObject keep = CreateGameObject("Keep");
        GameObject drop = CreateGameObject("Drop");
        scene.Add(keep);
        scene.Add(drop);

        drop.Dispose();
        // Count and AllObjects both exclude disposed objects immediately (before Flush), so they
        // alone can't prove Flush does anything - verify Flush actually removes it from the scene.
        Assert.Equal(1, scene.Count);
        Assert.DoesNotContain(drop, scene.AllObjects);
        Assert.NotNull(drop.Scene); // still owned by the scene until flushed

        scene.Flush();

        Assert.Null(drop.Scene); // Flush detached it from the scene
        Assert.Equal(1, scene.Count);
        Assert.Contains(keep, scene.AllObjects);
    }

    [Fact]
    public void Count_ExcludesDisposedObjects()
    {
        Scene scene = CreateScene();
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        scene.Add(a);
        scene.Add(b);
        Assert.Equal(2, scene.Count);

        a.Dispose();

        Assert.Equal(1, scene.Count);
    }

    // ---- Collection views ----

    [Fact]
    public void RootObjects_ExcludesChildren()
    {
        Scene scene = CreateScene();
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);
        scene.Add(parent);

        Assert.Single(scene.RootObjects);
        Assert.Contains(parent, scene.RootObjects);
        Assert.DoesNotContain(child, scene.RootObjects);
    }

    [Fact]
    public void ActiveObjects_ExcludesDisabled()
    {
        Scene scene = CreateScene();
        GameObject on = CreateGameObject("On");
        GameObject off = CreateGameObject("Off");
        off.Enabled = false;
        scene.Add(on);
        scene.Add(off);

        var active = scene.ActiveObjects.ToList();

        Assert.Contains(on, active);
        Assert.DoesNotContain(off, active);
    }

    [Fact]
    public void SaveableObjects_ExcludesDontSave()
    {
        Scene scene = CreateScene();
        GameObject normal = CreateGameObject("Normal");
        GameObject hidden = CreateGameObject("Hidden");
        hidden.HideFlags = HideFlags.DontSave;
        scene.Add(normal);
        scene.Add(hidden);

        var saveable = scene.SaveableObjects.ToList();

        Assert.Contains(normal, saveable);
        Assert.DoesNotContain(hidden, saveable);
    }

    [Fact]
    public void IsEmpty_ReflectsContents()
    {
        Scene scene = CreateScene();
        Assert.True(scene.IsEmpty);

        GameObject go = CreateGameObject();
        scene.Add(go);
        Assert.False(scene.IsEmpty);

        scene.Remove(go);
        Assert.True(scene.IsEmpty);
    }

    // ---- Find ----

    [Fact]
    public void FindObjectsOfType_ReturnsGameObjectsAndComponents()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Contains(go, scene.FindObjectsOfType<GameObject>());
        Assert.Contains(comp, scene.FindObjectsOfType<PlainComponent>());
    }

    [Fact]
    public void FindObjectByID_FindsGameObjectAndComponent()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Same(go, scene.FindObjectByID<GameObject>(go.InstanceID));
        Assert.Same(comp, scene.FindObjectByID<PlainComponent>(comp.InstanceID));
        Assert.Null(scene.FindObjectByID<GameObject>(-12345));
    }

    [Fact]
    public void FindObjectByIdentifier_FindsGameObjectAndComponent()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Same(go, scene.FindObjectByIdentifier<GameObject>(go.Identifier));
        Assert.Same(comp, scene.FindObjectByIdentifier<PlainComponent>(comp.Identifier));
    }

    // ---- Static scene manager ----
    //
    // Load only queues. The swap lands at the end of the frame, which the game loop drives and these
    // tests drive by hand. There is no unload: there is always a current scene.

    [Fact]
    public void Current_IsNeverNull()
    {
        Assert.NotNull(Scene.Current);
        Assert.False(Scene.Current.IsDisposed);
    }

    [Fact]
    public void Current_RebuildsAfterTheCurrentSceneIsDisposed()
    {
        Scene first = Scene.Current;
        first.Dispose();

        Scene second = Scene.Current;

        Assert.NotSame(first, second);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void Load_SetsCurrent_EnablesScene_FiresEvent()
    {
        Scene scene = CreateScene();
        bool fired = false;
        void handler() => fired = true;
        Scene.OnSceneLoaded += handler;
        try
        {
            Scene.Load(scene);
            Scene.ProcessPendingLoad();

            Assert.Same(scene, Scene.Current);
            Assert.True(scene.IsActive);
            Assert.True(fired);
        }
        finally
        {
            Scene.OnSceneLoaded -= handler;
        }
    }

    [Fact]
    public void Load_QueuedUntilProcessed()
    {
        Scene before = Scene.Current;
        Scene scene = CreateScene();

        Scene.Load(scene);

        Assert.Same(before, Scene.Current);
        Assert.False(scene.IsActive);

        Scene.ProcessPendingLoad();

        Assert.Same(scene, Scene.Current);
    }

    [Fact]
    public void Load_ReplacingCurrent_DisposesPrevious()
    {
        Scene first = CreateScene();
        Scene second = CreateScene();

        Scene.Load(first);
        Scene.ProcessPendingLoad();

        Scene.Load(second);

        // The outgoing scene stays usable until the swap actually applies.
        Assert.Same(first, Scene.Current);
        Assert.False(first.IsDisposed);

        Scene.ProcessPendingLoad();

        Assert.Same(second, Scene.Current);
        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void Load_LastRequestOfTheFrameWins()
    {
        Scene first = CreateScene();
        Scene second = CreateScene();

        Scene.Load(first);
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.Same(second, Scene.Current);
        Assert.True(first.IsDisposed); // replaced before it loaded, so nothing else would ever free it
    }

    // Loading the scene that is already current used to dispose it and then enable the corpse.
    [Fact]
    public void Load_TheCurrentScene_IsANoOp()
    {
        Scene current = Scene.Current;

        Scene.Load(current);
        Scene.ProcessPendingLoad();

        Assert.Same(current, Scene.Current);
        Assert.False(current.IsDisposed);
        Assert.True(current.IsActive);
    }

    [Fact]
    public void Load_AnAlreadyEnabledScene_DoesNotThrow()
    {
        Scene scene = CreateScene(enable: true);

        Scene.Load(scene);
        Scene.ProcessPendingLoad();

        Assert.Same(scene, Scene.Current);
        Assert.True(scene.IsActive);
    }

    [Fact]
    public void EnableAndDisable_AreIdempotent()
    {
        Scene scene = CreateScene();

        scene.Enable();
        scene.Enable();
        Assert.True(scene.IsActive);

        scene.Disable();
        scene.Disable();
        Assert.False(scene.IsActive);
    }

    // A component disposing its own scene mid-callback used to blow up on the trailing Flush().
    [Fact]
    public void FrameCallbacks_OnASceneDisposedMidCallback_DoNotThrow()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        UpdateActionComponent driver = go.AddComponent<UpdateActionComponent>();
        driver.Action = () => scene.Dispose();
        scene.Add(go);

        scene.Update(); // must not throw

        Assert.True(scene.IsDisposed);
    }

    [Fact]
    public void FrameCallbacks_OnADisposedScene_AreNoOps()
    {
        Scene scene = CreateScene(enable: true);
        scene.Dispose();

        scene.Update();
        scene.FixedUpdate();
        scene.DrawGizmos();
        scene.Flush();
        Assert.False(scene.Render());
    }

    // ---- Surviving a scene load ----

    private sealed class TickCounter : Component
    {
        public int Enables, Disables, Updates;
        public override void OnEnable() => Enables++;
        public override void OnDisable() => Disables++;
        public override void Update() => Updates++;
    }

    // The hand-rolled version: take the object out of the outgoing scene and put it in the incoming
    // one. It works, as long as you add it to the scene you are loading and not to Scene.Current,
    // which is still the outgoing scene until the swap applies.
    [Fact]
    public void ManualPreserve_RemoveFromOldSceneAndAddToTheNextOne_Survives()
    {
        Scene first = CreateScene();
        GameObject keeper = CreateGameObject("Keeper");
        first.Add(keeper);
        Scene.Load(first);
        Scene.ProcessPendingLoad();

        Scene second = CreateScene();
        first.Remove(keeper);
        second.Add(keeper);
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.False(keeper.IsDisposed);
        Assert.Same(second, keeper.Scene);
        Assert.Contains(keeper, second.AllObjects);
    }

    // The trap: after Load(), Scene.Current is still the outgoing scene, so adding there hands the
    // object to the scene that is about to be disposed.
    [Fact]
    public void ManualPreserve_AddingBackToSceneCurrentAfterLoad_LosesTheObject()
    {
        Scene first = CreateScene();
        GameObject keeper = CreateGameObject("Keeper");
        first.Add(keeper);
        Scene.Load(first);
        Scene.ProcessPendingLoad();

        Scene second = CreateScene();
        first.Remove(keeper);
        Scene.Load(second);
        Scene.Current.Add(keeper); // still `first` at this point
        Scene.ProcessPendingLoad();

        Assert.True(keeper.IsDisposed, "Scene.Current is only the new scene once the swap applies.");
    }

    [Fact]
    public void DontDestroyOnLoad_SurvivesTheLoad_AndJoinsTheNewScene()
    {
        Scene first = CreateScene();
        GameObject keeper = CreateGameObject("Keeper");
        GameObject doomed = CreateGameObject("Doomed");
        first.Add(keeper);
        first.Add(doomed);
        Scene.Load(first);
        Scene.ProcessPendingLoad();

        Scene.DontDestroyOnLoad(keeper);

        Scene second = CreateScene();
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.False(keeper.IsDisposed);
        Assert.Same(second, keeper.Scene);
        Assert.Contains(keeper, second.AllObjects);
        Assert.True(doomed.IsDisposed, "Anything not preserved goes with the old scene.");
    }

    [Fact]
    public void DontDestroyOnLoad_KeepsTicking_InTheNewScene()
    {
        Scene first = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        first.Add(keeper);
        Scene.Load(first);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(keeper);

        Update(first);
        Assert.Equal(1, comp.Updates);

        Scene second = CreateScene();
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Update(second);
        Assert.Equal(2, comp.Updates); // re-registered with the new scene's dispatcher
    }

    [Fact]
    public void DontDestroyOnLoad_MovesSceneRegistrations_ToTheNewScene()
    {
        Scene first = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        GameCanvas canvas = keeper.AddComponent<GameCanvas>();
        first.Add(keeper);
        Scene.Load(first);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(keeper);
        Assert.Equal([canvas], first.Canvases);

        Scene second = CreateScene();
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.Equal([canvas], second.Canvases);
    }

    [Fact]
    public void DontDestroyOnLoad_DoesNotRestartTheObject()
    {
        Scene first = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        first.Add(keeper);
        Scene.Load(first);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(keeper);
        int enables = comp.Enables;

        Scene.Load(CreateScene());
        Scene.ProcessPendingLoad();

        Assert.Equal(enables, comp.Enables);
        Assert.Equal(0, comp.Disables);
    }

    [Fact]
    public void DontDestroyOnLoad_OnAChild_PreservesItsRootInstead()
    {
        Scene first = CreateScene();
        GameObject root = CreateGameObject("Root");
        GameObject child = CreateGameObject("Child");
        child.SetParent(root);
        first.Add(root);
        Scene.Load(first);
        Scene.ProcessPendingLoad();

        Scene.DontDestroyOnLoad(child);

        Scene second = CreateScene();
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.False(root.IsDisposed);
        Assert.False(child.IsDisposed);
        Assert.Same(second, root.Scene);
        Assert.Same(second, child.Scene);
        Assert.Same(root, child.Parent);
    }

    [Fact]
    public void CancelDontDestroyOnLoad_LetsItDieWithTheScene()
    {
        Scene first = CreateScene();
        GameObject go = CreateGameObject();
        first.Add(go);
        Scene.Load(first);
        Scene.ProcessPendingLoad();

        Scene.DontDestroyOnLoad(go);
        Scene.CancelDontDestroyOnLoad(go);

        Scene.Load(CreateScene());
        Scene.ProcessPendingLoad();

        Assert.True(go.IsDisposed);
    }

    [Fact]
    public void DontDestroyOnLoad_ADestroyedObject_IsDroppedNotResurrected()
    {
        Scene first = CreateScene();
        GameObject go = CreateGameObject();
        first.Add(go);
        Scene.Load(first);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(go);

        go.Dispose();

        Scene second = CreateScene();
        Scene.Load(second);
        Scene.ProcessPendingLoad(); // must not throw or re-add the corpse

        Assert.Empty(second.AllObjects);
    }

    // Leaving play mode ends the preservation session. Without this the objects a play session kept
    // alive would ride the swap into the authoring scene the editor restores behind it, and stay there.
    [Fact]
    public void DestroyPreserved_KeepsThemOutOfTheNextScene()
    {
        Scene play = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        play.Add(keeper);
        Scene.Load(play);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(keeper);

        Scene.DestroyPreserved();

        // Same order the game loop uses: the destroy queue drains, then the scene swap applies.
        Scene restored = CreateScene();
        Scene.Load(restored);
        EngineObject.ProcessDestroyed();
        Scene.ProcessPendingLoad();

        Assert.True(keeper.IsDisposed);
        Assert.Empty(restored.AllObjects);
    }

    // Queued like any other Destroy, so teardown lands at the end of the frame rather than under
    // whatever was running when the play button was clicked.
    [Fact]
    public void DestroyPreserved_TearsDownAtTheEndOfTheFrame()
    {
        Scene scene = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        scene.Add(keeper);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(keeper);

        Scene.DestroyPreserved();

        Assert.False(keeper.IsDisposed);
        Assert.Equal(0, comp.Disables);

        EngineObject.ProcessDestroyed();

        Assert.True(keeper.IsDisposed);
        Assert.Equal(1, comp.Disables); // torn down properly, not just dropped from the registry
    }

    [Fact]
    public void Shutdown_DestroysPreservedObjects()
    {
        Scene scene = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        scene.Add(keeper);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Scene.DontDestroyOnLoad(keeper);

        Scene.Shutdown();

        // No frame follows a shutdown, so nothing would drain a destroy queue: teardown is immediate.
        Assert.True(keeper.IsDisposed);
        Assert.True(scene.IsDisposed);

        // And the registry is empty, so the next run does not inherit the last one's objects.
        Scene next = CreateScene();
        Scene.Load(next);
        Scene.ProcessPendingLoad();
        Assert.Empty(next.AllObjects);
    }

    [Fact]
    public void Load_SkipsASceneDisposedBeforeItApplied()
    {
        Scene current = CreateScene();
        Scene queued = CreateScene();

        Scene.Load(current);
        Scene.ProcessPendingLoad();

        Scene.Load(queued);
        queued.Dispose();
        Scene.ProcessPendingLoad();

        Assert.Same(current, Scene.Current);
        Assert.False(current.IsDisposed);
    }
}
