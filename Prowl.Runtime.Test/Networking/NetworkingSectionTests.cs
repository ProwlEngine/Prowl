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
        Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
        return (world, alice);
    }

    private static List<SectionData> ReadSectionAdd(byte[] payload, out uint objectCount, SceneInstance? linkTo = null)
    {
        NetReader reader = new(payload);
        SectionAddMessage message = SectionAddMessage.Read(ref reader);
        List<SectionData> sections = NetworkTestScene.Current.ReadSections(NetworkTestScene.Current.Decompress(message.Sections), linkTo);
        objectCount = ObjectStateBlock.Read(ref reader).Count;
        return sections;
    }

    [Fact]
    public void HostSendsAnAdditiveSectionWithItsNetworkedObjects()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject crate = new("Crate");
            crate.Network.Mode = NetworkMode.Object;
            world.AddContent(new SceneContent { Objects = [crate] }, "Arena", null);

            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            List<SectionData> sections = ReadSectionAdd(alice.Last(FakePeer.SectionAdd), out uint objectCount);
            try
            {
                SectionData arena = Assert.Single(sections);
                Assert.Equal("Arena", arena.Name);
                Assert.NotEqual(NetworkSystem.PersistentSectionId, arena.Id);
                Assert.Contains(arena.Content.Objects, o => o.Identifier == crate.Identifier);
                Assert.Equal(1u, objectCount);
            }
            finally
            {
                foreach (SectionData section in sections) section.Content.DisposeObjects();
            }
            Assert.Equal(1, NetworkDiagnostics.NetworkObjectCount);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void HostUnloadingASectionRemovesItWithoutDespawningItsObjects()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject crate = new("Crate");
            crate.Network.Mode = NetworkMode.Object;
            SceneSection arena = world.AddContent(new SceneContent { Objects = [crate] }, "Arena", null);
            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            List<SectionData> sent = ReadSectionAdd(alice.Last(FakePeer.SectionAdd), out _);
            uint id = sent[0].Id;
            foreach (SectionData section in sent) section.Content.DisposeObjects();

            world.UnloadNow(arena);
            Pump(() => alice.Has(FakePeer.SectionRemove), alice);

            NetReader reader = new(alice.Last(FakePeer.SectionRemove));
            Assert.Equal(id, SectionRemoveMessage.Read(ref reader).SectionId);
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
    public void ClientAddsAndRemovesTheSectionsTheHostSends()
    {
        SceneInstance hostWorld = CreateScene();
        GameObject level = CreateGameObject("Level");
        hostWorld.Add(level);
        using NetWriter snapshot = new(), add = new(), remove = new();
        NetworkTestScene.Current.SceneGeneration = 1;
        NetworkTestScene.Current.WriteSnapshot(hostWorld, snapshot);
        SceneSection arena = hostWorld.CreateSection("Arena");
        GameObject crate = CreateGameObject("Crate");
        hostWorld.Add(crate, arena);
        NetworkTestScene.Current.WriteSection(arena, add);
        new SectionRemoveMessage(1, NetworkTestScene.Current.SectionId(arena)).Write(remove);
        NetworkTestScene.Current.SceneGeneration = 0;

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == level.Identifier), host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);

            host.Session.SendRaw(client, FakePeer.SectionAdd, add.WrittenSpan);
            Pump(() => SceneInstance.Main.Sections.Any(s => s.Name == "Arena"), host);
            GameObject received = Find(SceneInstance.Main, crate.Identifier);
            Assert.Equal("Arena", received.Section!.Name);
            Assert.NotSame(crate, received);

            host.Session.SendRaw(client, FakePeer.SectionRemove, remove.WrittenSpan);
            Pump(() => SceneInstance.Main.Sections.All(s => s.Name != "Arena"), host);
            Assert.DoesNotContain(SceneInstance.Main.Objects, o => o.Identifier == crate.Identifier);
            Assert.Contains(SceneInstance.Main.Objects, o => o.Identifier == level.Identifier);
            Assert.True(Networking.Networking.IsActive);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private static void DisposeSections(List<SectionData> sections)
    {
        foreach (SectionData section in sections) section.Content.DisposeObjects();
    }

    [Fact]
    public void AnAdditiveSectionLinksToObjectsInOtherSectionsInsteadOfCopyingThem()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject player = CreateGameObject("Player");
            world.Add(player, world.Persistent);
            GameObject door = new("Door");
            door.AddComponent<ReferenceHolder>().Target = player;
            world.AddContent(new SceneContent { Objects = [door] }, "Arena", null);

            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            List<SectionData> sections = ReadSectionAdd(alice.Last(FakePeer.SectionAdd), out _, world);
            try
            {
                GameObject received = Assert.Single(sections[0].Content.Objects);
                Assert.Same(player, received.GetComponent<ReferenceHolder>()!.Target);
            }
            finally { DisposeSections(sections); }
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void UnloadingASectionSendsNoDespawnForANestedNetworkedObject()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject props = new("Props"), crate = new("Crate");
            crate.SetParent(props);
            crate.Network.Mode = NetworkMode.Object;
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
            crate.Network.Mode = NetworkMode.Object;
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
        NetworkTestScene.Current.WriteSnapshot(hostWorld, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Mode = NetworkMode.Object;
        NetworkTestScene.Current.WriteSpawn(crate, owner: -1, NetworkOrphaned.Destroy, spawn, generation: 1);
        new SectionMoveMessage(crate.Identifier, NetworkSystem.PersistentSectionId).Write(move);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
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
    public void SpawningIntoASectionMadeAfterTheSnapshotSendsTheSectionFirst()
    {
        var (world, alice) = SectionHost();
        try
        {
            Pump(() => alice.IsReady, alice);
            SceneSection late = world.CreateSection("Late");
            GameObject crate = CreateGameObject("Crate");
            world.Add(crate, late);
            crate.Network.Spawn();

            Pump(() => alice.Has(FakePeer.Spawn), alice);
            int added = alice.Received.FindIndex(m => m.Id == FakePeer.SectionAdd);
            Assert.InRange(added, 0, alice.Received.FindIndex(m => m.Id == FakePeer.Spawn) - 1);
            List<SectionData> sections = ReadSectionAdd(alice.Received[added].Data, out uint objectCount);
            try
            {
                Assert.Equal("Late", Assert.Single(sections).Name);
                Assert.Equal(0u, objectCount);
            }
            finally { DisposeSections(sections); }
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

            Pump(() => alice.Received.Count(m => m.Id == FakePeer.SceneSnapshot) == 2, alice);
            NetReader reader = new(alice.Last(FakePeer.SceneSnapshot));
            using SnapshotSections received = NetworkTestScene.Current.ReadSnapshot(ref reader);
            Assert.Contains(received.Objects, o => o.Name == "Arena");
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
        NetworkTestScene.Current.WriteSnapshot(hostWorld, snapshot);
        SceneInstance.Main = null!;

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
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
        root.Network.Mode = NetworkMode.Object;
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        hostWorld.Add(root);
        Bind(hostWorld);
        byte[] snapshot;
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            using NetWriter writer = new();
            NetworkTestScene.Current.WriteSnapshot(hostWorld, writer);
            snapshot = writer.ToArray();
        }
        finally { Networking.Networking.Shutdown(); }

        EngineObject? found = null;
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot);
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
        NetworkTestScene.Current.WriteSnapshot(hostWorld, snapshot);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
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
