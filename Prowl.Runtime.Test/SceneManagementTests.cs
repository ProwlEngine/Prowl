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
        var scene = CreateScene();
        var root = CreateGameObject("root");
        var child = CreateGameObject("child");
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
        var scene = CreateScene();
        var go = CreateGameObject();

        scene.Add(go);

        Assert.Same(scene, go.Scene);
        Assert.Equal(1, scene.Count);
        Assert.Contains(go, scene.AllObjects);
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        var scene = CreateScene();
        var go = CreateGameObject();

        scene.Add(go);
        scene.Add(go);

        Assert.Equal(1, scene.Count);
    }

    [Fact]
    public void Add_RegistersChildrenRecursively()
    {
        var scene = CreateScene();
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        scene.Add(parent);

        Assert.Equal(2, scene.Count);
        Assert.Same(scene, child.Scene);
    }

    [Fact]
    public void Remove_UnregistersObject_AndClearsScene()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        scene.Add(go);

        scene.Remove(go);

        Assert.Null(go.Scene);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void Remove_UnregistersChildrenRecursively()
    {
        var scene = CreateScene();
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);
        scene.Add(parent);

        scene.Remove(parent);

        Assert.Equal(0, scene.Count);
        Assert.Null(child.Scene);
    }

    [Fact]
    public void Add_MovesObjectFromPreviousScene()
    {
        var scene1 = CreateScene();
        var scene2 = CreateScene();
        var go = CreateGameObject();
        scene1.Add(go);

        scene2.Add(go);

        Assert.Same(scene2, go.Scene);
        Assert.DoesNotContain(go, scene1.AllObjects);
        Assert.Contains(go, scene2.AllObjects);
    }

    [Fact]
    public void Clear_RemovesAllObjects()
    {
        var scene = CreateScene();
        scene.Add(CreateGameObject("A"));
        scene.Add(CreateGameObject("B"));

        scene.Clear();

        Assert.True(scene.IsEmpty);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void Flush_DropsDisposedObjects()
    {
        var scene = CreateScene();
        var keep = CreateGameObject("Keep");
        var drop = CreateGameObject("Drop");
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
        var scene = CreateScene();
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
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
        var scene = CreateScene();
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);
        scene.Add(parent);

        Assert.Single(scene.RootObjects);
        Assert.Contains(parent, scene.RootObjects);
        Assert.DoesNotContain(child, scene.RootObjects);
    }

    [Fact]
    public void ActiveObjects_ExcludesDisabled()
    {
        var scene = CreateScene();
        var on = CreateGameObject("On");
        var off = CreateGameObject("Off");
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
        var scene = CreateScene();
        var normal = CreateGameObject("Normal");
        var hidden = CreateGameObject("Hidden");
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
        var scene = CreateScene();
        Assert.True(scene.IsEmpty);

        var go = CreateGameObject();
        scene.Add(go);
        Assert.False(scene.IsEmpty);

        scene.Remove(go);
        Assert.True(scene.IsEmpty);
    }

    // ---- Find ----

    [Fact]
    public void FindObjectsOfType_ReturnsGameObjectsAndComponents()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Contains(go, scene.FindObjectsOfType<GameObject>());
        Assert.Contains(comp, scene.FindObjectsOfType<PlainComponent>());
    }

    [Fact]
    public void FindObjectByID_FindsGameObjectAndComponent()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Same(go, scene.FindObjectByID<GameObject>(go.InstanceID));
        Assert.Same(comp, scene.FindObjectByID<PlainComponent>(comp.InstanceID));
        Assert.Null(scene.FindObjectByID<GameObject>(-12345));
    }

    [Fact]
    public void FindObjectByIdentifier_FindsGameObjectAndComponent()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<PlainComponent>();
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
        var scene = CreateScene();
        bool fired = false;
        Action handler = () => fired = true;
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
        var scene = CreateScene();

        Scene.Load(scene);

        Assert.Same(before, Scene.Current);
        Assert.False(scene.IsActive);

        Scene.ProcessPendingLoad();

        Assert.Same(scene, Scene.Current);
    }

    [Fact]
    public void Load_ReplacingCurrent_DisposesPrevious()
    {
        var first = CreateScene();
        var second = CreateScene();

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
        var first = CreateScene();
        var second = CreateScene();

        Scene.Load(first);
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.Same(second, Scene.Current);
        Assert.False(first.IsActive);
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
        var scene = CreateScene(enable: true);

        Scene.Load(scene);
        Scene.ProcessPendingLoad();

        Assert.Same(scene, Scene.Current);
        Assert.True(scene.IsActive);
    }

    [Fact]
    public void EnableAndDisable_AreIdempotent()
    {
        var scene = CreateScene();

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
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var driver = go.AddComponent<UpdateActionComponent>();
        driver.Action = () => scene.Dispose();
        scene.Add(go);

        scene.Update(); // must not throw

        Assert.True(scene.IsDisposed);
    }

    [Fact]
    public void FrameCallbacks_OnADisposedScene_AreNoOps()
    {
        var scene = CreateScene(enable: true);
        scene.Dispose();

        scene.Update();
        scene.FixedUpdate();
        scene.DrawGizmos();
        scene.Flush();
        Assert.False(scene.Render());
    }

    [Fact]
    public void Load_SkipsASceneDisposedBeforeItApplied()
    {
        var current = CreateScene();
        var queued = CreateScene();

        Scene.Load(current);
        Scene.ProcessPendingLoad();

        Scene.Load(queued);
        queued.Dispose();
        Scene.ProcessPendingLoad();

        Assert.Same(current, Scene.Current);
        Assert.False(current.IsDisposed);
    }
}
