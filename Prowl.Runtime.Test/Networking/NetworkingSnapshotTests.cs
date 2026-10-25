using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static byte[] IntValue(int value)
    {
        using NetWriter writer = new();
        NetSerializer<int>.Write(writer, value);
        return writer.ToArray();
    }

    private static int SnapshotsHolding(FakePeer peer, Guid component, uint slot) =>
        peer.WireReceived.Count(m => m.Id == FakePeer.Snapshot &&
                                     peer.SnapshotValues(m.Data).Any(v => v.Component == component && v.Slot == slot));

    /// <summary>A host-owned crate a ready client knows about.</summary>
    private (Scene Scene, GameObject Crate, NetTestComponent Component) SpawnCrate(LocalNetwork network, NetPeer? owner,
        params FakePeer[] peers)
    {
        Scene scene = Scene.Current;
        Pump(() => peers.All(p => p.IsReady), peers);
        GameObject crate = CreateGameObject("Crate");
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        scene.Add(crate);
        crate.Network.Spawn(owner);
        Pump(() => peers.All(p => p.Has(FakePeer.Spawn)), peers);
        return (scene, crate, component);
    }

    [Fact]
    public void UnacknowledgedValuesAreResentUntilAcknowledged()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            (_, _, NetTestComponent component) = SpawnCrate(network, null, alice);

            // Unacknowledged, it keeps coming.
            alice.AutoAck = false;
            component.Health = 7;
            Pump(() => SnapshotsHolding(alice, component.Identifier, NetTestComponent.HealthSlot) >= 3, alice);
            Assert.Equal(7, alice.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot));

            // Acknowledged, it stops.
            alice.AutoAck = true;
            PumpFor(300, alice);
            alice.WireReceived.Clear();
            PumpFor(300, alice);
            Assert.Equal(0, SnapshotsHolding(alice, component.Identifier, NetTestComponent.HealthSlot));

            // A new change goes out once, and only the value that changed.
            component.Health = 8;
            PumpFor(300, alice);
            Assert.Equal(1, SnapshotsHolding(alice, component.Identifier, NetTestComponent.HealthSlot));
            Assert.Equal(0, SnapshotsHolding(alice, component.Identifier, NetTestComponent.ScoreSlot));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void OlderSnapshotsDoNotRollValuesBack()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            (_, GameObject crate, NetTestComponent component) = SpawnCrate(network, aliceHere, alice);
            uint generation = NetworkGeneration(crate);

            alice.SendSnapshotValues(null, 1, 10, (component.Identifier, NetTestComponent.HealthSlot, generation, IntValue(5)));
            Pump(() => component.Health == 5, alice);
            // Tick 9 was taken before tick 10, so it arrives late and is dropped; it's still acknowledged.
            alice.SendSnapshotValues(null, 2, 9, (component.Identifier, NetTestComponent.HealthSlot, generation, IntValue(3)));
            alice.SendSnapshotValues(null, 3, 11, (component.Identifier, NetTestComponent.HealthSlot, generation, IntValue(6)));
            Pump(() => component.Health == 6, alice);
            alice.SendSnapshotValues(null, 4, 9, (component.Identifier, NetTestComponent.HealthSlot, generation, IntValue(3)));
            PumpFor(50, alice);
            Assert.Equal(6, component.Health);
            Assert.Equal(SessionState.Active, alice.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void HostRelaysAClientsValuesToOthersButNotBackAndFromHostValuesReachTheOwner()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        using FakePeer bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            (_, GameObject crate, NetTestComponent component) = SpawnCrate(network, aliceHere, alice, bob);
            uint generation = NetworkGeneration(crate);

            alice.SendSnapshotValues(null, 1, 1, (component.Identifier, NetTestComponent.HealthSlot, generation, IntValue(42)));
            Pump(() => bob.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == 42, alice, bob);
            Assert.Equal(42, component.Health);

            // Only the host writes Score, so it goes to the owner too.
            component.Score = 9;
            Pump(() => alice.SnapshotInt(component.Identifier, NetTestComponent.ScoreSlot) == 9 &&
                       bob.SnapshotInt(component.Identifier, NetTestComponent.ScoreSlot) == 9, alice, bob);
            Assert.Null(alice.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ValuesForObjectsNotSpawnedYetAreRejectedSoTheyStayOwed()
    {
        Scene scene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
        try
        {
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            Guid unknown = Guid.NewGuid();
            host.SendSnapshotValues(client, 5, 1, (unknown, NetTestComponent.HealthSlot, 1, IntValue(1)));
            Pump(() => host.Has(FakePeer.SnapshotAck), host);

            NetReader ack = new(host.Last(FakePeer.SnapshotAck));
            Assert.Equal(1u, ack.ReadVarUInt32());
            Assert.Equal(5u, ack.ReadVarUInt32());
            Assert.Equal(1u, ack.ReadVarUInt32());
            Assert.Equal(unknown, ack.ReadGuid());
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SnapshotsConvergeOverReliableOnlyTransports()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using LimitedTransport transport = new(network, _ => 0, DeliverySupport.Reliable);
        Networking.Networking.Host(transport);
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            (_, _, NetTestComponent component) = SpawnCrate(network, null, alice);
            component.Health = 3;
            Pump(() => alice.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == 3, alice);
            Assert.All(alice.Deliveries.Where(m => m.Id == FakePeer.Snapshot), m => Assert.Equal(Delivery.Reliable, m.Delivery));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ComponentsAreNamedByGuidOnlyUntilAcknowledged()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            (_, _, NetTestComponent component) = SpawnCrate(network, null, alice);
            bool[] Definitions()
            {
                List<bool> defines = new();
                foreach ((ushort id, byte[] data) in alice.WireReceived.Where(m => m.Id == FakePeer.Snapshot))
                {
                    NetReader r = new(data);
                    r.ReadVarUInt32(); r.ReadVarUInt32(); r.ReadVarUInt32();
                    FakePeer.SkipTransformGroups(ref r);
                    if (r.ReadVarUInt32() == 0) continue;
                    defines.Add((r.ReadVarUInt32() & 1) != 0);
                }
                return defines.ToArray();
            }

            component.Health = 1;
            Pump(() => alice.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == 1, alice);
            PumpFor(200, alice);
            component.Health = 2;
            Pump(() => alice.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == 2, alice);

            // The first names it by guid; once that's acknowledged, its small id is enough.
            bool[] defines = Definitions();
            Assert.True(defines[0]);
            Assert.False(defines[^1]);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SnapshotsFromAnotherSceneAreIgnored()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            (_, GameObject crate, NetTestComponent component) = SpawnCrate(network, aliceHere, alice);
            uint generation = NetworkGeneration(crate);

            using NetWriter stale = new(), value = new();
            NetSerializer<int>.Write(value, 5);
            stale.WriteVarUInt32(1); stale.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration + 1); stale.WriteVarUInt32(1);
            stale.WriteVarUInt32(0);
            stale.WriteVarUInt32(1);
            stale.WriteVarUInt32(1 << 1 | 1); stale.WriteGuid(component.Identifier);
            stale.WriteVarUInt32(NetTestComponent.HealthSlot); stale.WriteVarUInt32(generation);
            stale.WriteBytesWithLength(value.WrittenSpan);
            alice.Session.SendRaw(alice.Session.HostPeer!, FakePeer.Snapshot, stale.WrittenSpan, Delivery.Unreliable);
            alice.Session.Flush();
            PumpFor(100, alice);

            Assert.Equal(100, component.Health);
            Assert.False(alice.Has(FakePeer.SnapshotAck));
            Assert.Equal(SessionState.Active, alice.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SteadySnapshotsDoNotAllocate()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            (_, GameObject crate, NetTestComponent component) = SpawnCrate(network, null, alice);
            Action send = Networking.Networking.System!.Snapshots.Send;
            long Tick(int i)
            {
                // A moving object and a changing value, acknowledged in between as they would be.
                crate.Transform.LocalPosition = new Float3(i, 0, 0);
                component.Health = i;
                long before = GC.GetAllocatedBytesForCurrentThread();
                send();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                alice.Session.Update();
                Networking.Networking.Receive();
                return allocated;
            }

            for (int i = 0; i < 200; i++) Tick(i);
            long total = 0;
            for (int i = 200; i < 400; i++) total += Tick(i);

            // What's left is the transport queueing the messages, not the snapshots.
            Assert.InRange(total / 200, 0, 64);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
