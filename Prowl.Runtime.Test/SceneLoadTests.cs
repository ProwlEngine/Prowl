// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Load handles, in world loading, additive loads, unloads and their ordering.</summary>
public class SceneLoadTests : RuntimeTestBase
{
    private readonly MemoryAssetBackend _backend = new();
    private readonly AssetBackend? _previous = AssetDatabase.Backend;

    public SceneLoadTests()
    {
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = _backend;
    }

    public override void Dispose()
    {
        base.Dispose();
        AssetDatabase.Backend = _previous;
        AssetDatabase.ClearForTests();
    }

    private sealed class Counted : Component
    {
        public int Enables, Disables, Removals;
        public override void OnEnable() => Enables++;
        public override void OnDisable() => Disables++;
        public override void OnRemovedFromScene() => Removals++;
    }

    // A stored scene holding one root of each name.
    private Scene Stored(string name, params string[] roots)
    {
        var authoring = new SceneInstance();
        foreach (string root in roots)
            authoring.Create(root).AddComponent<Counted>();
        var stored = new Scene { Data = authoring.ActiveSection.CaptureTree() };
        authoring.Dispose();
        return AssetDatabase.Get<Scene>(_backend.Add(stored, name + ".scene"))!;
    }

    private static GameObject Only(SceneSection section) => Assert.Single(section.Roots);

    private sealed class KeepOnDisable : Component
    {
        public int Enables;
        public override void OnEnable() => Enables++;
        public override void OnDisable() { if (GameObject.Parent.IsNotValid()) GameObject.Persistent = true; }
    }

    [Fact]
    public void Load_ReplacesEverythingButPersistent_AndMakesTheLevelActive()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject old = main.Create("Old");
        Counted oldCounted = old.AddComponent<Counted>();
        GameObject keeper = main.Create("Keeper");
        keeper.Persistent = true;

        SceneLoad load = Stored("Level", "Door").Load();
        load.Wait();

