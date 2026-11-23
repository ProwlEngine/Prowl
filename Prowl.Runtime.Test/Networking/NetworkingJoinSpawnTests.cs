using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AJoinerGetsNetworkedObjectsAsSpawnsAndNoPlainObjectsOfACodeBuiltScene()
    {
        Scene scene = CreateScene();
        GameObject prop = CreateGameObject("Prop");
        prop.AddComponent<PlainTestComponent>();
        GameObject ship = CreateGameObject("Ship");
        ship.Network.Networked = true;
        NetTestComponent stats = ship.AddComponent<NetTestComponent>();
        scene.Add(prop);
        scene.Add(ship);
        MakeCurrent(scene);
        Guid propId = prop.Identifier, shipId = ship.Identifier;
        uint netId;
        byte[] snapshot;
        List<byte[]> spawns;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            stats.Score = 9;
            netId = NetworkTestScene.Current.NetIdOf(ship);
            using FakePeer recorder = FakePeer.Join(source, "recorder");
            Pump(() => recorder.Has(FakePeer.Scene) && recorder.Has(FakePeer.Spawn), recorder);
            snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data;
            spawns = [.. recorder.WireReceived.Where(m => m.Id == FakePeer.Spawn).Select(m => m.Data)];
            NetReader spawn = new(Assert.Single(spawns));
            Assert.Equal(shipId, spawn.ReadGuid());
        }
        finally { Networking.Networking.Shutdown(); }

        // The client has a world of its own, so anything of the host's found there came over the wire.
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Send(FakePeer.Scene, w => w.WriteBytes(snapshot), peer);
            foreach (byte[] spawn in spawns)
            {
                host.Send(FakePeer.Spawn, w => w.WriteBytes(spawn), peer);
            }
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && Scene.Main.Objects.Any(o => o.Identifier == shipId), host);
            Assert.DoesNotContain(Scene.Main.Objects, o => o.Identifier == propId);
            GameObject copy = Find(Scene.Main, shipId);
            Assert.True(copy.Network.Active);
            Assert.Equal(netId, NetworkTestScene.Current.NetIdOf(copy));
            Assert.Equal(9, copy.GetComponent<NetTestComponent>()!.Score);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AChangeArrivingDuringAJoinRunHoldsItsHookUntilTheRunEnds()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Changing");
        go.Network.Networked = true;
        NetCollectionTestComponent component = go.AddComponent<NetCollectionTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);

        byte[] snapshot;
        byte[][] spawns;
        uint entryGeneration;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            entryGeneration = NetworkGeneration(go);
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        ulong generation = SceneGenerationOf(snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => host.Has(FakePeer.Ready), host);
            NetPeer peer = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.SpawnStarted, w => w.WriteUInt64(generation), peer);
            foreach (byte[] spawn in spawns) host.Session.SendRaw(peer, FakePeer.Spawn, spawn);
            host.Session.Flush();
            Pump(() => Scene.Main.Objects.Any(o => o.Identifier == go.Identifier), host);
            NetCollectionTestComponent here = Find(Scene.Main, go.Identifier).GetComponent<NetCollectionTestComponent>()!;

            // The value changes on the host while the run is still going.
            using NetWriter value = new();
            NetSerializer<int>.Write(value, 55);
            host.SendSyncValues(peer, 0, 1, (component.Identifier, NetCollectionTestComponent.HealthSlot, entryGeneration, value.ToArray()));
            Pump(() => here.Health == 55, host);
            Assert.False(Networking.Networking.IsReady);
            Assert.Empty(here.HealthChanges);

            host.Send(FakePeer.SpawnFinished, w => w.WriteUInt64(generation), peer);
            Pump(() => Networking.Networking.IsReady, host);
            Assert.Equal([(100, 55)], here.HealthChanges);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AHeldHookDoesNotRunForAComponentDestroyedBeforeTheRunEnds()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Changing");
        go.Network.Networked = true;
        NetCollectionTestComponent component = go.AddComponent<NetCollectionTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);

        byte[] snapshot;
        byte[][] spawns;
        uint entryGeneration;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            entryGeneration = NetworkGeneration(go);
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        ulong generation = SceneGenerationOf(snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => host.Has(FakePeer.Ready), host);
            NetPeer peer = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.SpawnStarted, w => w.WriteUInt64(generation), peer);
            foreach (byte[] spawn in spawns) host.Session.SendRaw(peer, FakePeer.Spawn, spawn);
            host.Session.Flush();
            Pump(() => Scene.Main.Objects.Any(o => o.Identifier == go.Identifier), host);
            NetCollectionTestComponent here = Find(Scene.Main, go.Identifier).GetComponent<NetCollectionTestComponent>()!;

            // The value changes on the host while the run is still going.
            using NetWriter value = new();
            NetSerializer<int>.Write(value, 55);
            host.SendSyncValues(peer, 0, 1, (component.Identifier, NetCollectionTestComponent.HealthSlot, entryGeneration, value.ToArray()));
            Pump(() => here.Health == 55, host);
            Assert.False(Networking.Networking.IsReady);
            Assert.Empty(here.HealthChanges);

            // The host destroys the object before the run ends, so there is nothing left to tell.
            using NetWriter despawn = new();
            new DespawnMessage(go.Identifier).Write(despawn);
            byte[] despawned = despawn.ToArray();
            host.Send(FakePeer.Despawn, w => w.WriteBytes(despawned), peer);
            Pump(() => here.IsNotValid(), host);

            host.Send(FakePeer.SpawnFinished, w => w.WriteUInt64(generation), peer);
            Pump(() => Networking.Networking.IsReady, host);
            Assert.Empty(here.HealthChanges);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AJoiningOwnerHasItsOwnerOnlyValuesBeforeItsSpawnsFinish()
    {
        Scene scene = CreateScene();
        GameObject gun = CreateGameObject("Gun");
        gun.Network.Networked = true;
        AmmoTestComponent ammo = gun.AddComponent<AmmoTestComponent>();
        ammo.Ammo = 30;
        scene.Add(gun);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));

        // Handed to the peer as it joins, so its join run spawns the gun as its own.
        Networking.Networking.Session!.PeerJoined += peer => gun.Network.AssignOwnership(peer);
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            PumpFor(20, alice);
            List<ushort> order = [.. alice.WireReceived.Select(m => m.Id)];
            // One sent before the gun's spawn names an object the peer does not have yet, so it must follow the spawn.
            int spawned = order.IndexOf(FakePeer.Spawn);
            int values = alice.WireReceived.FindIndex(spawned, m => m.Id == FakePeer.Sync &&
                FakePeer.SyncValues(m.Data).Any(v => v.Component == ammo.Identifier && v.Slot == AmmoTestComponent.AmmoSlot));
            Assert.True(values >= 0, "The owner only value never arrived after the spawn.");
            Assert.True(values < order.IndexOf(FakePeer.SpawnFinished), "The owner only value came after the spawns finished.");
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
