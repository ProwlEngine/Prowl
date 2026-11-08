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
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();

        scene.Add(go);

        Assert.Same(scene, go.Scene);
        Assert.Equal(1, scene.Count);
        Assert.Contains(go, scene.AllObjects);
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();

        scene.Add(go);
        scene.Add(go);

        Assert.Equal(1, scene.Count);
    }

    [Fact]
    public void Add_RegistersChildrenRecursively()
    {
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();
        scene.Add(go);

        scene.Remove(go);

        Assert.Null(go.Scene);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void Remove_UnregistersChildrenRecursively()
    {
        SceneInstance scene = CreateScene();
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
        SceneInstance scene1 = CreateScene();
        SceneInstance scene2 = CreateScene();
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
        SceneInstance scene = CreateScene();
        scene.Add(CreateGameObject("A"));
        scene.Add(CreateGameObject("B"));

        scene.Clear();

        Assert.True(scene.IsEmpty);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void Flush_DropsDisposedObjects()
    {
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Contains(go, scene.FindObjectsOfType<GameObject>());
        Assert.Contains(comp, scene.FindObjectsOfType<PlainComponent>());
    }

    [Fact]
    public void FindObjectByID_FindsGameObjectAndComponent()
    {
        SceneInstance scene = CreateScene();
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
        SceneInstance scene = CreateScene();
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
        Assert.NotNull(SceneInstance.Main);
        Assert.False(SceneInstance.Main.IsDisposed);
    }

    [Fact]
    public void Main_IsNeverMadeOnTheFly_OnceItsWorldIsGone()
    {
        SceneInstance.Main.Dispose();

        Assert.False(SceneInstance.HasMain);
        Assert.Throws<InvalidOperationException>(() => SceneInstance.Main);
        Assert.Throws<InvalidOperationException>(() => SceneInstance.Active);
    }

    [Fact]
    public void BindingMain_RaisesMainChanged_InsideTheNewWorld()
    {
        SceneInstance previous = SceneInstance.Main;
        SceneInstance next = CreateScene(enable: true);
        (SceneInstance? From, SceneInstance? To, SceneInstance? Active) seen = default;
        void Handler(SceneInstance? from, SceneInstance? to) => seen = (from, to, SceneInstance.Active);
        SceneInstance.MainChanged += Handler;
        try { SceneInstance.Main = next; }
        finally { SceneInstance.MainChanged -= Handler; }

        Assert.Equal((previous, next, next), seen);
    }

    private sealed class GateProbe : Component
    {
        public bool EnableSawPlaying, DisableSawPlaying;
        public override void OnEnable() => EnableSawPlaying = GameObject.Scene!.IsPlaying;
        public override void OnDisable() => DisableSawPlaying = GameObject.Scene!.IsPlaying;
    }

    // Running is set before OnEnable and Dormant after OnDisable, so the gameplay gate passes for both.
    [Fact]
    public void ActivateAndDeactivate_DeliverLifecycle_WhileTheWorldCountsAsPlaying()
    {
        SceneInstance world = CreateScene();
        GameObject go = CreateGameObject();
        GateProbe probe = go.AddComponent<GateProbe>();
        world.Add(go);
        Assert.False(world.IsPlaying);

        world.Activate();
        Assert.True(probe.EnableSawPlaying);
        Assert.True(world.IsPlaying);

        world.Deactivate();
        Assert.True(probe.DisableSawPlaying);
        Assert.Equal(SceneState.Dormant, world.State);
        Assert.False(world.IsPlaying);
    }

    [Fact]
    public void EditAndPreviewWorlds_NeverPlay()
    {
        SceneInstance edit = new(SceneMode.Edit);
        SceneInstance preview = new(SceneMode.Preview);
        GameObject go = CreateGameObject();
        GateProbe probe = go.AddComponent<GateProbe>();
        edit.Add(go);
        edit.Activate();
        preview.Activate();

        Assert.False(edit.IsPlaying);
        Assert.False(preview.IsPlaying);
        Assert.False(probe.EnableSawPlaying);
        edit.Dispose();
        preview.Dispose();
    }

    [Fact]
    public void Destroy_GoesThroughDestroying_StillPlayingForOnDisable()
    {
        SceneInstance world = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        GateProbe probe = go.AddComponent<GateProbe>();
        world.Add(go);
        SceneState? raisedIn = null;
        void Handler(SceneInstance w) => raisedIn = w.State;
        SceneInstance.Destroying += Handler;
        try { world.Dispose(); }
        finally { SceneInstance.Destroying -= Handler; }

        Assert.Equal(SceneState.Destroying, raisedIn);
        Assert.True(probe.DisableSawPlaying);
        Assert.Equal(SceneState.Destroyed, world.State);
        Assert.DoesNotContain(world, SceneInstance.All);
        Assert.ThrowsAny<InvalidOperationException>(world.Activate);
    }

    [Fact]
    public void Load_SetsCurrent_EnablesScene_FiresEvent()
    {
        SceneInstance scene = CreateScene();
        bool fired = false;
        void handler() => fired = true;
        SceneInstance.OnSceneLoaded += handler;
        try
        {
            SceneInstance.Load(scene);
            SceneInstance.ProcessPendingLoad();

            Assert.Same(scene, SceneInstance.Main);
            Assert.Equal(SceneState.Running, scene.State);
            Assert.True(fired);
        }
        finally
        {
            SceneInstance.OnSceneLoaded -= handler;
        }
    }

    [Fact]
    public void Load_QueuedUntilProcessed()
    {
        SceneInstance before = SceneInstance.Main;
        SceneInstance scene = CreateScene();

        SceneInstance.Load(scene);

        Assert.Same(before, SceneInstance.Main);
        Assert.NotEqual(SceneState.Running, scene.State);

        SceneInstance.ProcessPendingLoad();

        Assert.Same(scene, SceneInstance.Main);
    }

    [Fact]
    public void Load_ReplacingCurrent_DisposesPrevious()
    {
        SceneInstance first = CreateScene();
        SceneInstance second = CreateScene();

        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();

        SceneInstance.Load(second);

        // The outgoing scene stays usable until the swap actually applies.
        Assert.Same(first, SceneInstance.Main);
        Assert.False(first.IsDisposed);

        SceneInstance.ProcessPendingLoad();

        Assert.Same(second, SceneInstance.Main);
        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void Load_LastRequestOfTheFrameWins()
    {
        SceneInstance first = CreateScene();
        SceneInstance second = CreateScene();

        SceneInstance.Load(first);
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad();

        Assert.Same(second, SceneInstance.Main);
        Assert.True(first.IsDisposed); // replaced before it loaded, so nothing else would ever free it
    }

    // Loading the scene that is already current used to dispose it and then enable the corpse.
    [Fact]
    public void Load_TheCurrentScene_IsANoOp()
    {
        SceneInstance current = SceneInstance.Main;

        SceneInstance.Load(current);
        SceneInstance.ProcessPendingLoad();

        Assert.Same(current, SceneInstance.Main);
        Assert.False(current.IsDisposed);
        Assert.Equal(SceneState.Running, current.State);
    }

    [Fact]
    public void Load_AnAlreadyEnabledScene_DoesNotThrow()
    {
        SceneInstance scene = CreateScene(enable: true);

        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();

        Assert.Same(scene, SceneInstance.Main);
        Assert.Equal(SceneState.Running, scene.State);
    }

    [Fact]
    public void EnableAndDisable_AreIdempotent()
    {
        SceneInstance scene = CreateScene();

        scene.Activate();
        scene.Activate();
        Assert.Equal(SceneState.Running, scene.State);

        scene.Deactivate();
        scene.Deactivate();
        Assert.NotEqual(SceneState.Running, scene.State);
    }

    // A component disposing its own scene mid-callback used to blow up on the trailing Flush().
    [Fact]
    public void FrameCallbacks_OnASceneDisposedMidCallback_DoNotThrow()
    {
        SceneInstance scene = CreateScene(enable: true);
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
        SceneInstance scene = CreateScene(enable: true);
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
        SceneInstance first = CreateScene();
        GameObject keeper = CreateGameObject("Keeper");
        first.Add(keeper);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();

        SceneInstance second = CreateScene();
        first.Remove(keeper);
        second.Add(keeper);
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad();

        Assert.False(keeper.IsDisposed);
        Assert.Same(second, keeper.Scene);
        Assert.Contains(keeper, second.AllObjects);
    }

    // The trap: after Load(), Scene.Current is still the outgoing scene, so adding there hands the
    // object to the scene that is about to be disposed.
    [Fact]
    public void ManualPreserve_AddingBackToSceneCurrentAfterLoad_LosesTheObject()
    {
        SceneInstance first = CreateScene();
        GameObject keeper = CreateGameObject("Keeper");
        first.Add(keeper);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();

        SceneInstance second = CreateScene();
        first.Remove(keeper);
        SceneInstance.Load(second);
        SceneInstance.Main.Add(keeper); // still `first` at this point
        SceneInstance.ProcessPendingLoad();

        Assert.True(keeper.IsDisposed, "Scene.Current is only the new scene once the swap applies.");
    }

    [Fact]
    public void DontDestroyOnLoad_SurvivesTheLoad_AndJoinsTheNewScene()
    {
        SceneInstance first = CreateScene();
        GameObject keeper = CreateGameObject("Keeper");
        GameObject doomed = CreateGameObject("Doomed");
        first.Add(keeper);
        first.Add(doomed);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();

        SceneInstance.DontDestroyOnLoad(keeper);

        SceneInstance second = CreateScene();
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad();

        Assert.False(keeper.IsDisposed);
        Assert.Same(second, keeper.Scene);
        Assert.Contains(keeper, second.AllObjects);
        Assert.True(doomed.IsDisposed, "Anything not preserved goes with the old scene.");
    }

    [Fact]
    public void DontDestroyOnLoad_KeepsTicking_InTheNewScene()
    {
        SceneInstance first = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        first.Add(keeper);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(keeper);

        Update(first);
        Assert.Equal(1, comp.Updates);

        SceneInstance second = CreateScene();
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad();

        Update(second);
        Assert.Equal(2, comp.Updates); // re-registered with the new scene's dispatcher
    }

    [Fact]
    public void DontDestroyOnLoad_MovesSceneRegistrations_ToTheNewScene()
    {
        SceneInstance first = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        GameCanvas canvas = keeper.AddComponent<GameCanvas>();
        first.Add(keeper);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(keeper);
        Assert.Equal([canvas], first.Canvases);

        SceneInstance second = CreateScene();
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad();

        Assert.Equal([canvas], second.Canvases);
    }

    [Fact]
    public void DontDestroyOnLoad_DoesNotRestartTheObject()
    {
        SceneInstance first = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        first.Add(keeper);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(keeper);
        int enables = comp.Enables;

        SceneInstance.Load(CreateScene());
        SceneInstance.ProcessPendingLoad();

        Assert.Equal(enables, comp.Enables);
        Assert.Equal(0, comp.Disables);
    }

    [Fact]
    public void DontDestroyOnLoad_OnAChild_PreservesItsRootInstead()
    {
        SceneInstance first = CreateScene();
        GameObject root = CreateGameObject("Root");
        GameObject child = CreateGameObject("Child");
        child.SetParent(root);
        first.Add(root);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();

        SceneInstance.DontDestroyOnLoad(child);

        SceneInstance second = CreateScene();
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad();

        Assert.False(root.IsDisposed);
        Assert.False(child.IsDisposed);
        Assert.Same(second, root.Scene);
        Assert.Same(second, child.Scene);
        Assert.Same(root, child.Parent);
    }

    [Fact]
    public void CancelDontDestroyOnLoad_LetsItDieWithTheScene()
    {
        SceneInstance first = CreateScene();
        GameObject go = CreateGameObject();
        first.Add(go);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();

        SceneInstance.DontDestroyOnLoad(go);
        SceneInstance.CancelDontDestroyOnLoad(go);

        SceneInstance.Load(CreateScene());
        SceneInstance.ProcessPendingLoad();

        Assert.True(go.IsDisposed);
    }

    [Fact]
    public void DontDestroyOnLoad_ADestroyedObject_IsDroppedNotResurrected()
    {
        SceneInstance first = CreateScene();
        GameObject go = CreateGameObject();
        first.Add(go);
        SceneInstance.Load(first);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(go);

        go.Dispose();

        SceneInstance second = CreateScene();
        SceneInstance.Load(second);
        SceneInstance.ProcessPendingLoad(); // must not throw or re-add the corpse

        Assert.Empty(second.AllObjects);
    }

    // Leaving play mode ends the preservation session. Without this the objects a play session kept
    // alive would ride the swap into the authoring scene the editor restores behind it, and stay there.
    [Fact]
    public void DestroyPreserved_KeepsThemOutOfTheNextScene()
    {
        SceneInstance play = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        play.Add(keeper);
        SceneInstance.Load(play);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(keeper);

        SceneInstance.DestroyPreserved();

        // Same order the game loop uses: the destroy queue drains, then the scene swap applies.
        SceneInstance restored = CreateScene();
        SceneInstance.Load(restored);
        EngineObject.ProcessDestroyed();
        SceneInstance.ProcessPendingLoad();

        Assert.True(keeper.IsDisposed);
        Assert.Empty(restored.AllObjects);
    }

    // Queued like any other Destroy, so teardown lands at the end of the frame rather than under
    // whatever was running when the play button was clicked.
    [Fact]
    public void DestroyPreserved_TearsDownAtTheEndOfTheFrame()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        scene.Add(keeper);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(keeper);

        SceneInstance.DestroyPreserved();

        Assert.False(keeper.IsDisposed);
        Assert.Equal(0, comp.Disables);

        EngineObject.ProcessDestroyed();

        Assert.True(keeper.IsDisposed);
        Assert.Equal(1, comp.Disables); // torn down properly, not just dropped from the registry
    }

    [Fact]
    public void Shutdown_DestroysPreservedObjects()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject keeper = CreateGameObject("Keeper");
        scene.Add(keeper);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        SceneInstance.DontDestroyOnLoad(keeper);

        SceneInstance.Shutdown();

        // No frame follows a shutdown, so nothing would drain a destroy queue: teardown is immediate.
        Assert.True(keeper.IsDisposed);
        Assert.True(scene.IsDisposed);

        // And the registry is empty, so the next run does not inherit the last one's objects.
        SceneInstance next = CreateScene();
        SceneInstance.Load(next);
        SceneInstance.ProcessPendingLoad();
        Assert.Empty(next.AllObjects);
    }

    [Fact]
    public void Load_SkipsASceneDisposedBeforeItApplied()
    {
        SceneInstance current = CreateScene();
        SceneInstance queued = CreateScene();

        SceneInstance.Load(current);
        SceneInstance.ProcessPendingLoad();

        SceneInstance.Load(queued);
        queued.Dispose();
        SceneInstance.ProcessPendingLoad();

        Assert.Same(current, SceneInstance.Main);
        Assert.False(current.IsDisposed);
    }

    // ---- The ambient world ----

    private sealed class ActiveProbe : Component
    {
        public SceneInstance? AtEnable, AtStart, AtDispose;
        public override void OnEnable() => AtEnable = SceneInstance.Active;
        public override void Start() => AtStart = SceneInstance.Active;
        protected override void OnDispose() { AtDispose = SceneInstance.Active; base.OnDispose(); }
    }

    // Code in one scene enabling an object in another runs that object's callbacks in its own scene.
    [Fact]
    public void Active_IsTheComponentsScene_WhenAnotherScenesCodeEnablesIt()
    {
        SceneInstance a = CreateScene(enable: true);
        SceneInstance b = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        ActiveProbe probe = go.AddComponent<ActiveProbe>();

        using (a.Enter())
            b.Add(go);

        Assert.Same(b, probe.AtEnable);
    }

    [Fact]
    public void Active_FlowsThroughAwait_AndScopesNest()
    {
        SceneInstance a = CreateScene(enable: true);
        SceneInstance b = CreateScene(enable: true);

        SceneInstance? afterAwait;
        using (a.Enter())
        {
            using (b.Enter())
            {
                afterAwait = AfterYield().GetAwaiter().GetResult();
                Assert.Same(b, SceneInstance.Active);
            }
            Assert.Same(a, SceneInstance.Active);
        }

        Assert.Same(b, afterAwait);

        static async Task<SceneInstance> AfterYield()
        {
            await Task.Delay(1).ConfigureAwait(false);
            return SceneInstance.Active;
        }
    }

    [Fact]
    public void SceneLoadedHandlers_RunInTheLoadedScene()
    {
        SceneInstance loaded = CreateScene();
        SceneInstance? seen = null;
        void Handler() => seen = SceneInstance.Active;
        SceneInstance.OnSceneLoaded += Handler;
        try
        {
            SceneInstance.Load(loaded);
            SceneInstance.ProcessPendingLoad();
        }
        finally { SceneInstance.OnSceneLoaded -= Handler; }

        Assert.Same(loaded, seen);
    }

    // A scene stays Active for its own teardown, and code still holding it afterwards gets a clear error.
    [Fact]
    public void Active_IsTheSceneDuringItsTeardown_AndThrowsOnceItIsDestroyed()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        ActiveProbe probe = go.AddComponent<ActiveProbe>();
        scene.Add(go);

        using (scene.Enter())
        {
            scene.Dispose();
            Assert.Throws<ObjectDisposedException>(() => SceneInstance.Active);
        }

        Assert.Same(scene, probe.AtDispose);
    }

    [Fact]
    public void CameraMain_IsTheActiveScenesCamera_AndANewCameraIsMainAtOnce()
    {
        SceneInstance a = CreateScene(enable: true);
        SceneInstance b = CreateScene(enable: true);
        GameObject camA = CreateGameObject("CamA");
        Camera cameraA = camA.AddComponent<Camera>();
        a.Add(camA);

        using (b.Enter())
        {
            Assert.True(ReferenceEquals(Camera.Main, null));
            GameObject camB = CreateGameObject("CamB");
            Camera cameraB = camB.AddComponent<Camera>();
            b.Add(camB);
            Assert.Same(cameraB, Camera.Main);
        }
        using (a.Enter())
            Assert.Same(cameraA, Camera.Main);
    }

    [Fact]
    public void DebugDraws_LandInTheActiveScene()
    {
        SceneInstance a = CreateScene(enable: true);
        SceneInstance b = CreateScene(enable: true);

        using (b.Enter())
            Debug.DrawLine(Vector.Float3.Zero, Vector.Float3.One, System.Drawing.Color.Red);

        Assert.True(a.Gizmos.IsEmpty);
        Assert.False(b.Gizmos.IsEmpty);
        Debug.ClearGizmos();
        Assert.True(b.Gizmos.IsEmpty);
    }

    // ---- Teardown ----

    private sealed class TeardownLog : Component
    {
        public static readonly List<string> Events = [];
        public string Tag = "";
        public bool ThrowOnDispose;
        public override void OnDisable() => Events.Add($"disable {Tag}");
        public override void OnRemovedFromScene() => Events.Add($"removed {Tag}");
        protected override void OnDispose()
        {
            Events.Add($"dispose {Tag}");
            base.OnDispose();
            if (ThrowOnDispose) throw new InvalidOperationException("teardown test");
        }
    }

    // A scene tears down in three passes, each object guarded, so one throwing OnDispose leaves the rest torn down.
    [Fact]
    public void Teardown_RunsThreePasses_AndContinuesPastAThrowingOnDispose()
    {
        TeardownLog.Events.Clear();
        SceneInstance scene = CreateScene(enable: true);
        GameObject a = CreateGameObject("A");
        TeardownLog first = a.AddComponent<TeardownLog>();
        first.Tag = "A";
        first.ThrowOnDispose = true;
        GameObject b = CreateGameObject("B");
        TeardownLog second = b.AddComponent<TeardownLog>();
        second.Tag = "B";
        scene.Add(a);
        scene.Add(b);

        scene.Dispose();

        Assert.True(a.IsDisposed && b.IsDisposed && first.IsDisposed && second.IsDisposed);
        int lastDisable = Math.Max(TeardownLog.Events.IndexOf("disable A"), TeardownLog.Events.IndexOf("disable B"));
        int firstRemoved = Math.Min(TeardownLog.Events.IndexOf("removed A"), TeardownLog.Events.IndexOf("removed B"));
        int lastRemoved = Math.Max(TeardownLog.Events.IndexOf("removed A"), TeardownLog.Events.IndexOf("removed B"));
        int firstDispose = Math.Min(TeardownLog.Events.IndexOf("dispose A"), TeardownLog.Events.IndexOf("dispose B"));
        Assert.True(lastDisable >= 0 && lastDisable < firstRemoved && lastRemoved < firstDispose, string.Join(", ", TeardownLog.Events));
    }
}