        Assert.Equal(SceneLoadPhase.Done, load.Phase);
        Assert.True(old.IsDisposed);
        Assert.Equal(1, oldCounted.Disables);
        Assert.Equal(1, oldCounted.Removals);
        Assert.False(keeper.IsDisposed);
        Assert.Same(load.Section, main.ActiveSection);
        Assert.Equal("Door", Only(load.Section!).Name);
        Assert.Equal(1, Only(load.Section!).GetComponent<Counted>()!.Enables);
        Assert.Equal([main.Persistent, load.Section], main.Sections);
        Assert.True(load.GetAwaiter().IsCompleted);
    }

    [Fact]
    public void MadePersistentWhileALoadDisablesIt_TheObjectSurvivesEnabled()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject keeper = main.Create("Keeper");
        KeepOnDisable keep = keeper.AddComponent<KeepOnDisable>();

        Stored("Level", "Door").Load().Wait();

        Assert.False(keeper.IsDisposed);
        Assert.True(keeper.Persistent);
        Assert.Equal(2, keep.Enables);
        Assert.Contains(keeper, main.Persistent.Roots);
    }

    [Fact]
    public void SceneBuildFind_MatchesANameOrTheEndOfAPath()
    {
        Scene forest = Stored("Levels/Forest", "f");
        Scene other = Stored("Old/Forest", "o");
        SceneBuild.Set([forest, other]);
        try
        {
            Assert.Same(forest, SceneBuild.Find("Forest"));
            Assert.Same(other, SceneBuild.Find("Old/Forest"));
            Assert.Same(other, SceneBuild.Find("old/forest.scene"));
            Assert.Null(SceneBuild.Find("ld/Forest"));
        }
        finally { SceneBuild.Set([]); }
    }

    [Fact]
    public void LoadAdditive_AddsASection_AndLeavesTheActiveOneAlone()
    {
        SceneInstance main = SceneInstance.Main;
        SceneSection before = main.ActiveSection;

        SceneLoad load = Stored("Extra", "Lamp").LoadAdditive();
        load.Wait();

        Assert.Same(before, main.ActiveSection);
        Assert.Equal([main.Persistent, before, load.Section], main.Sections);
    }

    [Fact]
    public void Open_MakesANewDormantWorld_WithTheLevelActive()
    {
        SceneLoad load = Stored("Menu", "Button").Open();
        load.Wait();

        SceneInstance world = load.Section!.Instance;
        try
        {
            Assert.NotSame(SceneInstance.Main, world);
            Assert.Equal(SceneState.Dormant, world.State);
            Assert.Same(load.Section, world.ActiveSection);
            Assert.Equal(0, Only(load.Section).GetComponent<Counted>()!.Enables);
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public void AMissingScene_FailsTheLoad_AndAwaitingThrows()
    {
        Scene missing = AssetDatabase.Get<Scene>(Guid.NewGuid())!;
        SceneInstance main = SceneInstance.Main;
        GameObject survivor = main.Create("Survivor");

        SceneLoad load = missing.Load();
        load.Wait();

        Assert.Equal(SceneLoadPhase.Failed, load.Phase);
        Assert.IsType<SceneLoadException>(load.Error);
        Assert.ThrowsAny<SceneLoadException>(() => load.GetAwaiter().GetResult());
        Assert.False(survivor.IsDisposed);
    }

    [Fact]
    public void Cancel_CompletesTheHandleAsCancelled()
    {
        SceneLoad load = Stored("Level", "Door").Load(options: new LoadOptions { HoldActivation = true });
        load.Wait();
        Assert.Equal(SceneLoadPhase.WaitingForActivation, load.Phase);

        load.Cancel();
        SceneInstance.ProcessPendingLoad();

        Assert.Equal(SceneLoadPhase.Cancelled, load.Phase);
        Assert.ThrowsAny<OperationCanceledException>(() => load.GetAwaiter().GetResult());
        Assert.Null(SceneInstance.Main.PendingLoad);
    }

    [Fact]
    public void ALoadIntoADestroyedWorld_IsCancelled()
    {
        SceneInstance world = CreateScene(enable: true);
        SceneLoad load = Stored("Level", "Door").Load(world);
        world.Dispose();

        SceneInstance.ProcessPendingLoad();
        load.Wait();

        Assert.Equal(SceneLoadPhase.Cancelled, load.Phase);
    }

    [Fact]
    public void HoldActivation_WaitsUntilAllowed()
    {
        SceneLoad load = Stored("Level", "Door").Load(options: new LoadOptions { HoldActivation = true });
        load.Wait();
        SceneInstance.ProcessPendingLoad();
        Assert.Equal(SceneLoadPhase.WaitingForActivation, load.Phase);
        Assert.Same(load, SceneInstance.Main.PendingLoad);

        load.AllowActivation = true;
        SceneInstance.ProcessPendingLoad();

        Assert.Equal(SceneLoadPhase.Done, load.Phase);
    }

    // A second Load cancels the first and every additive load queued before it, additive loads after it apply after it.
    [Fact]
    public void ASecondLoad_CancelsTheFirstAndTheAdditivesBeforeIt()
    {
        SceneLoad first = Stored("A", "a").Load();
        SceneLoad additiveBefore = Stored("B", "b").LoadAdditive();
        SceneLoad second = Stored("C", "c").Load();
        SceneLoad additiveAfter = Stored("D", "d").LoadAdditive();

        additiveAfter.Wait();
        second.Wait();
        SceneInstance.ProcessPendingLoad();

        Assert.Equal(SceneLoadPhase.Cancelled, first.Phase);
        Assert.Equal(SceneLoadPhase.Cancelled, additiveBefore.Phase);
        Assert.Equal(SceneLoadPhase.Done, second.Phase);
        Assert.Equal(SceneLoadPhase.Done, additiveAfter.Phase);
        Assert.Equal([SceneInstance.Main.Persistent, second.Section, additiveAfter.Section], SceneInstance.Main.Sections);
    }

    [Fact]
    public void UnloadingTheActiveSection_MakesTheNextOneActive_AndCancelsItsToken()
    {
        SceneInstance main = SceneInstance.Main;
        SceneSection first = main.ActiveSection;
        SceneLoad extra = Stored("Extra", "Lamp").LoadAdditive();
        extra.Wait();
        CancellationToken token = first.UnloadToken;

        first.Unload();
        SceneInstance.ProcessPendingLoad();

        Assert.False(first.IsLoaded);
        Assert.True(token.IsCancellationRequested);
        Assert.Same(extra.Section, main.ActiveSection);
    }

    [Fact]
    public void TheLastSection_CannotBeUnloaded_CountingQueuedUnloads()
    {
        SceneInstance main = SceneInstance.Main;
        SceneLoad extra = Stored("Extra", "Lamp").LoadAdditive();
        extra.Wait();

        main.ActiveSection.Unload();

        Assert.Throws<InvalidOperationException>(() => extra.Section!.Unload());
        Assert.Throws<InvalidOperationException>(() => main.Persistent.Unload());
    }

    [Fact]
    public void ObjectsMadeIntoAnUnloadingSection_GoWithIt()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject spawner = main.Create("Spawner");
        spawner.AddComponent<SpawnsOnDisable>();

        Stored("Level", "Door").Load().Wait();

        Assert.DoesNotContain(main.Objects, go => go.Name == "Effect");
    }

    private sealed class SpawnsOnDisable : Component
    {
        public override void OnDisable() => GameObject.Scene!.Create("Effect");
    }

    // A Load from a callback during activation waits for the next safe point instead of running inside this one.
    [Fact]
    public void ALoadFromAnActivation_RunsAtTheNextSafePoint()
    {
        Scene next = Stored("Next", "n");
        SceneLoad? queued = null;
        void Handler(SceneLoadedArgs args) { if (queued == null) queued = next.Load(); }
        SceneInstance.Loaded += Handler;
        SceneLoad first;
        try
        {
            first = Stored("First", "f").Load();
            first.Wait();
        }
        finally { SceneInstance.Loaded -= Handler; }

        Assert.Equal(SceneLoadPhase.Done, first.Phase);
        Assert.NotEqual(SceneLoadPhase.Done, queued!.Phase);

        queued.Wait();
        Assert.Equal(SceneLoadPhase.Done, queued.Phase);
        Assert.Equal("n", Assert.Single(SceneInstance.Main.ActiveSection.Roots).Name);
    }

    [Fact]
    public void WaitingOnTheLoadBeingActivated_LeavesItsActivationAlone()
    {
        SceneLoad? first = null;
        void Handler(SceneLoadedArgs args) => first!.Wait();
        SceneInstance.Loaded += Handler;
        try
        {
            first = Stored("First", "f").Load();
            first.Wait();
        }
        finally { SceneInstance.Loaded -= Handler; }

        Assert.Equal(SceneLoadPhase.Done, first.Phase);
        Assert.Null(first.Error);
        Assert.Equal("f", Assert.Single(SceneInstance.Main.ActiveSection.Roots).Name);
    }

    [Fact]
    public void CancellingTheLoadBeingActivated_LeavesItDone()
    {
        SceneLoad? first = null;
        void Handler(SceneLoadedArgs args) => first!.Cancel();
        SceneInstance.Loaded += Handler;
        try
        {
            first = Stored("First", "f").Load();
            first.Wait();
        }
        finally { SceneInstance.Loaded -= Handler; }

        Assert.Equal(SceneLoadPhase.Done, first.Phase);
        Assert.Same(first.Section, first.GetAwaiter().GetResult());
        Assert.Equal("f", Assert.Single(SceneInstance.Main.ActiveSection.Roots).Name);
    }

    [Fact]
    public void Loaded_FiresInsideTheWorld_WithTheSection()
    {
        SceneLoadedArgs? seen = null;
        SceneInstance? active = null;
        void Handler(SceneLoadedArgs args) { seen = args; active = SceneInstance.Active; }
        SceneInstance.Loaded += Handler;
        try { Stored("Level", "Door").Load().Wait(); }
        finally { SceneInstance.Loaded -= Handler; }

        Assert.Same(SceneInstance.Main.ActiveSection, seen!.Value.Section);
        Assert.False(seen.Value.Additive);
        Assert.Same(SceneInstance.Main, active);
    }

    // A stored scene with five probes around a point, tetrahedralized as a bake stores them.
    private Scene StoredWithProbes(string name, Float3 at)
    {
        var authoring = new SceneInstance();
        authoring.Create(name + "Root");
        Float3[] positions = [at, at + Float3.UnitX, at + Float3.UnitY, at + Float3.UnitZ, at + new Float3(1f, 1f, 1f)];
        ProbeTetrahedralizer.Result tet = ProbeTetrahedralizer.Build(positions);
        SceneLighting lighting = authoring.ActiveSection.Lighting;
        lighting.ProbePositions = positions;
        lighting.ProbeSH = new SphericalHarmonicsL2[positions.Length];
        lighting.ProbeTetrahedra = tet.Tetrahedra;
        lighting.ProbeTetNeighbours = tet.Neighbours;
        var stored = new Scene { Data = authoring.ActiveSection.CaptureTree() };
        authoring.Dispose();
        return AssetDatabase.Get<Scene>(_backend.Add(stored, name + ".scene"))!;
    }

    [Fact]
    public void ProbesInOneSection_AreUsedAsStored_WithoutARebuild()
    {
        SceneLoad load = StoredWithProbes("Lit", Float3.Zero).Load();
        load.Wait();
        SceneInstance main = SceneInstance.Main;

        Assert.Null(main.ProbeRebuild);
        Assert.Same(load.Section!.Lighting.ProbeTetrahedra, main.Probes!.Tetrahedra);
    }

    [Fact]
    public void ProbesInTwoSections_MergeIntoOneSet_AndUnloadingOneGoesBackToItsStoredSet()
    {
        SceneLoad first = StoredWithProbes("A", Float3.Zero).Load();
        first.Wait();
        SceneLoad second = StoredWithProbes("B", new Float3(10f, 0f, 0f)).LoadAdditive();
        second.Wait();
        SceneInstance main = SceneInstance.Main;

        main.ProbeRebuild!.Wait();
        SceneInstance.ProbeSet merged = main.Probes!;
        Assert.Equal(10, merged.Positions.Length);
        Assert.Equal(new Float3(10f, 0f, 0f), merged.Positions[5]);
        Assert.NotEmpty(merged.Tetrahedra);

        first.Section!.Unload();
        SceneInstance.ProcessPendingLoad();

        Assert.Null(main.ProbeRebuild);
        Assert.Same(second.Section!.Lighting.ProbeTetrahedra, main.Probes!.Tetrahedra);
    }

    [Fact]
    public void Load_DropsTheOldProbesAtOnce()
    {
        StoredWithProbes("Lit", Float3.Zero).Load().Wait();
        Assert.NotNull(SceneInstance.Main.Probes);

        Stored("Dark", "d").Load().Wait();

        Assert.Null(SceneInstance.Main.Probes);
        Assert.Null(SceneInstance.Main.ProbeVolume);
    }
}
