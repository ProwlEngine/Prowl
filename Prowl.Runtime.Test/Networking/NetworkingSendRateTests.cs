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
        transform.SyncInterval = -1;
        Assert.Equal(1, transform.Interval);
        transform.SyncInterval = 1000;
        Assert.Equal(255, transform.Interval);

        transform.SyncInterval = NetworkTestSpawn.Ticks(4);
        using GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.Equal(4, copy.GetComponent<NetworkTransform>()!.Interval);
        Assert.False(Serializer.Serialize(go).TryGet("Network", out _));
    }

    [Fact]
    public void AMovingObjectSendsItsTransformOnlyEveryIntervalTicks()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject slow = CreateGameObject("Slow"), fast = CreateGameObject("Fast");
            slow.AddComponent<NetworkTransform>().SyncInterval = NetworkTestSpawn.Ticks(3);
            fast.AddComponent<NetworkTransform>();
            scene.Add(slow); scene.Add(fast);
            slow.SpawnBuilt(); fast.SpawnBuilt();
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
        slow.SyncInterval = NetworkTestSpawn.Ticks(6);

        GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.Equal(6, NetworkAccessor.SendIntervalOf(copy.GetComponent<NetTestComponent>()!));
        Assert.Equal(1, NetworkAccessor.SendIntervalOf(copy.GetComponent<PlainTestComponent>()!));

        // It is the behaviour's own field, so the object stores no network settings for it.
        Assert.False(Serializer.Serialize(go).TryGet("Network", out _));
        copy.Dispose();
        go.Dispose();
    }

    [Fact]
    public void SendIntervalsSavedInTicksOnTheObjectMoveOntoTheirBehaviours()
    {
        GameObject go = CreateGameObject("Old");
        go.AddComponent<PlainTestComponent>();
        go.AddComponent<NetTestComponent>();
        go.Network.Networked = true;
        EchoObject saved = Serializer.Serialize(go);
        EchoObject entry = EchoObject.NewCompound(2);
        entry.Add("Index", new EchoObject(1));
        entry.Add("Ticks", new EchoObject((byte)6));
        EchoObject intervals = EchoObject.NewList();
        intervals.ListAdd(entry);
        saved["Network"].Add("ComponentIntervals", intervals);

        using GameObject copy = Serializer.Deserialize<GameObject>(saved)!;
        Assert.Equal(6, NetworkAccessor.SendIntervalOf(copy.GetComponent<NetTestComponent>()!));
        Assert.Equal(NetworkTestSpawn.Ticks(6), copy.GetComponent<NetTestComponent>()!.SyncInterval, 5);
        Assert.False(Serializer.Serialize(copy)["Network"].TryGet("ComponentIntervals", out _));
        go.Dispose();
    }

    [Fact]
    public void ACopiedSendIntervalFindsItsComponentWhereverItSits()
    {
        GameObject source = CreateGameObject("Source");
        source.AddComponent<PlainTestComponent>();
        NetTestComponent slow = source.AddComponent<NetTestComponent>();
        slow.SyncInterval = NetworkTestSpawn.Ticks(6);

        // An instance that came from it, with a component of its own put first.
        GameObject instance = CreateGameObject("Instance");
        VisibilityListener extra = instance.AddComponent<VisibilityListener>();
        PlainTestComponent plain = instance.AddComponent<PlainTestComponent>();
        NetTestComponent copied = instance.AddComponent<NetTestComponent>();
        plain.SourceIdentifier = source.GetComponent<PlainTestComponent>()!.Identifier;
        copied.SourceIdentifier = slow.Identifier;

        CopyMap map = new();
        map.Fill(source.GetComponent<PlainTestComponent>()!, plain);
        map.Fill(slow, copied);
        ObjectCopy.CopyTo(source, instance, map);

        Assert.Equal(6, NetworkAccessor.SendIntervalOf(copied));
        Assert.Equal(1, NetworkAccessor.SendIntervalOf(plain));
        Assert.Equal(1, NetworkAccessor.SendIntervalOf(extra));
        instance.Dispose();
        source.Dispose();
    }

    [Fact]
    public void ASyncValueChangingEveryTickWaitsForItsComponentsInterval()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            component.SyncInterval = NetworkTestSpawn.Ticks(4);
            Assert.Equal(4, NetworkAccessor.SendIntervalOf(component));
            scene.Add(crate);
            crate.SpawnBuilt();
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
    public void ASendIntervalStillSendsWhenTheTickSkipsAhead()
    {
        Scene scene = CreateScene();
        List<GameObject> objects = [];
        List<NetTestComponent> components = [];
        for (int i = 0; i < 2; i++)
        {
            GameObject go = CreateGameObject($"Slow {i}");
            go.Network.Networked = true;
            go.AddComponent<NetworkTransform>().SyncInterval = NetworkTestSpawn.Ticks(2);
            NetTestComponent component = go.AddComponent<NetTestComponent>();
            component.SyncInterval = NetworkTestSpawn.Ticks(2);
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
