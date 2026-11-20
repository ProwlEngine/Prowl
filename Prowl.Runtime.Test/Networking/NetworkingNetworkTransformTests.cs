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
        SceneInstance scene = CreateScene();
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
            owner.Send(FakePeer.Snapshot, w => WriteTransforms(w, 1, 1,
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
        SceneInstance scene = CreateScene();
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
                return observer.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
                    .SelectMany(m => ReadTransforms(m.Data)).Count(r => r.NetId == netId && r.Index == 1) >= 3;
            }, owner, observer);
            List<TransformRecord> sent = observer.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
                .SelectMany(m => ReadTransforms(m.Data)).Where(r => r.NetId == netId).ToList();
            Assert.DoesNotContain(sent, r => r.Index > 1);
            Assert.DoesNotContain(sent, r => r.Index == 0 && r.Position != null && r.Position.Value.X != 0);

            // An owner's record for the second transform moves the turret, and leaves the root where it is.
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            Float3 rootPosition = root.Transform.LocalPosition, wheelPosition = wheel.Transform.LocalPosition;
            owner.Send(FakePeer.Snapshot, w => WriteIndexedTransforms(w, 1, 1, 1,
                (netId, NetworkGeneration(root), new Float3(7, 8, 9), Quaternion.Identity, Float3.One)));
            Pump(() => turret.Transform.LocalPosition == new Float3(7, 8, 9), owner, observer);
            Assert.Equal(rootPosition, root.Transform.LocalPosition);
            Assert.Equal(wheelPosition, wheel.Transform.LocalPosition);

            // An index the object has no transform for is dropped.
            owner.Send(FakePeer.Snapshot, w => WriteIndexedTransforms(w, 2, 2, 5,
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
        SceneInstance scene = CreateScene();
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
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == original.Identifier), host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            GameObject proxy = Find(SceneInstance.Main, original.Identifier);
            GameObject proxyTurret = Assert.Single(proxy.Children);
            NetworkTransform[] transforms = proxy.GetComponents<NetworkTransform>().ToArray();
            Assert.Equal(2, transforms.Length);
            Assert.Same(proxyTurret, transforms[1].Target);

            uint netId = NetworkTestScene.Current.NetIdOf(proxy);
            host.Send(FakePeer.Snapshot, w => WriteIndexedTransforms(w, 1, 1, 1,
                (netId, 0, new Float3(4, 5, 6), Quaternion.Identity, Float3.One)), client);
            Pump(() => proxyTurret.Transform.LocalPosition == new Float3(4, 5, 6), host);
            Assert.Equal(Float3.Zero, proxy.Transform.LocalPosition);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ALateSpawnNamesTheTransformsTheHostUsesAndNotOnesAddedSince()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Tank"), turret = CreateGameObject("Turret");
        turret.SetParent(root);
        root.Network.Networked = true;
        NetworkTransform turning = root.AddComponent<NetworkTransform>();
        turning.Target = turret;
        scene.Add(root); MakeCurrent(scene);
        byte[] snapshot, spawn;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        using (FakePeer recorder = FakePeer.Join(source, "recorder"))
        {
            try
            {
                Pump(() => recorder.IsReady, recorder);
                Assert.Single(Networking.Networking.System!.FindEntry(root)!.Transforms);

                // Added once it is spawned, and ahead of the first in the tree: the host never sends it.
                GameObject hatch = CreateGameObject("Hatch");
                hatch.SetParent(root);
                hatch.SetSiblingIndex(0);
                hatch.AddComponent<NetworkTransform>();
                using NetWriter written = new();
                NetworkTestScene.Current.WriteSpawn(root, -1, NetworkOrphaned.Destroy, written,
                    NetworkGeneration(root), NetworkTestScene.Current.NetIdOf(root));
                spawn = written.ToArray();
                snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.SceneSnapshot).Data;
            }
            finally { Networking.Networking.Shutdown(); }
        }

        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), peer);
            host.Send(FakePeer.Spawn, w => w.WriteBytes(spawn), peer);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == root.Identifier), host);
            GameObject copy = Find(SceneInstance.Main, root.Identifier);
            NetworkTransform only = Assert.Single(Networking.Networking.System!.FindEntry(copy)!.Transforms);
            Assert.Equal(turning.Identifier, only.Identifier);
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
