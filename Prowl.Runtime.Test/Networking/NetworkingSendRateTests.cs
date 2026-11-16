using Prowl.Courier;
using Prowl.Echo;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static List<uint> TicksCarrying(FakePeer peer, uint netId) =>
        peer.WireReceived.Where(m => m.Id == FakePeer.Snapshot && ReadTransforms(m.Data).Any(r => r.NetId == netId))
            .Select(m => SnapshotTick(m.Data)).ToList();

    private static List<uint> TicksCarrying(FakePeer peer, Guid component, uint slot) =>
        peer.WireReceived.Where(m => m.Id == FakePeer.Snapshot && peer.SnapshotValues(m.Data).Any(v => v.Component == component && v.Slot == slot))
            .Select(m => SnapshotTick(m.Data)).ToList();

    private static uint SnapshotTick(byte[] snapshot)
    {
        NetReader r = new(snapshot);
        r.ReadVarUInt32(); r.ReadVarUInt32();
        return r.ReadVarUInt32();
    }

    [Fact]
    public void SendIntervalIsValidatedAndStoredOnlyWhenSet()
    {
        GameObject go = CreateGameObject("Rated");
        Assert.Equal(1, go.Network.SendInterval);
        Assert.Throws<ArgumentOutOfRangeException>(() => go.Network.SendInterval = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => go.Network.SendInterval = 256);

        go.Network.SendInterval = 4;
        GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.Equal(4, copy.Network.SendInterval);
        Assert.False(Serializer.Serialize(CreateGameObject("Plain")).TryGet("Network", out _));
    }

    [Fact]
    public void AMovingObjectSendsItsTransformOnlyEveryIntervalTicks()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject slow = CreateGameObject("Slow"), fast = CreateGameObject("Fast");
            slow.Network.SendInterval = 3;
            scene.Add(slow); scene.Add(fast);
            slow.Network.Spawn(); fast.Network.Spawn();
            uint slowId = NetworkTestScene.Current.NetIdOf(slow), fastId = NetworkTestScene.Current.NetIdOf(fast);

            float x = 0;
            Pump(() =>
            {
                x += 0.5f;
                slow.Transform.LocalPosition = fast.Transform.LocalPosition = new Float3(x, 0, 0);
                return TicksCarrying(alice, slowId).Count >= 4;
            }, alice);

            List<uint> slowTicks = TicksCarrying(alice, slowId), fastTicks = TicksCarrying(alice, fastId);
            Assert.All(slowTicks.Zip(slowTicks.Skip(1)), pair => Assert.Equal(0u, (pair.Second - pair.First) % 3));
            Assert.True(fastTicks.Count >= slowTicks.Count * 2, $"fast {fastTicks.Count}, slow {slowTicks.Count}");
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASyncValueChangingEveryTickWaitsForItsObjectsInterval()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = CreateGameObject("Crate");
            crate.Network.SendInterval = 4;
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.Network.Spawn();

            Pump(() =>
            {
                component.Health++;
                return TicksCarrying(alice, component.Identifier, NetTestComponent.HealthSlot).Count >= 4;
            }, alice);

            List<uint> ticks = TicksCarrying(alice, component.Identifier, NetTestComponent.HealthSlot);
            Assert.All(ticks.Zip(ticks.Skip(1)), pair => Assert.Equal(0u, (pair.Second - pair.First) % 4));
            Pump(() => alice.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == component.Health, alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ClientsLearnTheSendIntervalAtSpawnAndWhenTheHostChangesIt()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork shared = new();
        Networking.Networking.Host(new LocalTransport(shared, "host"));
        using FakePeer alice = FakePeer.Join(shared, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = CreateGameObject("Crate");
            crate.Network.SendInterval = 5;
            scene.Add(crate);
            crate.Network.Spawn();
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            Assert.Contains("SendInterval", SpawnJson(alice, crate));

            crate.Network.SendInterval = 2;
            Pump(() => alice.Has(FakePeer.SendInterval), alice);
            NetReader message = new(alice.Last(FakePeer.SendInterval));
            Assert.Equal(crate.Identifier, message.ReadGuid());
            message.ReadUInt32();
            Assert.Equal(2, message.ReadByte());
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
