// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A test component that tracks all lifecycle events for verification in unit tests.
/// </summary>
public class TestLifecycleComponent : MonoBehaviour
{
    public List<string> Events { get; } = [];

    public void ClearEvents() => Events.Clear();

    public override void OnAddedToScene()
    {
        Events.Add("OnAddedToScene");
    }

    public override void OnRemovedFromScene()
    {
        Events.Add("OnRemovedFromScene");
    }

    public override void OnEnable()
    {
        Events.Add("OnEnable");
    }

    public override void OnDisable()
    {
        Events.Add("OnDisable");
    }

    public override void Start()
    {
        Events.Add("Start");
    }

    public override void OnDispose()
    {
        Events.Add("OnDispose");
        base.OnDispose();
    }
}

/// <summary>
/// Comprehensive tests for MonoBehaviour lifecycle methods.
/// Ported from the LifecycleTest sample project.
/// </summary>
public class LifecycleTests : IDisposable
{
    private readonly List<Scene> _scenes = [];
    private readonly List<GameObject> _gameObjects = [];

    public void Dispose()
    {
        // Clean up all created scenes and game objects
        foreach (var scene in _scenes)
        {
            if (!scene.IsDisposed)
            {
                if (scene.IsActive)
                    scene.Disable();
                scene.Dispose();
            }
        }
        _scenes.Clear();

        foreach (var go in _gameObjects)
        {
            if (!go.IsDisposed)
                go.Dispose();
        }
        _gameObjects.Clear();
    }

    private Scene CreateScene()
    {
        var scene = new Scene();
        _scenes.Add(scene);
        return scene;
    }

    private GameObject CreateGameObject(string name = "TestObject")
    {
        var go = new GameObject(name);
        _gameObjects.Add(go);
        return go;
    }

    /// <summary>
    /// Test 1: Adding GameObject to DISABLED Scene
    /// Expected: OnAddedToScene called, OnEnable NOT called (scene disabled)
    /// </summary>
    [Fact]
    public void AddingGameObject_ToDisabledScene_CallsOnAddedToSceneOnly()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();

        scene.Add(go);

