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

            Assert.Same(scene, Scene.Current);
            Assert.True(scene.IsActive);
            Assert.True(fired);
        }
        finally
        {
            Scene.OnSceneLoaded -= handler;
            Scene.Unload();
        }
    }

    [Fact]
    public void Load_ReplacingCurrent_DisposesPrevious()
    {
        var first = CreateScene();
        var second = CreateScene();
        try
        {
            Scene.Load(first);
            Scene.Load(second);

            Assert.Same(second, Scene.Current);
            Assert.True(first.IsDisposed);
        }
        finally
        {
            Scene.Unload();
        }
    }

    [Fact]
    public void Unload_DisposesAndClearsCurrent()
    {
        var scene = CreateScene();
        Scene.Load(scene);

        Scene.Unload();

        Assert.Null(Scene.Current);
        Assert.True(scene.IsDisposed);
    }
}
