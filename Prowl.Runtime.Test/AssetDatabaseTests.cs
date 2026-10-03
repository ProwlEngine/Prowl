// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>An asset for residency tests, with the kinds of field the walk has to follow.</summary>
public sealed class Crate : Asset
{
    public int Size;
    public Crate? Other;
    public List<Crate> Many = new();
    [NotHeld] public Crate? Unheld;
    public AssetRef<Crate> Later;
}

/// <summary>A component holding a crate, the way gameplay code holds an asset.</summary>
public sealed class CrateHolder : MonoBehaviour
{
    public Crate? Crate;
    public Crate? SeenOnEnable;
    public bool WasLoadedOnEnable;

    public override void OnEnable()
    {
        SeenOnEnable = Crate;
        WasLoadedOnEnable = Crate is { IsLoaded: true };
    }
}

/// <summary>
/// The asset database's promises: one object per GUID that loading, unloading and refilling all act on, references
/// written as stubs, and everything something reaches staying loaded while what nothing reaches goes.
/// </summary>
public class AssetDatabaseTests : RuntimeTestBase
{
    private readonly MemoryAssetBackend _backend = new();
    private readonly AssetBackend? _previous = AssetDatabase.Backend;

    public AssetDatabaseTests()
    {
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = _backend;
    }

    public override void Dispose()
    {
        AssetDatabase.Backend = _previous;
        AssetDatabase.ClearForTests();
        base.Dispose();
    }

    private Guid AddCrate(int size, Action<Crate>? build = null)
    {
        var crate = new Crate { Size = size, Name = $"Crate{size}" };
        build?.Invoke(crate);
        return _backend.Add(crate, $"Crate{size}.crate");
    }

    #region Identity and state

    [Fact]
    public void Get_ReturnsOneObjectPerGuid_WithoutLoadingIt()
    {
        Guid id = AddCrate(3);

        Crate a = AssetDatabase.Get<Crate>(id)!;
        Crate b = AssetDatabase.Get<Crate>(id)!;

        Assert.Same(a, b);
        Assert.Equal(AssetState.Unloaded, a.State);
        Assert.Empty(_backend.Reads);
        Assert.Equal("Crate3", a.Name);
    }

    [Fact]
    public void Get_OfAnUnknownGuid_IsMissingWhenTyped_AndNothingWhenNot()
    {
        Guid unknown = Guid.NewGuid();

        Assert.Null(AssetDatabase.Get(unknown));
        Crate missing = AssetDatabase.Get<Crate>(unknown)!;
        Assert.True(missing.IsMissing);
        Assert.Same(missing, AssetDatabase.Get<Crate>(unknown));
    }

    [Fact]
    public void Load_FillsTheObject_Once()
    {
        Guid id = AddCrate(5);
        var loadedEvents = new List<Asset>();
        void OnLoaded(Asset asset) => loadedEvents.Add(asset);
        AssetDatabase.Loaded += OnLoaded;
        try
        {
            Crate crate = AssetDatabase.Load<Crate>(id)!;
            crate.Load();

            Assert.True(crate.IsLoaded);
            Assert.Equal(5, crate.Size);
            Assert.Equal(1, crate.ContentVersion);
            Assert.Equal(1, _backend.Reads[id]);
            Assert.Equal([crate], loadedEvents);
        }
        finally
        {
            AssetDatabase.Loaded -= OnLoaded;
        }
    }

