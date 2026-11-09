// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.UI;
using Prowl.Vector;

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
        Assert.Equal(1, scene.ObjectCount);
        Assert.Contains(go, scene.Objects);
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();

        scene.Add(go);
        scene.Add(go);

        Assert.Equal(1, scene.ObjectCount);
    }

    [Fact]
    public void Add_RegistersChildrenRecursively()
    {
        SceneInstance scene = CreateScene();
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        scene.Add(parent);

        Assert.Equal(2, scene.ObjectCount);
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
        Assert.Equal(0, scene.ObjectCount);
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

        Assert.Equal(0, scene.ObjectCount);
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
        Assert.DoesNotContain(go, scene1.Objects);
        Assert.Contains(go, scene2.Objects);
    }

    [Fact]
    public void Clear_RemovesAllObjects()
    {
        SceneInstance scene = CreateScene();
        scene.Add(CreateGameObject("A"));
        scene.Add(CreateGameObject("B"));

        scene.Clear();

        Assert.Equal(0, scene.ObjectCount);
        Assert.Equal(0, scene.ObjectCount);
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
        Assert.Equal(1, scene.ObjectCount);
        Assert.DoesNotContain(drop, scene.Objects);
        Assert.NotNull(drop.Scene); // still owned by the scene until flushed

        scene.Flush();

        Assert.Null(drop.Scene); // Flush detached it from the scene
        Assert.Equal(1, scene.ObjectCount);
        Assert.Contains(keep, scene.Objects);
    }

    [Fact]
    public void Count_ExcludesDisposedObjects()
    {
        SceneInstance scene = CreateScene();
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        scene.Add(a);
        scene.Add(b);
        Assert.Equal(2, scene.ObjectCount);

        a.Dispose();

        Assert.Equal(1, scene.ObjectCount);
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

        Assert.Single(scene.Roots);
        Assert.Contains(parent, scene.Roots);
        Assert.DoesNotContain(child, scene.Roots);
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

        var active = scene.Objects.Where(o => o.EnabledInHierarchy).ToList();

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
        Assert.Equal(0, scene.ObjectCount);

        GameObject go = CreateGameObject();
        scene.Add(go);
        Assert.NotEqual(0, scene.ObjectCount);

        scene.Remove(go);
        Assert.Equal(0, scene.ObjectCount);
    }

    // ---- Find ----

    [Fact]
    public void FindObject_FindsGameObjectAndComponent()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Same(go, scene.FindObject<GameObject>(go.Identifier));
        Assert.Same(comp, scene.FindObject<PlainComponent>(comp.Identifier));
        Assert.Null(scene.FindObject(Guid.NewGuid()));
    }

    [Fact]
    public void FindObjectByIdentifier_FindsGameObjectAndComponent()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        scene.Add(go);

        Assert.Same(go, scene.FindObject<GameObject>(go.Identifier));
        Assert.Same(comp, scene.FindObject<PlainComponent>(comp.Identifier));
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

    private interface IProbe { }

    private sealed class Probed : Component, IProbe { }

    private sealed class OtherProbed : Component { }

    // The index is built by the first query and then kept as components join, so later joins and destroyed members
    // show up correctly without a rescan.
    [Fact]
    public void GetAll_TracksComponentsJoiningAndLeaving_ByClassAndInterface()
    {
        SceneInstance world = CreateScene(enable: true);
        GameObject a = CreateGameObject("A");
        Probed first = a.AddComponent<Probed>();
        world.Add(a);
        Assert.Equal([first], world.GetAll<IProbe>());

        GameObject b = CreateGameObject("B");
        b.AddComponent<OtherProbed>();
        world.Add(b);
        Probed second = b.AddComponent<Probed>();
        Assert.Equal([first, second], world.GetAll<Probed>());
        Assert.Equal([first, second], world.GetAll<IProbe>());

        second.Enabled = false;
        Assert.Equal([first], world.GetAll<IProbe>());
        Assert.Equal([first, second], world.GetAll<IProbe>(includeDisabled: true));

        a.Dispose();
        Assert.Equal([second], world.GetAll<Probed>(includeDisabled: true));
        Assert.Null(world.Get<Probed>());

        world.Remove(b);
        Assert.Empty(world.GetAll<Probed>(includeDisabled: true));
    }

    [Fact]
    public void FindObject_ByIdentifier_FollowsIdentifierChangesAndLeaving()
    {
        SceneInstance world = CreateScene();
        GameObject go = CreateGameObject();
        PlainComponent comp = go.AddComponent<PlainComponent>();
        world.Add(go);

        Assert.Same(go, world.FindObject(go.Identifier));
        Assert.True(world.TryFindObject(comp.Identifier, out PlainComponent found));
        Assert.Same(comp, found);

        Guid old = go.Identifier, renewed = Guid.NewGuid();
        go.SetIdentifier(renewed);
        Assert.Null(world.FindObject(old));
        Assert.Same(go, world.FindObject(renewed));

        world.Remove(go);
        Assert.Null(world.FindObject(renewed));
    }

    [Fact]
    public void FindObject_ByNamePathAndTag()
    {
        SceneInstance world = CreateScene();
        GameObject level = CreateGameObject("Level");
        GameObject props = CreateGameObject("Props");
        GameObject door = CreateGameObject("Door");
        door.Tag = "Player";
        props.SetParent(level);
        door.SetParent(props);
        world.Add(level);

        Assert.Same(door, world.FindObject("Door"));
        Assert.Same(door, world.FindObjectByPath("Level/Props/Door"));
        Assert.Null(world.FindObjectByPath("Level/Door"));
        Assert.Same(door, world.FindObjectWithTag("Player"));
        var tagged = new List<GameObject>();
        world.FindObjectsWithTag("Player", tagged);
        Assert.Equal([door], tagged);
        Assert.Equal([level], world.Roots);
        Assert.Equal(3, world.ObjectCount);
    }

    // ---- Sections ----

    [Fact]
    public void ANewWorld_HasPersistentThenOneActiveSection()
    {
        SceneInstance world = SceneInstance.Create("Level");
        try
        {
            Assert.Equal(2, world.Sections.Count);
            Assert.True(world.Sections[0].IsPersistent);
            Assert.Same(world.Sections[1], world.ActiveSection);
            Assert.Equal("Level", world.ActiveSection.Name);
            Assert.Throws<InvalidOperationException>(() => world.ActiveSection = world.Persistent);
            Assert.Throws<InvalidOperationException>(() => world.Persistent.Capture());
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public void ObjectsJoinTheActiveSection_TheirParentsSection_OrTheirOwners()
    {
        SceneInstance world = CreateScene();
        SceneSection other = world.CreateSection("Other");

        GameObject inActive = world.Create("A");
        GameObject inOther = world.Create(other, "B");
        GameObject child = world.Create("Child", parent: inOther);
        GameObject owned = world.Create("Puppet", owner: inOther.AddComponent<PlainComponent>());

        Assert.Same(world.ActiveSection, inActive.Section);
        Assert.Same(other, inOther.Section);
        Assert.Same(other, child.Section);
        Assert.Same(other, owned.Section);
        Assert.Equal([inActive], world.ActiveSection.Roots);
        Assert.Equal([inOther, owned], other.Roots);
        Assert.Equal([inActive, inOther, owned], world.Roots);
    }

    [Fact]
    public void SectionsOwnTheirSettings_AndTheWorldShowsTheActiveOnes()
    {
        SceneInstance world = CreateScene();
        SceneSection other = world.CreateSection("Other");
        other.Lighting.LightmapScale = 5f;

        Assert.NotEqual(5f, world.Lighting.LightmapScale);
        world.ActiveSection = other;
        Assert.Equal(5f, world.Lighting.LightmapScale);
        Assert.Same(other.Environment, world.Environment);
    }

    [Fact]
    public void ReparentingAcrossSections_MovesTheSubtree_AndSetParentNullKeepsTheSection()
    {
        SceneInstance world = CreateScene();
        SceneSection other = world.CreateSection("Other");
        GameObject anchor = world.Create(other, "Anchor");
        GameObject mover = world.Create("Mover");
        GameObject moverChild = world.Create("MoverChild", parent: mover);

        mover.SetParent(anchor);
        Assert.Same(other, moverChild.Section);
        Assert.DoesNotContain(mover, world.ActiveSection.Roots);

        mover.SetParent(null!);
        Assert.Same(other, mover.Section);
        Assert.Equal([anchor, mover], other.Roots);
    }

    [Fact]
    public void Capture_WritesOneSection_AndSavesReferencesIntoAnotherAsNull()
    {
        SceneInstance world = CreateScene();
        SceneSection other = world.CreateSection("Other");
        GameObject target = world.Create(other, "TargetInOther");
        GameObject holder = world.Create("Holder");
        holder.AddComponent<SectionLink>().Target = target;

        SceneContent content = world.ActiveSection.Capture();

        GameObject saved = Assert.Single(content.Objects);
        Assert.Equal("Holder", saved.Name);
        Assert.Null(saved.GetComponent<SectionLink>()!.Target);
    }

    private sealed class SectionLink : Component
    {
        public GameObject? Target;
    }

    [Fact]
    public void MovingIntoASectionThatHoldsTheIdentifier_GivesAFreshOne()
    {
        SceneInstance world = CreateScene();
        SceneSection other = world.CreateSection("Other");
        GameObject first = world.Create(other, "First");
        GameObject second = world.Create("Second");
        second.SetIdentifier(first.Identifier);

        second.MoveTo(other);

        Assert.NotEqual(first.Identifier, second.Identifier);
        Assert.Same(first, world.FindObject(other, first.Identifier));
    }

    [Fact]
    public void SiblingIndex_OfARoot_IsItsIndexAmongItsSectionsRoots()
    {
        SceneInstance world = CreateScene();
        GameObject a = world.Create("A");
        GameObject b = world.Create("B");

        b.SiblingIndex = 0;

        Assert.Equal([b, a], world.ActiveSection.Roots);
        Assert.Equal(1, a.SiblingIndex);
    }

    [Fact]
    public void ADestroyingWorld_RefusesNewObjects()
    {
        SceneInstance world = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        SpawnOnDisable spawner = go.AddComponent<SpawnOnDisable>();
        world.Add(go);

        world.Dispose();

        Assert.True(spawner.Refused);
    }

    private sealed class SpawnOnDisable : Component
    {
        public bool Refused;
        public override void OnDisable()
        {
            try { GameObject.Scene!.Create("Late"); }
            catch (InvalidOperationException) { Refused = true; }
        }
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
    public void Destroy_UnloadsEverySection()
    {
        SceneInstance world = CreateScene(enable: true);
        SceneSection level = world.ActiveSection;
        List<SceneSection> unloading = [];
        void Handler(SceneSection s) => unloading.Add(s);
        SceneInstance.Unloading += Handler;
        try { world.Dispose(); }
        finally { SceneInstance.Unloading -= Handler; }

        Assert.False(level.IsLoaded);
        Assert.True(level.UnloadToken.IsCancellationRequested);
        Assert.Contains(level, unloading);
        Assert.Throws<InvalidOperationException>(() => new GameObject("Late").MoveTo(level));
    }

    private sealed class RaycastOnDisable : Component
    {
        public bool Hit;
        public override void OnDisable() => Hit = Physics.Raycast(new Float3(0, 0, -5), new Float3(0, 0, 1), 10f);
    }

    [Fact]
    public void Destroy_KeepsPhysicsUntilEverythingIsDisabled()
    {
        SceneInstance world = CreateScene(enable: true);
        RaycastOnDisable probe = world.Create("Probe").AddComponent<RaycastOnDisable>();
        world.Create("Wall").AddComponent<BoxCollider>();
        Tick(world, 1);

        world.Dispose();

        Assert.True(probe.Hit);
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

    // What a Load does at the safe point, without a stored scene.
    private static void LoadEmpty(SceneInstance world) => world.ReplaceContent(new SceneContent(), "Next", null);

    [Fact]
    public void Persistent_SurvivesALoad_WithoutRestarting_AndKeepsItsRegistrations()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject keeper = main.Create("Keeper");
        TickCounter comp = keeper.AddComponent<TickCounter>();
        GameCanvas canvas = keeper.AddComponent<GameCanvas>();
        GameObject doomed = main.Create("Doomed");
        keeper.Persistent = true;
        Update(main);

        LoadEmpty(main);
        Update(main);

        Assert.False(keeper.IsDisposed);
        Assert.True(doomed.IsDisposed, "Anything not persistent goes with the old level.");
        Assert.Equal(2, comp.Updates);
        Assert.Equal(1, comp.Enables);
        Assert.Equal(0, comp.Disables);
        Assert.Equal([canvas], main.Canvases);
    }

    [Fact]
    public void Persistent_IsSetOnARoot_AndReadOnAChildForItsRoot()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject root = main.Create("Root");
        GameObject child = main.Create("Child", root);

        Assert.Throws<InvalidOperationException>(() => child.Persistent = true);
        root.Persistent = true;
        Assert.True(child.Persistent);
        Assert.Same(main.Persistent, child.Section);

        LoadEmpty(main);

        Assert.False(root.IsDisposed);
        Assert.False(child.IsDisposed);
        Assert.Same(root, child.Parent);
    }

    [Fact]
    public void Persistent_SetBackToFalse_LetsItGoWithTheLevel()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject go = main.Create("Go");

        go.Persistent = true;
        go.Persistent = false;
        LoadEmpty(main);

        Assert.True(go.IsDisposed);
    }

    [Fact]
    public void Persistent_ADestroyedObject_IsDroppedNotResurrected()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject go = main.Create("Go");
        go.Persistent = true;

        go.Dispose();
        LoadEmpty(main);

        Assert.DoesNotContain(go, main.Objects);
    }

    [Fact]
    public void Persistent_IsRefusedOutsideAGameWorld()
    {
        var edit = new SceneInstance(SceneMode.Edit);
        GameObject go = CreateGameObject();
        edit.Add(go);

        Assert.Throws<InvalidOperationException>(() => go.Persistent = true);
        edit.Dispose();
    }

    [Fact]
    public void Shutdown_DestroysPersistentObjects()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject keeper = main.Create("Keeper");
        keeper.Persistent = true;

        SceneInstance.Shutdown();

        // No frame follows a shutdown, so nothing would drain a destroy queue: teardown is immediate.
        Assert.True(keeper.IsDisposed);
        Assert.True(main.IsDisposed);
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

    // Each world keeps its own registries, and a dormant world's are empty.
    [Fact]
    public void Registries_ArePerWorld_AndEmptyWhileDormant()
    {
        SceneInstance first = CreateScene(enable: true);
        SceneInstance second = CreateScene(enable: true);
        WindZone zone = first.Create("Wind").AddComponent<WindZone>();
        Volume volume = first.Create("Volume").AddComponent<Volume>();
        EventSystem events = second.Create("Events").AddComponent<EventSystem>();
        Camera camera = second.Create("Camera").AddComponent<Camera>();

        Assert.Equal([zone], first.WindZones);
        Assert.Empty(second.WindZones);
        Assert.Equal([volume], first.Volumes);
        Assert.Empty(second.Volumes);
        Assert.Same(events, second.Input.EventSystem);
        Assert.Null(first.Input.EventSystem);
        Assert.Same(camera, second.MainCamera);
        Assert.Null(first.MainCamera);

        first.Deactivate();
        Assert.Empty(first.WindZones);
        Assert.Empty(first.Volumes);

        first.Activate();
        Assert.Equal([zone], first.WindZones);
    }

    // A component's Scene is its world, and reading it with none throws instead of handing back null.
    [Fact]
    public void ComponentScene_IsItsWorld_AndThrowsWhileDetached()
    {
        GameObject go = CreateGameObject("Loose");
        TickCounter comp = go.AddComponent<TickCounter>();

        Assert.False(comp.IsInWorld);
        Assert.Throws<InvalidOperationException>(() => comp.Scene);

        SceneInstance world = CreateScene(enable: true);
        go.MoveTo(world);

        Assert.True(comp.IsInWorld);
        Assert.Same(world, comp.Scene);
    }
}
