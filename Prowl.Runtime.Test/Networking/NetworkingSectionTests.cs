using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private (SceneInstance World, FakePeer Alice) SectionHost()
    {
        SceneInstance world = CreateScene();
        world.Add(CreateGameObject("Level"));
        MakeCurrent(world);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        Pump(() => alice.Has(FakePeer.Scene), alice);
        return (world, alice);
    }

    [Fact]
    public void UnloadingASectionSendsNoDespawnForANestedNetworkedObject()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject props = new("Props"), crate = new("Crate");
            crate.SetParent(props);
            crate.Network.Networked = true;
            SceneSection arena = world.AddContent(new SceneContent { Objects = [props, crate] }, "Arena", null);
            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            Assert.Equal(1, NetworkDiagnostics.NetworkObjectCount);

            world.UnloadNow(arena);
            Pump(() => alice.Has(FakePeer.SectionRemove), alice);
            Assert.False(alice.Has(FakePeer.Despawn));
            Assert.Equal(0, NetworkDiagnostics.NetworkObjectCount);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void HostSendsANetworkedObjectMovingToAnotherSection()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject crate = new("Crate");
            crate.Network.Networked = true;
            SceneSection arena = world.AddContent(new SceneContent { Objects = [crate] }, "Arena", null);
            Pump(() => alice.Has(FakePeer.SectionAdd), alice);

            crate.Persistent = true;
            Pump(() => alice.Has(FakePeer.SectionMove), alice);
            NetReader reader = new(alice.Last(FakePeer.SectionMove));
            SectionMoveMessage move = SectionMoveMessage.Read(ref reader);
            Assert.Equal(crate.Identifier, move.Identifier);
            Assert.Equal(NetworkSystem.PersistentSectionId, move.SectionId);

            world.UnloadNow(arena);
            Pump(() => alice.Has(FakePeer.SectionRemove), alice);
            Assert.False(crate.IsDisposed);
            Assert.Equal(1, NetworkDiagnostics.NetworkObjectCount);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void ClientMovesANetworkedObjectWhereTheHostMovedIt()
    {
        SceneInstance hostWorld = CreateScene();
        hostWorld.Add(CreateGameObject("Level"));
        using NetWriter snapshot = new(), spawn = new(), move = new();
        NetworkTestScene.Current.WriteScene(hostWorld, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Networked = true;
        NetworkTestScene.Current.WriteSpawn(crate, owner: -1, NetworkOrphaned.Destroy, spawn, generation: 1);
        new SectionMoveMessage(crate.Identifier, NetworkSystem.PersistentSectionId).Write(move);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier), host);
            GameObject here = Find(SceneInstance.Main, crate.Identifier);
            Assert.False(here.Persistent);

            host.Session.SendRaw(client, FakePeer.SectionMove, move.WrittenSpan);
            Pump(() => here.Persistent, host);
            Assert.Equal(crate.Identifier, here.Identifier);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SpawningIntoASectionMadeAfterTheSceneMessageSendsTheSectionFirst()
    {
        var (world, alice) = SectionHost();
        try
        {
            Pump(() => alice.IsReady, alice);
            SceneSection late = world.CreateSection("Late");
            GameObject crate = CreateGameObject("Crate");
            world.Add(crate, late);
            crate.SpawnBuilt();

            Pump(() => alice.Has(FakePeer.Spawn), alice);
            int added = alice.Received.FindIndex(m => m.Id == FakePeer.SectionAdd);
            Assert.InRange(added, 0, alice.Received.FindIndex(m => m.Id == FakePeer.Spawn) - 1);
            NetReader section = new(alice.Received[added].Data);
            section.ReadUInt64();
            Assert.Equal("Late", SectionHeader.Read(ref section).Name);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void HostSharesAWorldBoundAfterMainWasUnbound()
    {
        var (world, alice) = SectionHost();
        try
        {
            SceneInstance.Main = null!;
            world.Dispose();
            SceneInstance next = CreateScene();
            next.Add(CreateGameObject("Arena"));
            Bind(next);

            Pump(() => alice.Received.Count(m => m.Id == FakePeer.Scene) == 2, alice);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void JoiningWithNoWorldBoundEndsTheSession()
    {
        SceneInstance hostWorld = CreateScene();
        hostWorld.Add(CreateGameObject("Level"));
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(hostWorld, snapshot);
        SceneInstance.Main = null!;

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
        string? failure = null;
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            session.Ended += (_, message) => failure = message;
            Pump(() => Networking.Networking.Session == null, host);
            Assert.Equal("Joining needs a world bound as SceneInstance.Main to load into.", failure);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASpawnCallbackOnAJoiningClientFindsTheIncomingObjects()
    {
        SceneInstance hostWorld = CreateScene();
        GameObject root = CreateGameObject("Root");
        root.Network.Networked = true;
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        hostWorld.Add(root);
        Bind(hostWorld);
        byte[] snapshot;
        byte[][] spawns;
        LocalNetwork source = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(source, "host"));
            (snapshot, spawns) = RecordJoin(source);
            Assert.Single(spawns);
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        EngineObject? found = null;
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot, spawns);
        NetworkLifecycleListener.SpawnedOnClient = l => found = Networking.Networking.System!.FindByIdentifier(l.GameObject.Identifier);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            Assert.NotNull(found);
            Assert.NotSame(root, found);
        }
        finally
        {
            NetworkLifecycleListener.SpawnedOnClient = null;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void LoadingTheSameSceneTwiceIntoTheNetworkedWorldIsRefused()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        var (world, alice) = SectionHost();
        try
        {
            var authoring = new SceneInstance();
            authoring.Create("Crate");
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            SceneLoad first = arena.LoadAdditive(world);
            first.Wait();
            Assert.Equal(SceneLoadPhase.Done, first.Phase);

            SceneLoad second = arena.LoadAdditive(world);
            second.Wait();
            Assert.Equal(SceneLoadPhase.Failed, second.Phase);
            Assert.IsType<SceneLoadException>(second.Error);
            Assert.Single(world.Sections, s => s.Name == "Arena");
            Assert.Single(world.Objects, o => o.Name == "Crate");

            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            PumpFor(50, alice);
            Assert.Equal(1, alice.Received.Count(m => m.Id == FakePeer.SectionAdd));
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }

    [Fact]
    public void LoadingTheSameSceneTwiceWithoutASessionStillWorks()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        try
        {
            var authoring = new SceneInstance();
            authoring.Create("Crate");
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            arena.LoadAdditive().Wait();
            SceneLoad second = arena.LoadAdditive();
            second.Wait();
            Assert.Equal(SceneLoadPhase.Done, second.Phase);
            Assert.Equal(2, SceneInstance.Main.Sections.Count(s => s.Name == "Arena"));
        }
        finally
        {
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }

    [Fact]
    public void ClientUnloadingAHostSectionEndsTheSession()
    {
        SceneInstance hostWorld = CreateScene();
        hostWorld.Add(CreateGameObject("Level"));
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(hostWorld, snapshot);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
        string? failure = null;
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            session.Ended += (_, message) => failure = message;
            Pump(() => Networking.Networking.IsReady, host);

            // A local additive section is the client's own, and unloading it is fine.
            SceneSection local = SceneInstance.Main.CreateSection("Local");
            SceneInstance.Main.UnloadNow(local);
            Assert.True(Networking.Networking.IsActive);

            SceneSection shared = SceneInstance.Main.Sections.First(s => !s.IsPersistent);
            SceneInstance.Main.CreateSection("Keep");
            SceneInstance.Main.UnloadNow(shared);
            Assert.Null(Networking.Networking.Session);
            Assert.Equal("The network scene was replaced locally during the session.", failure);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
