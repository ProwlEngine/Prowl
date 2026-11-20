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
        peer.WireReceived.Where(m => m.Id == FakePeer.Transform && ReadTransforms(m.Data).Any(r => r.NetId == netId))
            .Select(m => TransformTick(m.Data)).ToList();

    private static uint TransformTick(byte[] snapshot)
    {
        NetReader r = new(snapshot);
        r.ReadVarUInt32(); r.ReadVarUInt32();
        return r.ReadVarUInt32();
    }

    [Fact]
    public void ANetworkTransformsSendIntervalIsClampedAndSavedWithIt()
    {
        GameObject go = CreateGameObject("Rated");
        NetworkTransform transform = go.AddComponent<NetworkTransform>();
        Assert.Equal(1, transform.Interval);
        transform.SendInterval = 0;
        Assert.Equal(1, transform.Interval);
        transform.SendInterval = 256;
        Assert.Equal(255, transform.Interval);

        transform.SendInterval = 4;
        using GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.Equal(4, copy.GetComponent<NetworkTransform>()!.Interval);
        Assert.False(Serializer.Serialize(go).TryGet("Network", out _));
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
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject slow = CreateGameObject("Slow"), fast = CreateGameObject("Fast");
            slow.AddComponent<NetworkTransform>().SendInterval = 3;
            fast.AddComponent<NetworkTransform>();
            scene.Add(slow); scene.Add(fast);
            slow.Network.SpawnSerialized(); fast.Network.SpawnSerialized();
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
    public void AComponentsSendIntervalIsSavedWithItsObject()
    {
        GameObject go = CreateGameObject("Saved");
        go.AddComponent<PlainTestComponent>();
        NetTestComponent slow = go.AddComponent<NetTestComponent>();
        go.Network.SetSendInterval(slow, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => go.Network.SetSendInterval(slow, 0));
        Assert.Throws<ArgumentException>(() => go.Network.SetSendInterval(CreateGameObject().AddComponent<NetTestComponent>(), 2));

        GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.Equal(6, copy.Network.GetSendInterval(copy.GetComponent<NetTestComponent>()!));
        Assert.Equal(1, copy.Network.GetSendInterval(copy.GetComponent<PlainTestComponent>()!));

        // Back at 1 it is the default, and nothing is stored for it.
        go.Network.SetSendInterval(slow, 1);
        Assert.False(Serializer.Serialize(go).TryGet("Network", out _));
        copy.Dispose();
        go.Dispose();
    }

    [Fact]
    public void ASyncValueChangingEveryTickWaitsForItsComponentsInterval()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            crate.Network.SetSendInterval(component, 4);
            Assert.Equal(4, crate.Network.GetSendInterval(component));
            scene.Add(crate);
            crate.Network.SpawnSerialized();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            // Changing every tick, it still goes out only every fourth, so three more sends take twelve ticks or more.
            int Sends() => alice.WireReceived.Count(m => m.Id == FakePeer.Sync &&
                FakePeer.SyncValues(m.Data).Any(v => v.Component == component.Identifier && v.Slot == NetTestComponent.HealthSlot));
            Pump(() =>
            {
                component.Health++;
                return Sends() >= 1;
            }, alice);
            uint first = Networking.Networking.Tick;
            Pump(() =>
            {
                component.Health++;
                return Sends() >= 4;
            }, alice);
            Assert.True(Networking.Networking.Tick - first >= 12, $"Three more sends took {Networking.Networking.Tick - first} ticks");
            Pump(() => alice.SyncInt(component.Identifier, NetTestComponent.HealthSlot) == component.Health, alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ClientsLearnATransformsSendIntervalWithItsSpawn()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork shared = new();
        Networking.Networking.Host(new LocalTransport(shared, "host"));
        using FakePeer alice = FakePeer.Join(shared, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject crate = CreateGameObject("Crate");
            crate.AddComponent<NetworkTransform>().SendInterval = 5;
            scene.Add(crate);
            crate.Network.SpawnSerialized();
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            Assert.Contains("SendInterval", SpawnJson(alice, crate));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASendIntervalStillSendsWhenTheTickSkipsAhead()
    {
        SceneInstance scene = CreateScene();
        List<GameObject> objects = [];
        List<NetTestComponent> components = [];
        for (int i = 0; i < 2; i++)
        {
            GameObject go = CreateGameObject($"Slow {i}");
            go.Network.Networked = true;
            go.AddComponent<NetworkTransform>().SendInterval = 2;
            NetTestComponent component = go.AddComponent<NetTestComponent>();
            go.Network.SetSendInterval(component, 2);
            scene.Add(go);
            objects.Add(go);
            components.Add(component);
        }

        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetworkSystem system = Networking.Networking.System!;

            // Two network ticks pass each frame, as at half the tick rate, so the tick only takes every other value.
            for (int frame = 1; frame <= 40; frame++)
            {
                for (int i = 0; i < 2; i++)
                {
                    objects[i].Transform.LocalPosition = new Float3(frame, i, 0);
                    components[i].Health = frame;
                }

                system.NetworkTick = system.Tick + 2;
                system.Sync.SendSyncChanges();
                system.Transforms.Send();
                Networking.Networking.Session!.Flush();
                alice.Session.Update();
                Networking.Networking.Receive();
            }

            for (int i = 0; i < 2; i++)
            {
                uint netId = NetworkTestScene.Current.NetIdOf(objects[i]);
                Assert.True(TicksCarrying(alice, netId).Count >= 10, $"Object {i} sent {TicksCarrying(alice, netId).Count} transforms");
                Assert.True(alice.SyncInt(components[i].Identifier, NetTestComponent.HealthSlot) >= 38,
                    $"Object {i} last sent health {alice.SyncInt(components[i].Identifier, NetTestComponent.HealthSlot)}");
            }
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
