using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static void SendFlags(FakePeer host, NetPeer client, GameObject go, NetworkFlags flags,
        uint? generation = null, Delivery delivery = Delivery.Reliable) =>
        host.Session.SendRaw(client, FakePeer.NetworkFlags, FlagMessage(go.Identifier,
            generation ?? NetworkTestScene.Current.GenerationOf(go), flags), delivery);

    private static byte[] FlagMessage(Guid id, uint generation, NetworkFlags flags)
    {
        using NetWriter message = new();
        message.WriteGuid(id); message.WriteUInt32(generation); message.WriteByte((byte)flags);
        return message.ToArray();
    }

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void HostFlagChangesReplicateForRootsAndChildrenIncludingLoadingPeers(NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        var (_, state, network) = ReadinessHost();
        GameObject root = state.GameObject;
        GameObject child = CreateGameObject("Flag child");
        // Build a separate tree before spawning, so its child is covered by the root's generation.
        GameObject spawned = CreateGameObject("Flag root"); child.SetParent(spawned);
        Scene.Current.Add(spawned);
        using FakePeer client = FakePeer.Join(network, "client");
        client.AutoReady = false;
        try
        {
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            spawned.Network.Spawn();
            root.Network.Flags = NetworkFlags.NoScaleSync;
            child.Network.Flags = NetworkFlags.NoPositionSync | NetworkFlags.NoInterpolation;
            Pump(() => client.Received.Count(m => m.Id == FakePeer.NetworkFlags) == 2, client);
            Assert.Equal(NetworkFlags.NoScaleSync, root.Network.Flags);
            Assert.Equal(NetworkFlags.NoPositionSync | NetworkFlags.NoInterpolation, child.Network.Flags);
            Assert.All(client.Deliveries.Where(m => m.Id == FakePeer.NetworkFlags), m => Assert.Equal(Delivery.Reliable, m.Delivery));
            NetReader childUpdate = new(client.Last(FakePeer.NetworkFlags));
            Assert.Equal(child.Identifier, childUpdate.ReadGuid());
            Assert.Equal(NetworkGeneration(spawned), childUpdate.ReadUInt32());
            Assert.Equal((byte)child.Network.Flags, childUpdate.ReadByte());
            using Scene snapshot = RoundTrip(Scene.Current);
            Assert.Equal(root.Network.Flags, Find(snapshot, root.Identifier).Network.Flags);
            Assert.Equal(child.Network.Flags, Find(snapshot, child.Identifier).Network.Flags);
            root.Network.Flags = NetworkFlags.NoScaleSync; // A no-op does not broadcast again.
            PumpFor(10, client);
            Assert.Equal(2, client.Received.Count(m => m.Id == FakePeer.NetworkFlags));
            // A client cannot send flag changes back to the host, including for objects it owns.
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.AssignOwnership(peer);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.NetworkFlags,
                FlagMessage(root.Identifier, NetworkGeneration(root), NetworkFlags.None));
            client.Session.Flush(); PumpFor(10, client);
            Assert.Equal(NetworkFlags.NoScaleSync, root.Network.Flags);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void LiveHostFlagsDisableAndReenableChannelsWithoutReplayingBufferedValues(NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            SendProxyTransform(host, client, proxy.Identifier, 1, 10);
            Pump(() => proxy.Transform.LocalPosition.X == 10, host);
            Assert.Throws<InvalidOperationException>(() => proxy.Network.Flags = NetworkFlags.NoPositionSync);
            SendFlags(host, client, proxy, NetworkFlags.NoPositionSync);
            host.Session.Flush();
            Pump(() => proxy.Network.Flags == NetworkFlags.NoPositionSync, host);
            proxy.Transform.LocalPosition = new Float3(3, 0, 0);
            SendProxyTransform(host, client, proxy.Identifier, 2, 20);
            PumpFor(15, host);
            Assert.Equal(3, proxy.Transform.LocalPosition.X);
            SendFlags(host, client, proxy, NetworkFlags.None);
            host.Session.Flush();
            Pump(() => proxy.Network.Flags == NetworkFlags.None, host);
            PumpFor(10, host);
            Assert.Equal(3, proxy.Transform.LocalPosition.X);
            SendProxyTransform(host, client, proxy.Identifier, 3, 30);
            Pump(() => proxy.Transform.LocalPosition.X == 30, host);
            // A delayed policy update from another ownership generation must not apply.
            SendFlags(host, client, proxy, NetworkFlags.NoTransformSync, NetworkGeneration(proxy) + 1);
            host.Session.Flush(); PumpFor(10, host);
            Assert.Equal(NetworkFlags.None, proxy.Network.Flags);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void NoInterpolationAppliesNewestPoseImmediatelyAndCanBeDisabled()
    {
        TimeSpan previous = Networking.Networking.InterpolationDelay;
        Networking.Networking.InterpolationDelay = TimeSpan.FromSeconds(10);
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            SendFlags(host, client, proxy, NetworkFlags.NoInterpolation);
            host.Session.Flush(); Pump(() => proxy.Network.Flags == NetworkFlags.NoInterpolation, host);
            SendProxyTransform(host, client, proxy.Identifier, 1, 10);
            SendProxyTransform(host, client, proxy.Identifier, 2, 20);
            Pump(() => proxy.Transform.LocalPosition.X == 20, host);
            SendFlags(host, client, proxy, NetworkFlags.None);
            host.Session.Flush(); Pump(() => proxy.Network.Flags == NetworkFlags.None, host);
            SendProxyTransform(host, client, proxy.Identifier, 3, 30);
            SendProxyTransform(host, client, proxy.Identifier, 4, 40);
            PumpFor(15, host);
            Assert.Equal(30, proxy.Transform.LocalPosition.X);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.InterpolationDelay = previous; }
    }

    [Fact]
    public void ClientReceivesRootAndChildFlagsWhileSceneActivationIsQueued()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Flag root"); root.Network.Mode = NetworkMode.Object;
        GameObject child = CreateGameObject("Flag child"); child.SetParent(root);
        scene.Add(root); MakeCurrent(scene);
        using NetWriter snapshot = new();
        uint generation;
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "source"));
            generation = NetworkGeneration(root);
            NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        }
        finally { Networking.Networking.Shutdown(); }
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.NetworkFlags, FlagMessage(root.Identifier, generation, NetworkFlags.NoScaleSync));
            host.Session.SendRaw(peer, FakePeer.NetworkFlags, FlagMessage(child.Identifier, generation, NetworkFlags.NoRotationSync));
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            for (int i = 0; i < 1000 && NetworkTestScene.Current.FindByIdentifier(child.Identifier) is not GameObject { Network: { Flags: NetworkFlags.NoRotationSync } }; i++)
            {
                Networking.Networking.Receive(); Networking.Networking.Flush(); host.Session.Update();
                Thread.Sleep(1);
            }
            Assert.Equal(NetworkJoinState.Loading, Networking.Networking.JoinState);
            Assert.True(Scene.IsLoadPending);
            Assert.Equal(NetworkFlags.NoRotationSync, ((GameObject)NetworkTestScene.Current.FindByIdentifier(child.Identifier)!).Network.Flags);
            Pump(() => Networking.Networking.IsReady, host);
            Assert.Equal(NetworkFlags.NoScaleSync, Find(Scene.Current, root.Identifier).Network.Flags);
            Assert.Equal(NetworkFlags.NoRotationSync, Find(Scene.Current, child.Identifier).Network.Flags);
            Assert.Throws<InvalidOperationException>(() => Find(Scene.Current, child.Identifier).Network.Flags = NetworkFlags.None);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(16, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    public void MalformedHostFlagMessagesEndTheClientWithoutApplyingFlags(byte flags, bool trailing, bool unreliable)
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            using NetWriter message = new();
            message.WriteBytes(FlagMessage(proxy.Identifier, NetworkGeneration(proxy), (NetworkFlags)flags));
            if (trailing) message.WriteByte(0);
            host.Session.SendRaw(client, FakePeer.NetworkFlags, message.WrittenSpan,
                unreliable ? Delivery.Unreliable : Delivery.Reliable);
            host.Session.Flush();
            Pump(() => Networking.Networking.Session == null, host);
            Assert.Equal(NetworkFlags.None, proxy.Network.Flags);
        }
        finally { Networking.Networking.Shutdown(); }
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
