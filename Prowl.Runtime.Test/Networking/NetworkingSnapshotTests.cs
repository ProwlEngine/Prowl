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
        crate.SpawnBuilt(owner);
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

            alice.SendSyncValues(null, 1, 1, (component.Identifier, NetTestComponent.HealthSlot, generation, IntValue(42)));
            Pump(() => bob.SyncInt(component.Identifier, NetTestComponent.HealthSlot) == 42, alice, bob);
            Assert.Equal(42, component.Health);

            // Only the host writes Score, so it goes to the owner too.
            component.Score = 9;
            Pump(() => alice.SyncInt(component.Identifier, NetTestComponent.ScoreSlot) == 9 &&
                       bob.SyncInt(component.Identifier, NetTestComponent.ScoreSlot) == 9, alice, bob);
            Assert.Null(alice.SyncInt(component.Identifier, NetTestComponent.HealthSlot));
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
            Pump(() => alice.SyncInt(component.Identifier, NetTestComponent.HealthSlot) == 3, alice);
            Assert.All(alice.Deliveries.Where(m => m.Id == FakePeer.Transform), m => Assert.Equal(Delivery.Reliable, m.Delivery));
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

            // One snapshot asking to be acknowledged, under the scene given.
            uint netId = NetworkTestScene.Current.NetIdOf(crate);
            void Send(uint scene)
            {
                using NetWriter snapshot = new();
                snapshot.WriteVarUInt32(1); snapshot.WriteVarUInt32(scene); snapshot.WriteVarUInt32(1);
                snapshot.WriteDouble(1); snapshot.WriteVarUInt32(1);
                snapshot.WriteVarUInt32(netId); snapshot.WriteVarUInt32(generation);
                snapshot.WriteByte(PositionChannel | RotationChannel | ScaleChannel | ExactChannel);
                NetSerializers.WriteFloat3(snapshot, new Float3(5, 6, 7));
                NetSerializers.WriteQuaternion(snapshot, Quaternion.Identity);
                NetSerializers.WriteFloat3(snapshot, Float3.One);
                alice.Session.SendRaw(alice.Session.HostPeer!, FakePeer.Transform, snapshot.WrittenSpan, Delivery.Unreliable);
                alice.Session.Flush();
            }

            Float3 before = crate.Transform.LocalPosition;
            Send((uint)NetworkTestScene.Current.SceneGeneration + 1);
            PumpFor(100, alice);
            Assert.Equal(before, crate.Transform.LocalPosition);
            Assert.False(alice.Has(FakePeer.TransformAck));
            Assert.Equal(SessionState.Active, alice.Session.State);

            // The same snapshot under this scene is taken, and acknowledged.
            Send((uint)NetworkTestScene.Current.SceneGeneration);
            Pump(() => alice.Has(FakePeer.TransformAck), alice);
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
            Action send = Networking.Networking.System!.Transforms.Send;
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
