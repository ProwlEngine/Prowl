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
            foreach (string path in new[] { "Tick", "CatchUp", "Ownership" })
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
        root.AddComponent<NetTestComponent>();
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
            uint netId = NetworkTestScene.Current.NetIdOf(proxy);
            if (path == "Tick")
                host.Send(FakePeer.Snapshot, w => WriteTransforms(w, 1, 1,
                    (netId, generation, remotePosition, remoteRotation, remoteScale)), client);
            else if (path == "CatchUp")
            {
                NetReader loaded = new(host.Last(FakePeer.SceneLoaded));
                ulong sceneGeneration = loaded.ReadUInt64();
                host.Send(FakePeer.SceneCatchUp, w =>
                {
                    w.WriteUInt64(sceneGeneration); w.WriteVarUInt32(0); w.WriteVarUInt32(1);
                    w.WriteVarUInt32(netId); w.WriteVarUInt32(generation); w.WriteVarUInt32(1); WritePose(w);
                }, client);
                Pump(() => host.Has(FakePeer.SceneReady), host);
            }
            else
            {
                using NetWriter poses = new(), message = new();
                poses.WriteVarUInt32(1); poses.WriteGuid(proxy.Identifier); WritePose(poses);
                message.WriteGuid(proxy.Identifier); message.WriteUInt32(generation);
                NetSerializers.WriteFloat3(message, Float3.Zero);
                message.WriteBytesWithLength(poses.WrittenSpan);
                message.WriteVarUInt32(1); message.WriteVarUInt32(0); // No values.
                host.Session.SendRaw(client, FakePeer.OwnershipState, message.WrittenSpan);
                host.Session.Flush();
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
            owner.Send(FakePeer.Snapshot, w => WriteTransforms(w, 1, 1,
                (NetworkTestScene.Current.NetIdOf(root), NetworkGeneration(root), new Float3(5, 6, 7), Quaternion.Identity,
                    new Float3(99, 99, 99))));
            Pump(() => root.Transform.LocalPosition == new Float3(5, 6, 7) && observer.Has(FakePeer.Snapshot), owner, observer);
            Assert.Equal(new Float3(2, 2, 2), root.Transform.LocalScale);
            // The relay leaves the disabled scale out, so observers keep their own.
            TransformRecord relayed = Assert.Single(ReadTransforms(observer.Last(FakePeer.Snapshot)));
            Assert.Equal(new Float3(5, 6, 7), relayed.Position);
            Assert.Null(relayed.Scale);
            root.Network.AssignOwnership(null); // Handoff must also preserve the disabled scale.
            Assert.Equal(new Float3(2, 2, 2), root.Transform.LocalScale);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void CompactRotationsStayWithinAQuarterDegree()
    {
        Random random = new(7);
        for (int i = 0; i < 1000; i++)
        {
            Quaternion q = Quaternion.Normalize(new Quaternion((float)random.NextDouble() * 2 - 1,
                (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1));
            using NetWriter writer = new();
            NetSerializers.WriteCompactQuaternion(writer, q);
            Assert.Equal(4, writer.Length);
            NetReader reader = new(writer.WrittenSpan);
            Quaternion back = NetSerializers.ReadCompactQuaternion(ref reader);
            // q and -q are the same rotation.
            float dot = MathF.Min(1, MathF.Abs(q.X * back.X + q.Y * back.Y + q.Z * back.Z + q.W * back.W));
            Assert.True(2 * MathF.Acos(dot) < 0.25f * MathF.PI / 180, $"{q} came back as {back}");
        }
    }

    [Fact]
    public void OnlyChangedChannelsAreSentAndASettledPoseIsExact()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject();
        root.Network.Mode = NetworkMode.Object;
        root.Transform.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.3f);
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            Action send = Networking.Networking.System!.Snapshots.Send;
            uint netId = NetworkTestScene.Current.NetIdOf(root);
            List<TransformRecord> Sent()
            {
                PumpFor(5, client);
                List<TransformRecord> records = client.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
                    .SelectMany(m => ReadTransforms(m.Data)).Where(r => r.NetId == netId).ToList();
                client.WireReceived.Clear();
                return records;
            }

            // Let the first full send and its settle go by.
            for (int i = 0; i < 5; i++) send();
            Sent();

            // Only moving: rotation and scale don't go along.
            for (int i = 1; i <= 8; i++)
            {
                root.Transform.LocalPosition = new Float3(i, 0, 0);
                send();
            }

            List<TransformRecord> moving = Sent();
            Assert.Equal(8, moving.Count);
            Assert.All(moving, r => Assert.NotNull(r.Position));
            Assert.All(moving, r => Assert.Null(r.Rotation));
            Assert.All(moving, r => Assert.Null(r.Scale));
            Assert.Equal(new Float3(8, 0, 0), moving[^1].Position);

            // Still: once settled, the whole pose goes once, exactly, then nothing once it's acknowledged.
            // Real ticks, so acknowledgements arrive in between as they would.
            PumpFor(400, client);
            TransformRecord settled = Assert.Single(Sent());
            Assert.NotEqual(0, settled.Channels & ExactRotationChannel);
            Assert.Equal(root.Transform.LocalRotation, settled.Rotation);
            Assert.Equal(Float3.One, settled.Scale);
            Assert.Equal(Delivery.Unreliable, client.Deliveries.Last(m => m.Id == FakePeer.Snapshot).Delivery);
            for (int i = 0; i < 6; i++) send();
            Assert.Empty(Sent());
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
            uint netId = NetworkTestScene.Current.NetIdOf(root), generation = NetworkGeneration(root);
            for (uint i = 0; i < 2; i++)
            {
                uint tick = i;
                owner.Send(FakePeer.Snapshot, w => WriteTransforms(w, tick + 1, 1 + tick,
                    (netId, generation, new Float3(tick, 0, 0), Quaternion.Identity, Float3.One)));
            }
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
            Action send = Networking.Networking.System!.Snapshots.Send;
            if ((mask & NetworkFlags.NoPositionSync) == 0) root.Transform.LocalPosition = Float3.One;
            else if ((mask & NetworkFlags.NoRotationSync) == 0) root.Transform.LocalRotation = new Quaternion(0, 0, 1, 0);
            send();
            PumpFor(10, client);
            int count = client.Received.Count(m => m.Id == FakePeer.Snapshot);
            if (mask == NetworkFlags.NoTransformSync) Assert.Equal(0, count);
            else Assert.True(count > 0);
            root.Transform.LocalScale = new Float3(9, 9, 9);
            send();
            PumpFor(10, client);
            Assert.Equal(count, client.Received.Count(m => m.Id == FakePeer.Snapshot));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