        Assert.Contains("OnAddedToScene", comp.Events);
        Assert.DoesNotContain("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 2: Enabling Scene
    /// Expected: OnEnable called for all enabled components
    /// </summary>
    [Fact]
    public void EnablingScene_CallsOnEnableForAllEnabledComponents()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        scene.Enable();

        Assert.Contains("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 3: Adding GameObject to ENABLED Scene
    /// Expected: OnAddedToScene called, then OnEnable called (scene enabled)
    /// </summary>
    [Fact]
    public void AddingGameObject_ToEnabledScene_CallsOnAddedToSceneAndOnEnable()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();

        scene.Add(go);

        Assert.Equal(2, comp.Events.Count);
        Assert.Equal("OnAddedToScene", comp.Events[0]);
        Assert.Equal("OnEnable", comp.Events[1]);
    }

    /// <summary>
    /// Test 4: Toggling Component Enabled State
    /// Expected: OnDisable called when disabled, OnEnable called when re-enabled
    /// </summary>
    [Fact]
    public void TogglingComponentEnabled_CallsOnDisableAndOnEnable()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        // Disable component
        comp.Enabled = false;
        Assert.Contains("OnDisable", comp.Events);

        comp.ClearEvents();

        // Re-enable component
        comp.Enabled = true;
        Assert.Contains("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 5: Toggling GameObject Enabled State
    /// Expected: OnDisable called for all enabled components when disabled,
    /// OnEnable called for all enabled components when re-enabled
    /// </summary>
    [Fact]
    public void TogglingGameObjectEnabled_CallsOnDisableAndOnEnableForComponents()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        // Disable GameObject
        go.Enabled = false;
        Assert.Contains("OnDisable", comp.Events);

        comp.ClearEvents();

        // Re-enable GameObject
        go.Enabled = true;
        Assert.Contains("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 6: Disabling Scene
    /// Expected: OnDisable called for all enabled components
    /// </summary>
    [Fact]
    public void DisablingScene_CallsOnDisableForAllEnabledComponents()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        scene.Disable();

        Assert.Contains("OnDisable", comp.Events);
    }

    /// <summary>
    /// Test 7: Toggling States in DISABLED Scene
    /// Expected: No OnDisable/OnEnable (scene disabled)
    /// </summary>
    [Fact]
    public void TogglingStates_InDisabledScene_DoesNotCallOnEnableOrOnDisable()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        // Disable component in disabled scene
        comp.Enabled = false;
        Assert.DoesNotContain("OnDisable", comp.Events);

        // Re-enable component in disabled scene
        comp.Enabled = true;
        Assert.DoesNotContain("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 8: Re-enabling Scene
    /// Expected: OnEnable called for all enabled components
    /// </summary>
    [Fact]
    public void ReEnablingScene_CallsOnEnableForAllEnabledComponents()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        scene.Disable();
        comp.ClearEvents();

        scene.Enable();

        Assert.Contains("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 9: Parent-Child Hierarchy Enabled State
    /// Expected: Child components follow parent enabled state
    /// </summary>
    [Fact]
    public void ParentChildHierarchy_ChildFollowsParentEnabledState()
    {
        var scene = CreateScene();
        scene.Enable();

        var parent = CreateGameObject("Parent");
        var parentComp = parent.AddComponent<TestLifecycleComponent>();
        scene.Add(parent);

        var child = CreateGameObject("Child");
        var childComp = child.AddComponent<TestLifecycleComponent>();

        // Parent child to parent object
        child.SetParent(parent);

        Assert.Contains("OnAddedToScene", childComp.Events);
        Assert.Contains("OnEnable", childComp.Events);

        parentComp.ClearEvents();
        childComp.ClearEvents();

        // Disable parent - both should get OnDisable
        parent.Enabled = false;
        Assert.Contains("OnDisable", parentComp.Events);
        Assert.Contains("OnDisable", childComp.Events);

        parentComp.ClearEvents();
        childComp.ClearEvents();

        // Re-enable parent - both should get OnEnable
        parent.Enabled = true;
        Assert.Contains("OnEnable", parentComp.Events);
        Assert.Contains("OnEnable", childComp.Events);
    }

    /// <summary>
    /// Test 10: Removing GameObject from Scene
    /// Expected: OnDisable (if enabled), then OnRemovedFromScene
    /// </summary>
    [Fact]
    public void RemovingGameObject_FromScene_CallsOnDisableAndOnRemovedFromScene()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        scene.Remove(go);

        Assert.Equal(2, comp.Events.Count);
        Assert.Equal("OnDisable", comp.Events[0]);
        Assert.Equal("OnRemovedFromScene", comp.Events[1]);
    }

    /// <summary>
    /// Test 11: Disposing GameObject
    /// Expected: OnDisable (if enabled), OnDispose (if OnEnable was called)
    /// Note: OnDispose is called if the component has previously had OnEnable called
    /// </summary>
    [Fact]
    public void DisposingGameObject_CallsOnDisableAndOnDispose()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        Assert.True(comp.HasBeenEnabled); // OnEnable was called when added to active scene
        comp.ClearEvents();

        go.Dispose();
        _gameObjects.Remove(go); // Already disposed

        Assert.Contains("OnDisable", comp.Events);
        Assert.Contains("OnDispose", comp.Events);
    }

    /// <summary>
    /// Test 11b: Disposing GameObject that was never enabled
    /// Expected: No OnDisable or OnDispose (component was never enabled)
    /// </summary>
    [Fact]
    public void DisposingGameObject_NeverEnabled_NoOnDispose()
    {
        var scene = CreateScene();
        // Scene is NOT enabled
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        Assert.False(comp.HasBeenEnabled); // OnEnable was NOT called
        comp.ClearEvents();

        go.Dispose();
        _gameObjects.Remove(go); // Already disposed

        // Neither OnDisable nor OnDispose should be called
        Assert.DoesNotContain("OnDisable", comp.Events);
        Assert.DoesNotContain("OnDispose", comp.Events);
    }

    /// <summary>
    /// Test 12: Removing Component
    /// Expected: OnDisable (if enabled), OnDispose (if OnEnable was called)
    /// </summary>
    [Fact]
    public void RemovingComponent_CallsOnDisableAndOnDispose()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        Assert.True(comp.HasBeenEnabled);
        comp.ClearEvents();

        go.RemoveComponent(comp);

        Assert.Contains("OnDisable", comp.Events);
        Assert.Contains("OnDispose", comp.Events);
    }

    /// <summary>
    /// Test 12b: Removing Component that was never enabled
    /// Expected: No OnDisable or OnDispose
    /// </summary>
    [Fact]
    public void RemovingComponent_NeverEnabled_NoOnDispose()
    {
        var scene = CreateScene();
        // Scene is NOT enabled
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        Assert.False(comp.HasBeenEnabled);
        comp.ClearEvents();

        go.RemoveComponent(comp);

        Assert.DoesNotContain("OnDisable", comp.Events);
        Assert.DoesNotContain("OnDispose", comp.Events);
    }

    /// <summary>
    /// Test 13: Multiple Scene Management - Moving object between scenes
    /// Expected: OnDisable (from source), OnRemovedFromScene, OnAddedToScene, OnEnable (to target)
    /// </summary>
    [Fact]
    public void MovingObject_BetweenEnabledScenes_CallsProperLifecycleSequence()
    {
        var scene1 = CreateScene();
        var scene2 = CreateScene();
        scene1.Enable();
        scene2.Enable();

        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene1.Add(go);
        comp.ClearEvents();

        // Move from scene1 to scene2
        scene2.Add(go);

        // Verify the sequence: OnDisable -> OnRemovedFromScene -> OnAddedToScene -> OnEnable
        Assert.Equal(4, comp.Events.Count);
        Assert.Equal("OnDisable", comp.Events[0]);
        Assert.Equal("OnRemovedFromScene", comp.Events[1]);
        Assert.Equal("OnAddedToScene", comp.Events[2]);
        Assert.Equal("OnEnable", comp.Events[3]);
    }

    /// <summary>
    /// Test 13b: Multiple Scene Management - Adding to disabled scene then enabling
    /// Expected: OnAddedToScene (no OnEnable), then OnEnable when scene enabled
    /// </summary>
    [Fact]
    public void AddingToDisabledScene_ThenEnabling_CallsCorrectSequence()
    {
        var scene = CreateScene();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();

        scene.Add(go);
        Assert.Contains("OnAddedToScene", comp.Events);
        Assert.DoesNotContain("OnEnable", comp.Events);

        comp.ClearEvents();
        scene.Enable();
        Assert.Contains("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 14: Disabled GameObject Added to Enabled Scene
    /// Expected: OnAddedToScene only, no OnEnable (GameObject disabled)
    /// </summary>
    [Fact]
    public void DisabledGameObject_AddedToEnabledScene_CallsOnAddedToSceneOnly()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        go.Enabled = false;
        var comp = go.AddComponent<TestLifecycleComponent>();

        scene.Add(go);

        Assert.Contains("OnAddedToScene", comp.Events);
        Assert.DoesNotContain("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 14b: Enabling previously disabled GameObject in active scene
    /// Expected: OnEnable called when GameObject is enabled
    /// </summary>
    [Fact]
    public void EnablingDisabledGameObject_InActiveScene_CallsOnEnable()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        go.Enabled = false;
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        comp.ClearEvents();

        go.Enabled = true;

        Assert.Contains("OnEnable", comp.Events);
    }

    /// <summary>
    /// Test 15: Scene Cleanup - Disable and Dispose
    /// Expected: OnDisable for all enabled components, OnDispose when scene disposed
    /// </summary>
    [Fact]
    public void SceneCleanup_DisableAndDispose_CallsProperSequence()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        Assert.True(comp.HasBeenEnabled);
        comp.ClearEvents();

        scene.Disable();
        Assert.Contains("OnDisable", comp.Events);

        comp.ClearEvents();
        scene.Dispose();
        _scenes.Remove(scene); // Already disposed
        _gameObjects.Remove(go); // Disposed with scene

        Assert.Contains("OnDispose", comp.Events);
    }

    /// <summary>
    /// Test 15b: Scene Cleanup when components were never enabled
    /// Expected: No OnDisable or OnDispose
    /// </summary>
    [Fact]
    public void SceneCleanup_NeverEnabled_NoOnDispose()
    {
        var scene = CreateScene();
        // Scene is NOT enabled
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        scene.Add(go);
        Assert.False(comp.HasBeenEnabled);
        comp.ClearEvents();

        scene.Dispose();
        _scenes.Remove(scene); // Already disposed
        _gameObjects.Remove(go); // Disposed with scene

        Assert.DoesNotContain("OnDisable", comp.Events);
        Assert.DoesNotContain("OnDispose", comp.Events);
    }

    /// <summary>
    /// Additional test: Disabled component on enabled GameObject in active scene
    /// Expected: OnAddedToScene called, but not OnEnable (component disabled)
    /// </summary>
    [Fact]
    public void DisabledComponent_OnEnabledGameObject_CallsOnAddedToSceneOnly()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp = go.AddComponent<TestLifecycleComponent>();
        comp.Enabled = false;

        scene.Add(go);

        Assert.Contains("OnAddedToScene", comp.Events);
        Assert.DoesNotContain("OnEnable", comp.Events);
    }

    /// <summary>
    /// Additional test: Multiple components on same GameObject
    /// Expected: All components receive lifecycle events
    /// </summary>
    [Fact]
    public void MultipleComponents_AllReceiveLifecycleEvents()
    {
        var scene = CreateScene();
        scene.Enable();
        var go = CreateGameObject();
        var comp1 = go.AddComponent<TestLifecycleComponent>();
        var comp2 = go.AddComponent<TestLifecycleComponent>();

        scene.Add(go);

        Assert.Contains("OnAddedToScene", comp1.Events);
        Assert.Contains("OnEnable", comp1.Events);
        Assert.Contains("OnAddedToScene", comp2.Events);
        Assert.Contains("OnEnable", comp2.Events);
    }

    /// <summary>
    /// Additional test: Child unparented stays in scene
    /// Expected: Child remains in scene when unparented (unparenting doesn't remove from scene)
    /// </summary>
    [Fact]
    public void ChildUnparented_StaysInScene()
    {
        var scene = CreateScene();
        scene.Enable();

        var parent = CreateGameObject("Parent");
        scene.Add(parent);

        var child = CreateGameObject("Child");
        var childComp = child.AddComponent<TestLifecycleComponent>();
        child.SetParent(parent);
        childComp.ClearEvents();

        // Unparent child - it stays in the scene, just loses its parent
        child.SetParent(null);

        // No lifecycle events are triggered - child stays in scene
        Assert.DoesNotContain("OnDisable", childComp.Events);
        Assert.DoesNotContain("OnRemovedFromScene", childComp.Events);
        Assert.Equal(scene, child.Scene);
    }

    /// <summary>
    /// Additional test: Child explicitly removed from scene
    /// Expected: Child gets OnDisable and OnRemovedFromScene when scene.Remove() is called
    /// </summary>
    [Fact]
    public void ChildExplicitlyRemoved_GetsOnDisableAndOnRemovedFromScene()
    {
        var scene = CreateScene();
        scene.Enable();

        var parent = CreateGameObject("Parent");
        scene.Add(parent);

        var child = CreateGameObject("Child");
        var childComp = child.AddComponent<TestLifecycleComponent>();
        child.SetParent(parent);
        childComp.ClearEvents();

        // Explicitly remove child from scene
        scene.Remove(child);

        Assert.Contains("OnDisable", childComp.Events);
        Assert.Contains("OnRemovedFromScene", childComp.Events);
        Assert.Null(child.Scene);
    }

    /// <summary>
    /// Additional test: Scene not double-enabled
    /// Expected: Throws exception when enabling already enabled scene
    /// </summary>
    [Fact]
    public void EnablingAlreadyEnabledScene_ThrowsException()
    {
        var scene = CreateScene();
        scene.Enable();

        Assert.Throws<Exception>(() => scene.Enable());
    }

    /// <summary>
    /// Additional test: Scene not double-disabled
    /// Expected: Throws exception when disabling already disabled scene
    /// </summary>
    [Fact]
    public void DisablingAlreadyDisabledScene_ThrowsException()
    {
        var scene = CreateScene();

        Assert.Throws<Exception>(() => scene.Disable());
    }

    /// <summary>
    /// Additional test: Child added to parent that is already in scene
    /// Expected: Child gets OnAddedToScene and OnEnable
    /// </summary>
    [Fact]
    public void ChildAddedToParentInScene_GetsLifecycleEvents()
    {
        var scene = CreateScene();
        scene.Enable();

        var parent = CreateGameObject("Parent");
        scene.Add(parent);

        var child = CreateGameObject("Child");
        var childComp = child.AddComponent<TestLifecycleComponent>();

        child.SetParent(parent);

        Assert.Contains("OnAddedToScene", childComp.Events);
        Assert.Contains("OnEnable", childComp.Events);
    }
}
