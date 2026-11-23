using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ANetworkedObjectWithoutANetworkTransformKeepsItsTransformToItself()
    {
        Scene scene = CreateScene();
        GameObject plain = CreateGameObject("Plain"), moving = CreateGameObject("Moving");
        plain.Network.Networked = true;
        moving.Network.Networked = true;
        moving.AddComponent<NetworkTransform>();
        scene.Add(plain); scene.Add(moving); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer owner = FakePeer.Join(network, "owner");
            Pump(() => owner.IsReady, owner);
            uint plainId = NetworkTestScene.Current.NetIdOf(plain), movingId = NetworkTestScene.Current.NetIdOf(moving);
            float x = 0;
            Pump(() =>
            {
                x += 0.5f;
                plain.Transform.LocalPosition = moving.Transform.LocalPosition = new Float3(x, 0, 0);
                return TicksCarrying(owner, movingId).Count >= 3;
            }, owner);
            Assert.Empty(TicksCarrying(owner, plainId));

            // A transform its owner sends for it has nowhere to go either.
            plain.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            Float3 kept = plain.Transform.LocalPosition;
            owner.Send(FakePeer.Transform, w => WriteTransforms(w, 1, 1,
                (plainId, NetworkGeneration(plain), new Float3(50, 60, 70), Quaternion.Identity, Float3.One)));
            PumpFor(15, owner);
            Assert.True(Networking.Networking.IsActive);
            Assert.Equal(kept, plain.Transform.LocalPosition);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AChildMovesThroughASecondNetworkTransformWithATarget()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Tank"), turret = CreateGameObject("Turret"), wheel = CreateGameObject("Wheel");
        turret.SetParent(root); wheel.SetParent(root);
        root.Network.Networked = true;
        root.AddComponent<NetworkTransform>();
        root.AddComponent<NetworkTransform>().Target = turret;
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer owner = FakePeer.Join(network, "owner");
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => owner.IsReady && observer.IsReady, owner, observer);
            uint netId = NetworkTestScene.Current.NetIdOf(root);

            // The host sends the turret as the object's second transform, and a child without one not at all.
            float x = 0;
            Pump(() =>
            {
                x += 0.5f;
                turret.Transform.LocalPosition = wheel.Transform.LocalPosition = new Float3(x, 0, 0);
                return observer.WireReceived.Where(m => m.Id == FakePeer.Transform)
                    .SelectMany(m => ReadTransforms(m.Data)).Count(r => r.NetId == netId && r.Index == 1) >= 3;
            }, owner, observer);
            List<TransformRecord> sent = observer.WireReceived.Where(m => m.Id == FakePeer.Transform)
                .SelectMany(m => ReadTransforms(m.Data)).Where(r => r.NetId == netId).ToList();
            Assert.DoesNotContain(sent, r => r.Index > 1);
            Assert.DoesNotContain(sent, r => r.Index == 0 && r.Position != null && r.Position.Value.X != 0);

            // An owner's record for the second transform moves the turret, and leaves the root where it is.
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            Float3 rootPosition = root.Transform.LocalPosition, wheelPosition = wheel.Transform.LocalPosition;
            owner.Send(FakePeer.Transform, w => WriteIndexedTransforms(w, 1, 1, 1,
                (netId, NetworkGeneration(root), new Float3(7, 8, 9), Quaternion.Identity, Float3.One)));
            Pump(() => turret.Transform.LocalPosition == new Float3(7, 8, 9), owner, observer);
            Assert.Equal(rootPosition, root.Transform.LocalPosition);
            Assert.Equal(wheelPosition, wheel.Transform.LocalPosition);

            // An index the object has no transform for is dropped.
            owner.Send(FakePeer.Transform, w => WriteIndexedTransforms(w, 2, 2, 5,
                (netId, NetworkGeneration(root), new Float3(1, 1, 1), Quaternion.Identity, Float3.One)));
            PumpFor(15, owner, observer);
            Assert.True(Networking.Networking.IsActive);
            Assert.Equal(new Float3(7, 8, 9), turret.Transform.LocalPosition);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientBuildsATargetedNetworkTransformFromItsSpawn()
    {
        Scene scene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        GameObject original = CreateGameObject("Tank"), turret = CreateGameObject("Turret");
        turret.SetParent(original);
        original.Network.Networked = true;
        original.AddComponent<NetworkTransform>();
        original.AddComponent<NetworkTransform>().Target = turret;
        using NetWriter spawn = new();
        NetworkTestScene.Current.WriteSpawn(original, -1, NetworkOrphaned.ClearOwner, spawn);
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
            Pump(() => Networking.Networking.IsReady && Scene.Main.Objects.Any(o => o.Identifier == original.Identifier), host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            GameObject proxy = Find(Scene.Main, original.Identifier);
            GameObject proxyTurret = Assert.Single(proxy.Children);
            NetworkTransform[] transforms = proxy.GetComponents<NetworkTransform>().ToArray();
            Assert.Equal(2, transforms.Length);
            Assert.Same(proxyTurret, transforms[1].Target);

            uint netId = NetworkTestScene.Current.NetIdOf(proxy);
            host.Send(FakePeer.Transform, w => WriteIndexedTransforms(w, 1, 1, 1,
                (netId, 0, new Float3(4, 5, 6), Quaternion.Identity, Float3.One)), client);
            Pump(() => proxyTurret.Transform.LocalPosition == new Float3(4, 5, 6), host);
            Assert.Equal(Float3.Zero, proxy.Transform.LocalPosition);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private static GameObject BuildTank(in NetworkSpawn spawn)
    {
        GameObject tank = new("Tank"), turret = new("Turret");
        turret.SetParent(tank);
        tank.Transform.Position = spawn.Position;
        tank.AddComponent<NetworkTransform>();
        tank.AddComponent<NetworkTransform>().Target = turret;
        return tank;
    }

    private static GameObject BuildSprouting(in NetworkSpawn spawn)
    {
        GameObject go = new("Sprouting");
        go.AddComponent<NetworkTransform>();
        go.AddComponent<SproutsATransform>();
        return go;
    }

    [Fact]
    public void ATransformGameCodeAddsOnceTheObjectRunsIsNotNetworkedOnAnyPeer()
    {
        Networking.Networking.RegisterFactory("sprouting", BuildSprouting);
        MakeCurrent(CreateScene());
        byte[] snapshot, spawn;
        Guid id;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            GameObject sprouting = Networking.Networking.SpawnFactory("sprouting", Float3.Zero, Quaternion.Identity);
            id = sprouting.Identifier;
            Assert.Equal(2, sprouting.GetComponents<NetworkTransform>().Count());

            // The list is the object's as built, which is what every peer's factory gives.
            Assert.Single(Networking.Networking.System!.FindEntry(sprouting)!.Transforms);
            (snapshot, byte[][] spawns) = RecordJoin(source);
            spawn = spawns.Single();
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot, spawn);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            GameObject copy = Find(Scene.Main, id);
            Assert.Equal(2, copy.GetComponents<NetworkTransform>().Count());
            Assert.Single(Networking.Networking.System!.FindEntry(copy)!.Transforms);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASpawnCarriesThePoseOfAChildATransformMoves()
    {
        Networking.Networking.RegisterFactory("tank", BuildTank);
        MakeCurrent(CreateScene());
        byte[] snapshot, spawn;
        Guid id;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            using FakePeer recorder = FakePeer.Join(source, "recorder");
            Pump(() => recorder.Has(FakePeer.Scene), recorder);
            snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data;
            GameObject tank = Networking.Networking.SpawnFactory("tank", new Float3(1, 0, 0), Quaternion.Identity);
            id = tank.Identifier;
            tank.Children[0].Transform.LocalPosition = new Float3(0, 2, 0);
            tank.Children[0].Transform.LocalScale = new Float3(2, 2, 2);
            using NetWriter written = new();
            NetworkTestScene.Current.WriteSpawn(tank, -1, NetworkOrphaned.Destroy, written, NetworkGeneration(tank),
                NetworkTestScene.Current.NetIdOf(tank));
            spawn = written.ToArray();
        }
        finally { Networking.Networking.Shutdown(); }

        // The joiner builds the turret where its factory puts it, so where the host has it comes with the spawn.
        MakeCurrent(CreateScene());
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
            Pump(() => Scene.Main.Objects.Any(o => o.Identifier == id), host);
            GameObject copy = Find(Scene.Main, id);
            Assert.Equal(new Float3(1, 0, 0), copy.Transform.LocalPosition);
            Assert.Equal(new Float3(0, 2, 0), copy.Children[0].Transform.LocalPosition);
            Assert.Equal(new Float3(2, 2, 2), copy.Children[0].Transform.LocalScale);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ANetworkTransformWithoutInterpolationAppliesTheNewestPoseAtOnce()
    {
        TimeSpan previous = Networking.Networking.InterpolationDelay;
        Networking.Networking.InterpolationDelay = TimeSpan.FromSeconds(10);
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            NetworkTransform transform = proxy.GetComponent<NetworkTransform>()!;
            transform.Interpolate = false;
            SendProxyTransform(host, client, proxy.Identifier, 1, 10);
            SendProxyTransform(host, client, proxy.Identifier, 2, 20);
            Pump(() => proxy.Transform.LocalPosition.X == 20, host);
            transform.Interpolate = true;
            SendProxyTransform(host, client, proxy.Identifier, 3, 30);
            SendProxyTransform(host, client, proxy.Identifier, 4, 40);
            PumpFor(15, host);
            Assert.Equal(30, proxy.Transform.LocalPosition.X);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.InterpolationDelay = previous; }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void TurningAChannelOffAndOnDoesNotReplayBufferedValues(NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            NetworkTransform transform = proxy.GetComponent<NetworkTransform>()!;
            SendProxyTransform(host, client, proxy.Identifier, 1, 10);
            Pump(() => proxy.Transform.LocalPosition.X == 10, host);
            transform.SyncPosition = false;
            proxy.Transform.LocalPosition = new Float3(3, 0, 0);
            SendProxyTransform(host, client, proxy.Identifier, 2, 20);
            Pump(() => transform.Sync.LastReceivedTick == 2, host);
            PumpFor(5, host);
            Assert.Equal(3, proxy.Transform.LocalPosition.X);
            transform.SyncPosition = true;
            PumpFor(10, host);
            Assert.Equal(3, proxy.Transform.LocalPosition.X);
            SendProxyTransform(host, client, proxy.Identifier, 3, 30);
            Pump(() => proxy.Transform.LocalPosition.X == 30, host);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void TeleportIsForThePeerThatControlsTheObject()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer _);
        using (host)
        try
        {
            NetworkTransform transform = proxy.GetComponent<NetworkTransform>()!;
            Assert.Throws<InvalidOperationException>(() => transform.Teleport(new Float3(1, 2, 3), Quaternion.Identity));
            Assert.Equal(Float3.Zero, proxy.Transform.Position);
        }
        finally { Networking.Networking.Shutdown(); }

        GameObject free = CreateGameObject("Free");
        free.AddComponent<NetworkTransform>().Teleport(new Float3(1, 2, 3), Quaternion.Identity);
        Assert.Equal(new Float3(1, 2, 3), free.Transform.Position);
    }

    [Fact]
    public void DefaultNetworkSettingsDoNotAllocateOrSerializeAnAccessor()
    {
        GameObject go = CreateGameObject();
        EchoObject serialized = Serializer.Serialize(go);
        Assert.Null(go._network);
        Assert.False(serialized.TryGet("Network", out _));
        using GameObject defaults = Serializer.Deserialize<GameObject>(serialized)!;
        Assert.Null(defaults._network);
        _ = go.Network;
        Assert.False(Serializer.Serialize(go).TryGet("Network", out _));
    }
}

/// <summary>Adds a NetworkTransform when it first runs, after the object was built.</summary>
public sealed class SproutsATransform : Component
{
    public override void OnEnable()
    {
        if (!AddedOne)
        {
            AddedOne = true;
            GameObject.AddComponent<NetworkTransform>();
        }
    }

    [Prowl.Echo.SerializeIgnore] public bool AddedOne;
}