    [Fact]
    public void ADatabaseAsset_CannotBeDisposed_ButARuntimeOneCan()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(1))!;
        var runtime = new Crate();

        Assert.Throws<InvalidOperationException>(crate.Dispose);
        Assert.Throws<InvalidOperationException>(crate.Destroy);
        runtime.Dispose();
        Assert.True(runtime.IsDisposed);
    }

    [Fact]
    public void ARuntimeAsset_IsAlwaysLoaded_AndHasNoGuid()
    {
        var crate = new Crate();
        Assert.True(crate.IsLoaded);
        Assert.False(crate.IsFromDatabase);
    }

    [Fact]
    public void Refill_KeepsTheObject_AndMovesItsContentVersion()
    {
        Guid id = AddCrate(1);
        Crate crate = AssetDatabase.Load<Crate>(id)!;
        int version = crate.ContentVersion;
        var reasons = new List<ReloadReason>();
        void OnReloaded(Asset asset, ReloadReason reason) => reasons.Add(reason);
        AssetDatabase.Reloaded += OnReloaded;
        try
        {
            _backend.Add(id, new Crate { Size = 9 });
            AssetDatabase.Refill(crate, ReloadReason.Reimport);

            Assert.Same(crate, AssetDatabase.Get(id));
            Assert.Equal(9, crate.Size);
            Assert.True(crate.ContentVersion > version);
            Assert.Equal([ReloadReason.Reimport], reasons);
        }
        finally
        {
            AssetDatabase.Reloaded -= OnReloaded;
        }
    }

    [Fact]
    public void MeshVersion_KeepsMovingForward_AcrossARefill()
    {
        var source = new Mesh { Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)] };
        Guid id = _backend.Add(source, "Tri.mesh");
        Mesh mesh = AssetDatabase.Load<Mesh>(id)!;
        mesh.Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)];
        mesh.Vertices = [new(0, 0, 0), new(3, 0, 0), new(0, 3, 0)];
        uint before = mesh.Version;

        AssetDatabase.Refill(mesh, ReloadReason.Revert);

        Assert.True(mesh.Version > before);
    }

    [Fact]
    public void TerrainVersions_KeepMovingForward_AcrossARefill()
    {
        Guid id = _backend.Add(new TerrainData(), "Ground.terrain");
        TerrainData terrain = AssetDatabase.Load<TerrainData>(id)!;
        terrain.SetHeightmapDirty();
        terrain.SetDetailsDirty();
        int heights = terrain.HeightsVersion, details = terrain.DetailsVersion;

        AssetDatabase.Refill(terrain, ReloadReason.Revert);

        Assert.True(terrain.HeightsVersion > heights);
        Assert.True(terrain.DetailsVersion > details);
    }

    [Fact]
    public void GraphDeepVersion_Changes_AcrossARefillThatBringsBackAnOlderEditCount()
    {
        var source = new AnimationGraph { Name = "Graph" };
        Guid id = _backend.Add(source, "G.animgraph");
        AnimationGraph graph = AssetDatabase.Load<AnimationGraph>(id)!;
        int before = graph.DeepVersion;

        AssetDatabase.Refill(graph, ReloadReason.Revert);

        Assert.NotEqual(before, graph.DeepVersion);
    }

    private void WaitForBackgroundRead(Guid id)
    {
        for (int i = 0; i < 2000 && !_backend.Reads.ContainsKey(id); i++)
            System.Threading.Thread.Sleep(1);
        Assert.True(_backend.Reads.ContainsKey(id));
    }

    private static void PumpUntil(Task load)
    {
        for (int i = 0; i < 2000 && !load.IsCompleted; i++)
        {
            AssetDatabase.Pump();
            System.Threading.Thread.Sleep(1);
        }
        Assert.True(load.IsCompleted);
    }

    [Fact]
    public void AReimportDuringABackgroundLoad_PublishesTheNewContent()
    {
        Guid id = AddCrate(1);
        Crate crate = AssetDatabase.Get<Crate>(id)!;
        AssetLoader.SetMainThread();

        Task load = crate.LoadAsync();
        WaitForBackgroundRead(id);
        _backend.Add(id, new Crate { Size = 9 });
        AssetDatabase.Refill(crate, ReloadReason.Reimport);
        PumpUntil(load);

        Assert.Equal(9, crate.Size);
    }

    [Fact]
    public void AnAssetDeletedDuringABackgroundLoad_StaysMissing()
    {
        Guid id = AddCrate(1);
        Crate crate = AssetDatabase.Get<Crate>(id)!;
        AssetLoader.SetMainThread();

        Task load = crate.LoadAsync();
        WaitForBackgroundRead(id);
        _backend.Remove(id);
        AssetDatabase.MarkMissing(id);
        PumpUntil(load);

        Assert.True(crate.IsMissing);
    }

    private sealed class IteratorHolder : MonoBehaviour
    {
        public static bool Ran;
        public IEnumerable<Crate>? Crates;

        public static IEnumerable<Crate> Endless()
        {
            Ran = true;
            while (true) yield return new Crate();
        }
    }

    [Fact]
    public void TheWalk_NeverRunsAnIteratorItFinds()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        scene.Add(go);
        IteratorHolder.Ran = false;
        go.AddComponent<IteratorHolder>().Crates = IteratorHolder.Endless();

        AssetDatabase.Walk();

        Assert.False(IteratorHolder.Ran);
    }

    [Fact]
    public void MeshVersion_KeepsMovingForward_AcrossAnUnloadAndReload()
    {
        var source = new Mesh { Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)] };
        Mesh mesh = AssetDatabase.Load<Mesh>(_backend.Add(source, "Tri.mesh"))!;
        mesh.Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)];
        uint before = mesh.Version;

        AssetDatabase.UnloadUnused();
        mesh.Load();

        Assert.True(mesh.Version > before);
    }

    [Fact]
    public void AMissingGuidNothingRefersTo_LeavesTheRegistry()
    {
        Guid unknown = Guid.NewGuid();
        Assert.True(AssetDatabase.Get<Crate>(unknown)!.IsMissing);

        AssetDatabase.Walk();
        AssetDatabase.Walk();

        Assert.False(AssetDatabase.TryGetExisting(unknown, out _));
    }

    [Fact]
    public void EveryBuiltInAsset_LoadsOnTheLoaderThread_WithoutLoadingAnother()
    {
        var nested = new List<string>();
        void Capture(string message, DebugStackTrace? trace, LogSeverity severity)
        {
            if (message.Contains("inside another load") || message.Contains("while another asset was loading")) lock (nested) nested.Add(message);
        }

        Debug.OnLog += Capture;
        try
        {
            AssetLoader.SetMainThread();
            var loads = new List<Task>();
            foreach (Guid id in BuiltInAssets.Entries.Keys)
                if (AssetDatabase.Get(id) is { } builtIn)
                    loads.Add(builtIn.LoadAsync());

            Task all = Task.WhenAll(loads);
            PumpUntil(all);
        }
        finally
        {
            Debug.OnLog -= Capture;
        }

        Assert.True(nested.Count == 0, string.Join("\n", nested));
    }

    [Fact]
    public void AMaterialWhoseShaderIsMissing_DrawsWithTheStandardShader()
    {
        var material = new Material();
        material.Shader = AssetDatabase.Get<Shader>(Guid.NewGuid());

        Assert.Same(Shader.LoadDefault(DefaultShader.Standard), material.Shader);
    }

    [Fact]
    public void ARefilledActionMap_KeepsItsActions_WithTheirListenersAndEnabledState()
    {
        var source = new InputActionMap("Player");
        source.AddAction("Jump");
        Guid id = _backend.Add(source, "Player.inputactions");
        InputActionMap map = AssetDatabase.Load<InputActionMap>(id)!;
        InputAction jump = map.GetAction("Jump");
        jump.Performed += _ => { };
        jump.Enable();

        AssetDatabase.Refill(map, ReloadReason.Reimport);

        Assert.Same(jump, map.GetAction("Jump"));
        Assert.Same(map, jump.ActionMap);
        Assert.True(jump.Enabled);
        jump.Disable();
    }

    // What leaving play mode does, so nothing a play session attached to the map carries on.
    [Fact]
    public void AReloadedActionMap_HandsOutFreshActions()
    {
        var source = new InputActionMap("Player");
        source.AddAction("Jump");
        Guid id = _backend.Add(source, "Player.inputactions");
        InputActionMap map = AssetDatabase.Load<InputActionMap>(id)!;
        InputAction played = map.GetAction("Jump");

        AssetDatabase.Reload(map);

        Assert.NotSame(played, map.GetAction("Jump"));
        Assert.Same(map, map.GetAction("Jump").ActionMap);
    }

    [Fact]
    public void MarkMissing_KeepsTheObject_AndARestoredGuidLoadsInIt()
    {
        Guid id = AddCrate(4);
        Crate crate = AssetDatabase.Load<Crate>(id)!;

        _backend.Remove(id);
        AssetDatabase.MarkMissing(id);
        Assert.True(crate.IsMissing);
        Assert.Equal(0, crate.Size);

        _backend.Add(id, new Crate { Size = 4 });
        AssetDatabase.Refill(crate, ReloadReason.Reimport);
        crate.Load();

        Assert.True(crate.IsLoaded);
        Assert.Equal(4, crate.Size);
    }

    [Fact]
    public void AnUnreadableAsset_Fails_AndReadsAsEmpty()
    {
        Guid id = Guid.NewGuid();
        _backend.Set(id, typeof(Crate), new EchoObject("not a crate"));

        Crate crate = AssetDatabase.Load<Crate>(id)!;

        Assert.Equal(AssetState.Failed, crate.State);
        Assert.Equal(0, crate.Size);
    }

    [Fact]
    public void AFailedAsset_LoadsOnceItsSourceIsFixedAndReimported()
    {
        Guid id = Guid.NewGuid();
        _backend.Set(id, typeof(Crate), new EchoObject("not a crate"));
        Crate crate = AssetDatabase.Load<Crate>(id)!;
        Assert.Equal(AssetState.Failed, crate.State);

        _backend.Add(id, new Crate { Size = 5 });
        AssetDatabase.Refill(crate, ReloadReason.Reimport);
        Assert.Equal(AssetState.Unloaded, crate.State);

        crate.Load();
        Assert.True(crate.IsLoaded);
        Assert.Equal(5, crate.Size);
    }

    [Fact]
    public void CloneAll_CopiesEveryAssetRoot()
    {
        Crate a = AssetDatabase.Load<Crate>(AddCrate(1))!;
        Crate b = AssetDatabase.Load<Crate>(AddCrate(2))!;

        List<Crate> copies = ObjectCopy.CloneAll([a, b]);

        Assert.NotSame(a, copies[0]);
        Assert.NotSame(b, copies[1]);
        Assert.Equal(1, copies[0].Size);
        Assert.Equal(2, copies[1].Size);
    }

    #endregion

    #region Serialization

    [Fact]
    public void ADatabaseAsset_IsWrittenAsAStub_AndReadBackAsTheSameObject()
    {
        Crate crate = AssetDatabase.Get<Crate>(AddCrate(2))!;
        var holder = new Crate { Other = crate };

        EchoObject echo = Serializer.Serialize(typeof(Crate), holder);
        Crate copy = Serializer.Deserialize<Crate>(echo)!;

        Assert.Equal(crate.AssetID.ToString(), echo["Other"]["$asset"].StringValue);
        Assert.Same(crate, copy.Other);
        Assert.Empty(_backend.Reads);
    }

    [Fact]
    public void AnAssetThatIsTheRoot_IsWrittenInFull()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(6))!;

        EchoObject echo = Serializer.Serialize(typeof(Crate), crate);

        Assert.Equal(6, echo["Size"].IntValue);
    }

    // A stub is a leaf, so loading one asset never loads another and a cycle loads without a null.
    [Fact]
    public void ACycleOfThreeAssets_LoadsWithEveryFieldSet()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Crate shellA = AssetDatabase.Get<Crate>(a)!, shellB = AssetDatabase.Get<Crate>(b)!, shellC = AssetDatabase.Get<Crate>(c)!;
        _backend.Add(a, new Crate { Size = 1, Other = shellB });
        _backend.Add(b, new Crate { Size = 2, Other = shellC });
        _backend.Add(c, new Crate { Size = 3, Other = shellA });
        AssetDatabase.ClearForTests();

        Crate loadedA = AssetDatabase.Load<Crate>(a)!;
        Crate loadedB = loadedA.Other!;
        loadedB.Load();
        Crate loadedC = loadedB.Other!;
        loadedC.Load();

        Assert.Same(loadedA, loadedC.Other);
        Assert.Equal(1, _backend.Reads[a]);
    }

    [Fact]
    public void AStubForAnUnknownGuid_ReadsAsAMissingAsset_ThatKeepsItsGuid()
    {
        Guid unknown = Guid.NewGuid();
        var echo = EchoObject.NewCompound();
        echo["Other"] = EchoObject.NewCompound();
        echo["Other"]["$asset"] = new EchoObject(unknown.ToString());

        Crate copy = Serializer.Deserialize<Crate>(echo)!;

        Assert.True(copy.Other!.IsMissing);
        EchoObject written = Serializer.Serialize(typeof(Crate), copy);
        Assert.Equal(unknown.ToString(), written["Other"]["$asset"].StringValue);
    }

    [Fact]
    public void AssetRef_IsWrittenAsItsOwnKey_AndLoadsOnlyWhenAsked()
    {
        Guid id = AddCrate(8);
        var holder = new Crate { Later = new AssetRef<Crate>(id) };

        EchoObject echo = Serializer.Serialize(typeof(Crate), holder);
        Crate copy = Serializer.Deserialize<Crate>(echo)!;

        Assert.Equal(id.ToString(), echo["Later"]["$assetRef"].StringValue);
        Assert.Equal(AssetState.Unloaded, copy.Later.Get()!.State);
        Assert.Equal(8, copy.Later.Load()!.Size);
    }

    #endregion

    #region Residency

    [Fact]
    public void AnAssetNothingReaches_IsUnloaded_AndLoadsInTheSameObjectAgain()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(7))!;

        Assert.Equal(1, AssetDatabase.UnloadUnused());
        Assert.Equal(AssetState.Unloaded, crate.State);

        crate.Load();
        Assert.Equal(7, crate.Size);
        Assert.Same(crate, AssetDatabase.Get(crate.AssetID));
    }

    // An accessor loads what was unloaded under it, so a reader never sees an empty payload.
    [Fact]
    public void ReadingThroughAnAccessor_AfterUnloading_LoadsItAgain()
    {
        var source = new Mesh { Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)] };
        Mesh mesh = AssetDatabase.Load<Mesh>(_backend.Add(source, "Tri.mesh"))!;

        AssetDatabase.UnloadUnused();
        Assert.Equal(AssetState.Unloaded, mesh.State);

        Assert.Equal(3, mesh.VertexCount);
        Assert.True(mesh.IsLoaded);
    }

    [Fact]
    public void AnObjectRemovedFromASavedScene_NoLongerKeepsWhatItUsed()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(1))!;
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        scene.Add(go);
        go.AddComponent<CrateHolder>().Crate = crate;
        scene.Update();
        Serializer.Serialize(typeof(Scene), scene);

        scene.Remove(go);
        go.Dispose();
        AssetDatabase.UnloadUnused();

        Assert.False(crate.IsLoaded);
    }

    private static class StaticHolder
    {
        [HeldStatic] public static Crate? Held;
        public static Crate? NotMarked;
    }

    [Fact]
    public void AStaticMarkedHeld_KeepsItsAssetLoaded_AndAnUnmarkedOneDoesNot()
    {
        Crate held = AssetDatabase.Load<Crate>(AddCrate(1))!;
        Crate unmarked = AssetDatabase.Load<Crate>(AddCrate(2))!;
        StaticHolder.Held = held;
        StaticHolder.NotMarked = unmarked;
        try
        {
            AssetDatabase.UnloadUnused();

            Assert.True(held.IsLoaded);
            Assert.False(unmarked.IsLoaded);

            StaticHolder.Held = null;
            AssetDatabase.UnloadUnused();
            Assert.False(held.IsLoaded);
        }
        finally
        {
            StaticHolder.Held = null;
            StaticHolder.NotMarked = null;
        }
    }

    [Fact]
    public void AnAssetAComponentHolds_StaysLoaded_EvenWhileDisabled()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(1))!;
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        scene.Add(go);
        go.AddComponent<CrateHolder>().Crate = crate;
        go.Enabled = false;

        AssetDatabase.UnloadUnused();

        Assert.True(crate.IsLoaded);
    }

    [Fact]
    public void AnAsset_ReachesWhatItsOwnFieldsHold()
    {
        Crate inner = AssetDatabase.Load<Crate>(AddCrate(2))!;
        Crate listed = AssetDatabase.Load<Crate>(AddCrate(3))!;
        var outer = new Crate { Other = inner, Many = { listed } };
        object owner = new();
        AssetDatabase.AddRoot(owner = new List<Crate> { outer });

        AssetDatabase.UnloadUnused();

        Assert.True(inner.IsLoaded);
        Assert.True(listed.IsLoaded);
        AssetDatabase.RemoveRoot(owner);
    }

    [Fact]
    public void NotHeldFields_AndAssetRefs_HoldNothing()
    {
        Crate unheld = AssetDatabase.Load<Crate>(AddCrate(2))!;
        Crate later = AssetDatabase.Load<Crate>(AddCrate(3))!;
        var outer = new Crate { Unheld = unheld, Later = later };
        var root = new List<Crate> { outer };
        AssetDatabase.AddRoot(root);

        AssetDatabase.UnloadUnused();

        Assert.False(unheld.IsLoaded);
        Assert.False(later.IsLoaded);
        AssetDatabase.RemoveRoot(root);
    }

    [Fact]
    public void Hold_KeepsAnAssetLoaded_UntilReleased_AndIsIdempotent()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(1))!;
        object owner = new();

        AssetDatabase.Hold(crate, owner);
        AssetDatabase.Hold(crate, owner);
        AssetDatabase.UnloadUnused();
        Assert.True(crate.IsLoaded);

        AssetDatabase.Release(crate, owner);
        AssetDatabase.UnloadUnused();
        Assert.False(crate.IsLoaded);
    }

    [Fact]
    public void AnAssetStaysLoaded_ThroughTheGracePeriod()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(1))!;
        TimeSpan grace = AssetDatabase.GracePeriod, interval = AssetDatabase.WalkInterval;
        AssetDatabase.GracePeriod = TimeSpan.FromHours(1);
        AssetDatabase.WalkInterval = TimeSpan.Zero;
        try
        {
            for (int i = 0; i < 4; i++) AssetDatabase.EndFrame();
            Assert.True(crate.IsLoaded);

            AssetDatabase.GracePeriod = TimeSpan.Zero;
            AssetDatabase.EndFrame();
            AssetDatabase.EndFrame();
            Assert.False(crate.IsLoaded);
        }
        finally
        {
            AssetDatabase.GracePeriod = grace;
            AssetDatabase.WalkInterval = interval;
        }
    }

    [Fact]
    public void Explain_SaysWhatHoldsAnAsset()
    {
        Crate crate = AssetDatabase.Load<Crate>(AddCrate(1))!;
        object owner = "the test";
        AssetDatabase.Hold(crate, owner);
        AssetDatabase.RecordReachPaths = true;
        try
        {
            AssetDatabase.Walk();
            AssetResidency residency = AssetDatabase.Explain(crate);

            Assert.True(residency.Reached);
            Assert.Contains(owner, residency.HeldBy);
            Assert.Equal(AssetState.Loaded, residency.State);
        }
        finally
        {
            AssetDatabase.RecordReachPaths = false;
            AssetDatabase.ReleaseAll(owner);
        }
    }

    [Fact]
    public void AGroup_LoadsEverything_AndHoldsItUntilDisposed()
    {
        Crate a = AssetDatabase.Get<Crate>(AddCrate(1))!;
        Crate b = AssetDatabase.Get<Crate>(AddCrate(2))!;

        AssetGroup group = AssetDatabase.LoadGroup([a, b], withDependencies: false);
        group.Wait();

        Assert.True(group.IsDone);
        Assert.Equal(1f, group.Progress);
        AssetDatabase.UnloadUnused();
        Assert.True(a.IsLoaded && b.IsLoaded);

        group.Dispose();
        AssetDatabase.UnloadUnused();
        Assert.False(a.IsLoaded || b.IsLoaded);
    }

    [Fact]
    public void ABlockingLoadOnAnotherThread_FinishesOnceTheMainThreadPumps()
    {
        Crate crate = AssetDatabase.Get<Crate>(AddCrate(5))!;
        AssetLoader.SetMainThread();

        Task load = Task.Run(() => crate.Load());
        for (int i = 0; i < 1000 && !load.IsCompleted; i++)
        {
            AssetDatabase.Pump();
            System.Threading.Thread.Sleep(1);
        }

        Assert.True(load.IsCompleted);
        Assert.Equal(5, crate.Size);
    }

    [Fact]
    public async Task LoadAsync_CompletesOnceTheLoadIsPublished()
    {
        Crate crate = AssetDatabase.Get<Crate>(AddCrate(4))!;

        Task load = crate.LoadAsync();
        for (int i = 0; i < 500 && !load.IsCompleted; i++)
        {
            AssetDatabase.Pump();
            await Task.Delay(2);
        }

        Assert.True(load.IsCompleted);
        Assert.Equal(4, crate.Size);
    }

    #endregion

    #region Scenes and prefabs

    // Everything a scene uses is loaded before any of it enables, so OnEnable never sees an asset still loading.
    [Fact]
    public void LoadingAStoredScene_LoadsWhatItUses_BeforeAnythingEnables()
    {
        Crate crate = AssetDatabase.Get<Crate>(AddCrate(5))!;
        var authored = new Scene();
        var go = new GameObject("Holder");
        authored.Add(go);
        go.AddComponent<CrateHolder>().Crate = crate;
        var stored = new SceneAsset { Data = Serializer.Serialize(typeof(object), authored) };
        authored.Dispose();
        Guid sceneId = _backend.Add(stored, "Level.scene");

        Scene.Load(AssetDatabase.Get<SceneAsset>(sceneId)!);
        Scene.ProcessPendingLoad();
        try
        {
            CrateHolder holder = Scene.Current.AllObjects.Single().GetComponent<CrateHolder>()!;
            Assert.Same(crate, holder.SeenOnEnable);
            Assert.True(holder.WasLoadedOnEnable);
        }
        finally
        {
            Scene.Shutdown();
        }
    }

    [Fact]
    public void APrefab_KeepsWhatItsStoredTreeUsesLoaded()
    {
        Crate crate = AssetDatabase.Get<Crate>(AddCrate(3))!;
        var go = new GameObject("Holder");
        go.AddComponent<CrateHolder>().Crate = crate;
        var stored = new PrefabAsset { GameObjectData = Serializer.Serialize(typeof(object), go) };
        go.Dispose();
        PrefabAsset prefab = AssetDatabase.Load<PrefabAsset>(_backend.Add(stored, "Holder.prefab"))!;
        crate.Load();
        object owner = new();
        AssetDatabase.Hold(prefab, owner);

        AssetDatabase.UnloadUnused();

        Assert.Contains(crate, prefab.ReferencedAssets);
        Assert.True(crate.IsLoaded);
        AssetDatabase.ReleaseAll(owner);
    }

    [Fact]
    public void AStoredScene_HeldOnItsOwn_DoesNotKeepWhatItUses()
    {
        Crate crate = AssetDatabase.Get<Crate>(AddCrate(3))!;
        var authored = new Scene();
        var go = new GameObject("Holder");
        authored.Add(go);
        go.AddComponent<CrateHolder>().Crate = crate;
        var stored = new SceneAsset { Data = Serializer.Serialize(typeof(object), authored) };
        authored.Dispose();
        SceneAsset level = AssetDatabase.Load<SceneAsset>(_backend.Add(stored, "Level.scene"))!;
        crate.Load();
        object owner = new();
        AssetDatabase.Hold(level, owner);

        AssetDatabase.UnloadUnused();

        Assert.True(level.IsLoaded);
        Assert.False(crate.IsLoaded);
        AssetDatabase.ReleaseAll(owner);
    }

    #endregion
}
