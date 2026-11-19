using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>A [Sync] GameObject reference, as the script compiler generates for <c>[Sync] public GameObject? Target { get; set; }</c>.</summary>
public class SyncReferenceTestComponent : Component
{
    public const uint TargetSlot = 401;
    private GameObject? _target;
    private static readonly SyncMember<SyncReferenceTestComponent, GameObject?> TargetState = new(TargetSlot, "Target",
        SyncFlags.FromOwner, static c => c._target, static (c, v) => c._target = v);
    public GameObject? Target { get => _target; set => TargetState.Set(this, ref _target, value); }
}

public partial class NetworkingTests
{
    private static byte[] IntValue(int value)
    {
        using NetWriter writer = new();
        NetSerializer<int>.Write(writer, value);
        return writer.ToArray();
    }

    /// <summary>A host-owned crate a ready client knows about.</summary>
    private (SceneInstance Scene, GameObject Crate, NetTestComponent Component) SpawnCrate(LocalNetwork network, NetPeer? owner,
        params FakePeer[] peers)
    {
        SceneInstance scene = SceneInstance.Main;
        Pump(() => peers.All(p => p.IsReady), peers);
        GameObject crate = CreateGameObject("Crate");
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        scene.Add(crate);
        crate.Network.SpawnSerialized(owner);
        Pump(() => peers.All(p => p.Has(FakePeer.Spawn)), peers);
        return (scene, crate, component);
    }

    [Fact]
    public void HostRelaysAClientsValuesToOthersButNotBackAndHostOnlyValuesReachTheOwner()
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
