using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    public static IEnumerable<object[]> TransformChannelCases()
    {
        for (byte mask = 0; mask <= (byte)(NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation); mask++)
            foreach (string path in new[] { "Tick", "Rpc", "CatchUp", "Ownership" })
                yield return new object[] { (NetworkFlags)mask, path };
    }

    [Theory]
    [MemberData(nameof(TransformChannelCases))]
    public void TransformChannelsAreEnforcedOnEveryReceivePath(NetworkFlags mask, string path)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Channel proxy");
        root.Network.Mode = NetworkMode.Object;
        root.Network.Flags = mask;
        root.Transform.LocalPosition = new Float3(2, 3, 4);
        root.Transform.LocalScale = new Float3(2, 2, 2);
        NetTestComponent component = root.AddComponent<NetTestComponent>();
        scene.Add(root); MakeCurrent(scene);
        using NetWriter snapshot = new();
        uint generation;
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "source"));
            generation = NetworkGeneration(root);
            Networking.Networking.WriteSnapshot(scene, snapshot);
        }
        finally { Networking.Networking.Shutdown(); }

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = path != "CatchUp";
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => path == "CatchUp" ? host.Has(FakePeer.SceneLoaded) : Networking.Networking.IsReady, host);
            GameObject proxy = Find(Scene.Current, root.Identifier);
            Assert.Equal(mask, proxy.Network.Flags);
            Assert.Throws<InvalidOperationException>(() => proxy.Network.Flags = mask ^ NetworkFlags.NoScaleSync);
            // Disabled channels must retain current local values, not values frozen in the snapshot.
            Float3 localPosition = new(3, 4, 5), localScale = new(3, 3, 3);
            proxy.Transform.LocalPosition = localPosition;
            proxy.Transform.LocalScale = localScale;
            Float3 remotePosition = new(8, 9, 10), remoteScale = new(7, 7, 7);
            Quaternion remoteRotation = new(0, 0, 1, 0);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            void WritePose(NetWriter writer)
            {
                NetSerializers.WriteFloat3(writer, remotePosition);
                NetSerializers.WriteQuaternion(writer, remoteRotation);
                NetSerializers.WriteFloat3(writer, remoteScale);
            }
            if (path == "Tick")
                host.Send(FakePeer.Transform, w =>
                {
                    w.WriteGuid(proxy.Identifier); w.WriteUInt32(generation); w.WriteUInt32(1); w.WriteDouble(1);
                    WritePose(w);
                }, client);
            else if (path == "CatchUp")
            {
                NetReader loaded = new(host.Last(FakePeer.SceneLoaded));
                ulong sceneGeneration = loaded.ReadUInt64();
                host.Send(FakePeer.SceneCatchUp, w =>
                {
                    w.WriteUInt64(sceneGeneration); w.WriteVarUInt32(0); w.WriteVarUInt32(1);
                    w.WriteGuid(proxy.Identifier); w.WriteUInt32(generation); w.WriteUInt32(1); WritePose(w);
                }, client);
                Pump(() => host.Has(FakePeer.SceneReady), host);
            }
            else
            {
                using NetWriter transforms = new(), state = new(), states = new(), message = new();
                transforms.WriteGuid(proxy.Identifier); transforms.WriteUInt32(1); WritePose(transforms);
                state.WriteInt32(NetPeer.HostId); state.WriteUInt64(1);
                state.WriteGuid(proxy.Identifier); state.WriteUInt32(generation);
                state.WriteBytesWithLength(new byte[] { 0 }); state.WriteBytesWithLength(transforms.WrittenSpan);
                states.WriteVarUInt32(1); states.WriteBytesWithLength(state.WrittenSpan);
                if (path == "Ownership")
                {
                    message.WriteGuid(proxy.Identifier); message.WriteUInt32(generation);
                    NetSerializers.WriteFloat3(message, Float3.Zero);
                    message.WriteBytesWithLength(states.WrittenSpan);
                    host.Session.SendRaw(client, FakePeer.OwnershipState, message.WrittenSpan);
                }
                else
                {
                    message.WriteVarUInt32((uint)NetPeer.HostId);
                    message.WriteGuid(component.Identifier); message.WriteUInt32(NetTestComponent.ShoutId);
                    using NetWriter arguments = new(); arguments.WriteString("channels");
                    message.WriteBytesWithLength(arguments.WrittenSpan); message.WriteBytesWithLength(states.WrittenSpan);
                    host.Session.SendRaw(client, FakePeer.RpcFromHost, message.WrittenSpan);
                }
                host.Session.Flush();
                if (path == "Rpc") Pump(() => proxy.GetComponent<NetTestComponent>()!.Shouts.Count == 1, host);
            }
            PumpFor(20, host);
            Assert.True(Networking.Networking.IsActive);
            Assert.Equal((mask & NetworkFlags.NoPositionSync) == 0 ? remotePosition : localPosition, proxy.Transform.LocalPosition);
            Assert.Equal((mask & NetworkFlags.NoRotationSync) == 0 ? remoteRotation : Quaternion.Identity, proxy.Transform.LocalRotation);
            Assert.Equal((mask & NetworkFlags.NoScaleSync) == 0 ? remoteScale : localScale, proxy.Transform.LocalScale);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(NetworkFlags.NoTransformSync)]
    [InlineData(NetworkFlags.NoPositionSync)]
    [InlineData(NetworkFlags.NoRotationSync)]
    [InlineData(NetworkFlags.NoScaleSync)]
    [InlineData(NetworkFlags.NoPositionSync | NetworkFlags.NoRotationSync)]
    [InlineData(NetworkFlags.NoPositionSync | NetworkFlags.NoScaleSync)]
    [InlineData(NetworkFlags.NoRotationSync | NetworkFlags.NoScaleSync)]
    [InlineData(NetworkFlags.None)]
    [InlineData(NetworkFlags.NoInterpolation)]
    [InlineData(NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation)]
    public void TransformChannelFlagsRoundTrip(NetworkFlags mask)
    {
        GameObject go = CreateGameObject();
        go.Network.Flags = mask;
        using GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.Equal(mask, copy.Network.Flags);
        Assert.Throws<ArgumentOutOfRangeException>(() => go.Network.Flags = (NetworkFlags)16);
    }

    [Fact]
    public void OwnerCannotChangeDisabledChannelsAndRelayUsesHostValues()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject();
        root.Network.Mode = NetworkMode.Object;
        root.Network.Flags = NetworkFlags.NoScaleSync;
        root.Transform.LocalScale = new Float3(2, 2, 2);
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer owner = FakePeer.Join(network, "owner");
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => owner.Has(FakePeer.SceneSnapshot) && observer.Has(FakePeer.SceneSnapshot), owner, observer);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            observer.Received.Clear();
            owner.Send(FakePeer.Transform, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(NetworkGeneration(root)); w.WriteUInt32(1); w.WriteDouble(1);
                NetSerializers.WriteFloat3(w, new Float3(5, 6, 7));
                NetSerializers.WriteQuaternion(w, Quaternion.Identity);
                NetSerializers.WriteFloat3(w, new Float3(99, 99, 99));
            });
            Pump(() => root.Transform.LocalPosition == new Float3(5, 6, 7) && observer.Has(FakePeer.Transform), owner, observer);
            Assert.Equal(new Float3(2, 2, 2), root.Transform.LocalScale);
            NetReader relay = new(observer.Last(FakePeer.Transform));
            relay.ReadGuid(); relay.ReadUInt32(); relay.ReadUInt32(); relay.ReadDouble();
            NetSerializers.ReadFloat3(ref relay); NetSerializers.ReadQuaternion(ref relay);
            Assert.Equal(new Float3(2, 2, 2), NetSerializers.ReadFloat3(ref relay));
            root.Network.AssignOwnership(null); // Handoff must also preserve the disabled scale.
            Assert.Equal(new Float3(2, 2, 2), root.Transform.LocalScale);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void HandoffVelocityIsInWorldSpaceUnderATransformedParent()
    {
        Scene scene = CreateScene();
        GameObject parent = CreateGameObject();
        parent.Transform.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, MathF.PI * 0.5f);
        parent.Transform.LocalScale = new Float3(2, 2, 2);
        GameObject root = CreateGameObject();
        root.Network.Mode = NetworkMode.Object;
        root.Network.Flags = NetworkFlags.NoInterpolation; // Keep both samples.
        root.SetParent(parent);
        scene.Add(parent); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer owner = FakePeer.Join(network, "owner");
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => owner.IsReady && observer.IsReady, owner, observer);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            // Moving one unit along local X per second.
            owner.Send(FakePeer.Transform, w =>
            {
                for (uint i = 0; i < 2; i++)
                {
                    w.WriteGuid(root.Identifier); w.WriteUInt32(NetworkGeneration(root)); w.WriteUInt32(i + 1); w.WriteDouble(1 + i);
                    NetSerializers.WriteFloat3(w, new Float3(i, 0, 0));
                    NetSerializers.WriteQuaternion(w, Quaternion.Identity);
                    NetSerializers.WriteFloat3(w, Float3.One);
                }
            });
            Pump(() => root.Transform.LocalPosition == new Float3(1, 0, 0), owner, observer);
            observer.Received.Clear();
            root.Network.AssignOwnership(null);
            Pump(() => observer.Has(FakePeer.OwnershipState), owner, observer);
            NetReader handoff = new(observer.Last(FakePeer.OwnershipState));
            handoff.ReadGuid(); handoff.ReadUInt32();
            Float3 velocity = NetSerializers.ReadFloat3(ref handoff);
            Float3 expected = parent.Transform.TransformVector(Float3.UnitX);
            Assert.True(Float3.Distance(velocity, expected) < 1e-4f, $"{velocity} != {expected}");
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(NetworkFlags.NoTransformSync)]
    [InlineData(NetworkFlags.NoScaleSync | NetworkFlags.NoRotationSync)]
    [InlineData(NetworkFlags.NoScaleSync | NetworkFlags.NoPositionSync)]
    [InlineData(NetworkFlags.NoScaleSync)]
    public void DisabledChannelChangesDoNotTriggerTransformMessages(NetworkFlags mask)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject();
        root.Network.Mode = NetworkMode.Object;
        root.Network.Flags = mask;
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            var send = typeof(Networking.Networking).GetMethod("SendTransforms",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            if ((mask & NetworkFlags.NoPositionSync) == 0) root.Transform.LocalPosition = Float3.One;
            else if ((mask & NetworkFlags.NoRotationSync) == 0) root.Transform.LocalRotation = new Quaternion(0, 0, 1, 0);
            send.Invoke(null, null);
            PumpFor(10, client);
            int count = client.Received.Count(m => m.Id == FakePeer.Transform);
            if (mask == NetworkFlags.NoTransformSync) Assert.Equal(0, count);
            else Assert.True(count > 0);
            root.Transform.LocalScale = new Float3(9, 9, 9);
            send.Invoke(null, null);
            PumpFor(10, client);
            Assert.Equal(count, client.Received.Count(m => m.Id == FakePeer.Transform));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
