using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ADespawnKeepsTheHostsObjectAndItCanBeSpawnedAgainByItsPrefab()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            GameObject crate = Networking.Networking.SpawnPrefab(prefab, Float3.Zero, Quaternion.Identity);
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            Networking.Networking.Despawn(crate);
            Pump(() => alice.Has(FakePeer.Despawn), alice);
            Assert.True(crate.IsValid());
            Assert.False(crate.Network.Active);

            alice.WireReceived.Clear();
            Networking.Networking.SpawnGameObject(crate);
            Pump(() => alice.WireReceived.Any(m => m.Id == FakePeer.Spawn), alice);
            ReadTemplateSpawn(alice.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientBuildsAndReturnsPrefabInstancesThroughItsHandlers()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        byte[] snapshot, spawn;
        Guid id;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            using FakePeer recorder = FakePeer.Join(source, "recorder");
            Pump(() => recorder.Has(FakePeer.Scene), recorder);
            snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data;
            GameObject crate = Networking.Networking.SpawnPrefab(prefab, Float3.Zero, Quaternion.Identity);
            id = crate.Identifier;
            using NetWriter written = new();
            NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, written, NetworkGeneration(crate),
                NetworkTestScene.Current.NetIdOf(crate));
            spawn = written.ToArray();
            crate.Dispose();
        }
        finally { Networking.Networking.Shutdown(); }

        int built = 0;
        List<GameObject> returned = new();
        Networking.Networking.RegisterPrefab(prefab, p => { built++; return GameObject.InstantiateDetached(p)!; }, go => returned.Add(go));
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Send(FakePeer.Scene, w => w.WriteBytes(snapshot), peer);
            host.Send(FakePeer.Spawn, w => w.WriteBytes(spawn), peer);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == id), host);
            Assert.Equal(1, built);
            GameObject copy = Find(SceneInstance.Main, id);

            using NetWriter despawn = new();
            new DespawnMessage(id).Write(despawn);
            byte[] bytes = despawn.ToArray();
            host.Send(FakePeer.Despawn, w => w.WriteBytes(bytes), host.Session.Peers.Single(p => !p.IsLocal));
            Pump(() => returned.Count == 1, host);
            Assert.Same(copy, returned[0]);
            Assert.False(copy.IsDisposed);
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.UnregisterPrefab(prefab);
        }
    }

    [Fact]
    public void AClientKeepsAnObjectDespawnedAndSpawnedAgainInOneFrame()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        GameObject original = CreateGameObject("Crate");
        original.Network.Networked = true;
        using NetWriter spawn = new();
        NetworkTestScene.Current.WriteSpawn(original, -1, NetworkOrphaned.ClearOwner, spawn);
        Guid id = original.Identifier;
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == id), host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);

            // As the host sends for a Despawn and a Spawn of the same object in one frame.
            using NetWriter despawn = new();
            despawn.WriteGuid(id);
            host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            PumpFor(40, host);

            GameObject here = Assert.Single(SceneInstance.Main.Objects, o => o.Identifier == id);
            Assert.True(here.IsValid());
            Assert.True(here.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
