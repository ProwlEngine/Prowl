using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ASceneAssetIsLoadedByTheClientAndItsNetworkedObjectsArriveBySceneSpawn()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        try
        {
            SceneInstance authoring = new();
            authoring.Create("Arena Level");
            GameObject authoredDoor = authoring.Create("Door");
            authoredDoor.Network.Networked = true;
            authoredDoor.AddComponent<NetTestComponent>();
            GameObject authoredGone = authoring.Create("Gone");
            authoredGone.Network.Networked = true;
            authoring.Create("Secret").Network.ServerOnly = true;
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            SceneInstance world = CreateScene();
            MakeCurrent(world);
            byte[] scene;
            byte[][] spawns;
            Guid doorId, goneId;
            uint doorNetId;
            LocalNetwork source = new();
            Networking.Networking.Host(new LocalTransport(source, "host"));
            try
            {
                arena.LoadAdditive(world).Wait();
                GameObject door = world.Objects.Single(o => o.Name == "Door");
                GameObject gone = world.Objects.Single(o => o.Name == "Gone");
                (doorId, goneId) = (door.Identifier, gone.Identifier);
                Assert.True(door.Network.Active);
                door.GetComponent<NetTestComponent>()!.Score = 7;
                doorNetId = NetworkTestScene.Current.NetIdOf(door);
                gone.Dispose();

                (scene, spawns) = RecordJoin(source);

                // The scene message names the asset, and the door's spawn names the object without carrying it.
                NetReader message = new(scene);
                message.ReadByte(); message.ReadUInt64();
                uint sections = message.ReadVarUInt32();
                List<SectionHeader> headers = new();
                for (uint i = 0; i < sections; i++) headers.Add(SectionHeader.Read(ref message));
                Assert.True(message.IsAtEnd);
                Assert.Equal(arena.AssetID, headers.Single(h => h.Name == "Arena").Source);
                Assert.True(ReadTemplateSpawn(spawns.Single(s => SpawnedId(s) == doorId)).Scene);
                Assert.DoesNotContain(spawns, s => SpawnedId(s) == goneId);
            }
            finally { Networking.Networking.Shutdown(); }

            // The client has a world of its own, so what is in it afterwards it loaded or was spawned.
            MakeCurrent(CreateScene());
            LocalNetwork network = new();
            using FakePeer host = FakePeer.Host(network);
            SendJoin(host, scene, spawns);
            try
            {
                Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
                Pump(() => Networking.Networking.IsReady, host);
                SceneInstance mine = SceneInstance.Main;
                Assert.Contains(mine.Objects, o => o.Name == "Arena Level");
                Assert.DoesNotContain(mine.Objects, o => o.Name == "Secret");
                Assert.DoesNotContain(mine.Objects, o => o.Identifier == goneId);

                GameObject doorHere = Find(mine, doorId);
                Assert.True(doorHere.Network.Active);
                Assert.Equal(doorNetId, NetworkTestScene.Current.NetIdOf(doorHere));
                Assert.Equal(7, doorHere.GetComponent<NetTestComponent>()!.Score);
                Assert.Equal("Arena", doorHere.Section!.Name);

                // Despawned, a scene object is switched off and kept, and the next spawn brings the same one back.
                NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
                using NetWriter despawn = new();
                new DespawnMessage(doorId, NetworkDespawnReason.OutOfInterest).Write(despawn);
                byte[] despawned = despawn.ToArray();
                host.Send(FakePeer.Despawn, w => w.WriteBytes(despawned), client);
                Pump(() => !doorHere.Network.Active, host);
                Assert.True(doorHere.IsValid());
                Assert.False(doorHere.Enabled);

                byte[] again = spawns.Single(s => SpawnedId(s) == doorId);
                host.Send(FakePeer.Spawn, w => w.WriteBytes(again), client);
                Pump(() => doorHere.Network.Active, host);
                Assert.True(doorHere.Enabled);
                Assert.Same(doorHere, Find(mine, doorId));

                // The copy of the one the host never spawned is still held, out of the world, and goes with the session.
                GameObject goneHere = NetworkTestScene.Current._held[goneId];
                Assert.False(goneHere.IsDisposed);
                Networking.Networking.Shutdown();
                Assert.True(goneHere.IsDisposed);
                Assert.False(doorHere.IsDisposed);
            }
            finally { Networking.Networking.Shutdown(); }
        }
        finally
        {
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APersistentSceneObjectTheClientHoldsOutlastsASceneChange(bool despawnedBeforeTheChange)
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        try
        {
            SceneInstance authoring = new();
            GameObject authoredDoor = authoring.Create("Door");
            authoredDoor.Network.Networked = true;
            authoredDoor.AddComponent<NetworkLifecycleListener>();
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            MakeCurrent(CreateScene());
            byte[] scene;
            byte[] spawn;
            Guid doorId;
            LocalNetwork source = new();
            Networking.Networking.Host(new LocalTransport(source, "host"));
            try
            {
                arena.LoadAdditive(SceneInstance.Main).Wait();
                doorId = SceneInstance.Main.Objects.Single(o => o.Name == "Door").Identifier;
                (scene, byte[][] spawns) = RecordJoin(source);
                spawn = spawns.Single(s => SpawnedId(s) == doorId);
            }
            finally { Networking.Networking.Shutdown(); }

            MakeCurrent(CreateScene());
            LocalNetwork network = new();
            using FakePeer host = FakePeer.Host(network);
            SendJoin(host, scene, spawn);
            try
            {
                Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
                Pump(() => Networking.Networking.IsReady, host);
                GameObject door = Find(SceneInstance.Main, doorId);
                NetworkLifecycleListener listener = door.GetComponent<NetworkLifecycleListener>()!;
                NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);

                // The host keeps the door across loads, so it does here too.
                using NetWriter move = new();
                new SectionMoveMessage(doorId, NetworkSystem.PersistentSectionId).Write(move);
                host.Session.SendRaw(client, FakePeer.SectionMove, move.WrittenSpan);
                Pump(() => door.Persistent, host);

                if (despawnedBeforeTheChange)
                {
                    using NetWriter despawn = new();
                    new DespawnMessage(doorId, NetworkDespawnReason.OutOfInterest).Write(despawn);
                    host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
                    Pump(() => !door.Network.Active, host);
                }

                // The host changes scene and does not spawn the door for this peer, which is out of its interest.
                host.JoinSpawns.Clear();
                host.Session.SendRaw(client, FakePeer.Scene, CodeBuiltScene(2));
                Pump(() => NetworkTestScene.Current.SceneGeneration == 2 && Networking.Networking.IsReady, host);
                PumpFor(30, host);
                Assert.True(door.IsValid());
                Assert.False(door.Network.Active);
                Assert.False(door.Enabled);

                // Back in interest, it is the same door, and the session goes on.
                host.Session.SendRaw(client, FakePeer.Spawn, spawn);
                Pump(() => door.Network.Active, host);
                Assert.True(door.Enabled);
                Assert.True(Networking.Networking.IsActive);
                Assert.Equal(new[] { "spawn", "despawn", "spawn" }, listener.Events);
            }
            finally { Networking.Networking.Shutdown(); }
        }
        finally
        {
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }

    [Fact]
    public void TheHostKeepsOneCopyOfAPersistentSceneObjectWhenItsSceneLoadsAgain()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        try
        {
            SceneInstance authoring = new();
            GameObject authoredDoor = authoring.Create("Door");
            authoredDoor.Network.Networked = true;
            authoredDoor.AddComponent<NetworkLifecycleListener>();
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            SceneInstance world = CreateScene();
            MakeCurrent(world);
            LocalNetwork network = new();
            Networking.Networking.Host(new LocalTransport(network, "host"));
            try
            {
                arena.Load(world).Wait();
                GameObject door = world.Objects.Single(o => o.Name == "Door");
                Guid doorId = door.Identifier;
                NetworkLifecycleListener listener = door.GetComponent<NetworkLifecycleListener>()!;
                door.Persistent = true;
                Assert.True(door.Network.Active);

                // The scene brings the door again. As on a client, the copy kept from before gives way to it, so
                // every peer has the one door and has seen it leave and arrive.
                arena.Load(world).Wait();
                GameObject fresh = Assert.Single(world.Objects, o => o.Identifier == doorId);
                Assert.NotSame(door, fresh);
                Assert.True(fresh.Network.Active);
                Assert.False(fresh.Persistent);
                Assert.Equal(new[] { "spawn", "authority gained", "authority lost", "despawn" }, listener.Events);
                Assert.Equal(new[] { "spawn", "authority gained" }, fresh.GetComponent<NetworkLifecycleListener>()!.Events);
            }
            finally { Networking.Networking.Shutdown(); }
        }
        finally
        {
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }

    [Fact]
    public void WhatASceneObjectAddsOnceItRunsIsNotNetworkedOnAnyPeer()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        try
        {
            SceneInstance authoring = new();
            GameObject authoredDoor = authoring.Create("Door");
            authoredDoor.Network.Networked = true;
            authoredDoor.AddComponent<NetTestComponent>();
            authoredDoor.AddComponent<SproutsATransform>();
            authoredDoor.AddComponent<SproutsState>();
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            MakeCurrent(CreateScene());
            byte[] scene;
            byte[][] spawns;
            Guid doorId;
            LocalNetwork source = new();
            Networking.Networking.Host(new LocalTransport(source, "host"));
            try
            {
                arena.LoadAdditive(SceneInstance.Main).Wait();
                GameObject door = SceneInstance.Main.Objects.Single(o => o.Name == "Door");
                doorId = door.Identifier;
                Assert.NotNull(door.GetComponent<NetworkTransform>());
                Assert.NotNull(door.GetComponent<SyncPairTestComponent>());

                // The host's door has run and grown, and a client's copy has not yet when it is spawned: what is
                // networked is the door as the scene asset stores it.
                NetworkObject entry = Networking.Networking.System!.FindEntry(door)!;
                Assert.Empty(entry.Transforms);
                Assert.Equal([door.GetComponent<NetTestComponent>()!], entry.Components);
                (scene, spawns) = RecordJoin(source);
            }
            finally { Networking.Networking.Shutdown(); }

            MakeCurrent(CreateScene());
            LocalNetwork network = new();
            using FakePeer host = FakePeer.Host(network);
            SendJoin(host, scene, spawns);
            try
            {
                Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
                Pump(() => Networking.Networking.IsReady, host);
                GameObject door = Find(SceneInstance.Main, doorId);
                Assert.True(door.Network.Active);
                NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);

                // Despawned and spawned again, the grown door on this peer is still the same networked object.
                using NetWriter despawn = new();
                new DespawnMessage(doorId, NetworkDespawnReason.OutOfInterest).Write(despawn);
                host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
                Pump(() => !door.Network.Active, host);
                host.Session.SendRaw(client, FakePeer.Spawn, spawns.Single(s => SpawnedId(s) == doorId));
                Pump(() => door.Network.Active, host);
                Assert.True(door.IsValid());
                Assert.True(Networking.Networking.IsActive);
                Assert.Empty(Networking.Networking.System!.FindEntry(door)!.Transforms);
            }
            finally { Networking.Networking.Shutdown(); }
        }
        finally
        {
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }
}

/// <summary>Adds a component with [Sync] values when it first runs, after the object was built.</summary>
public sealed class SproutsState : Component
{
    public override void OnEnable()
    {
        if (GameObject.GetComponent<SyncPairTestComponent>().IsNotValid())
        {
            GameObject.AddComponent<SyncPairTestComponent>();
        }
    }
}
