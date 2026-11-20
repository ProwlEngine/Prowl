using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    /// <summary>A Transform of one moving record: a whole grid position, and a compact rotation when given.</summary>
    private static void WriteMoving(NetWriter w, uint tick, uint netId, uint generation, Float3 position, Quaternion? rotation = null)
    {
        w.WriteVarUInt32(0); w.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration); w.WriteVarUInt32(tick);
        w.WriteDouble(tick / 30.0); w.WriteVarUInt32(1);
        w.WriteVarUInt32(netId); w.WriteVarUInt32(generation);
        w.WriteByte((byte)(PositionChannel | (rotation != null ? RotationChannel : 0)));
        w.WriteVarUInt32(0);
        w.WriteVarInt64((long)MathF.Round(position.X * 100)); w.WriteVarInt64((long)MathF.Round(position.Y * 100));
        w.WriteVarInt64((long)MathF.Round(position.Z * 100));
        if (rotation is { } turned) NetSerializers.WriteCompactQuaternion(w, turned);
    }

    /// <summary>A Transform of one moving record that resumes from rest or teleports.</summary>
    private static void WriteMovingFlagged(NetWriter w, uint tick, uint netId, uint generation, Float3 position,
        float? stillFor, byte? teleport)
    {
        w.WriteVarUInt32(0); w.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration); w.WriteVarUInt32(tick);
        w.WriteDouble(tick / 30.0); w.WriteVarUInt32(1);
        w.WriteVarUInt32(netId); w.WriteVarUInt32(generation);
        w.WriteByte((byte)(PositionChannel | (stillFor != null ? ResumedChannel : 0) | (teleport != null ? TeleportChannel : 0)));
        if (stillFor is { } still) w.WriteSingle(still);
        if (teleport is { } jump) w.WriteByte(jump);
        w.WriteVarUInt32(0);
        w.WriteVarInt64((long)MathF.Round(position.X * 100)); w.WriteVarInt64((long)MathF.Round(position.Y * 100));
        w.WriteVarInt64((long)MathF.Round(position.Z * 100));
    }

    /// <summary>The records of a Transform, read the way a client reads them.</summary>
    private static List<Networking.TransformRecord> ReadAsAClient(byte[] snapshot)
    {
        NetReader reader = new(snapshot);
        TransformBatchMessage.Read(ref reader);
        uint count = TransformBatchMessage.ReadRecordCount(ref reader);
        List<Networking.TransformRecord> records = [];
        for (uint i = 0; i < count; i++) records.Add(Networking.TransformRecord.Read(ref reader));
        Assert.True(reader.IsAtEnd);
        return records;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PassingOnAResumeAndATeleportTogetherGivesARecordClientsAccept(bool teleportLast)
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Driven");
        root.Network.Networked = true;
        root.AddComponent<NetworkTransform>();
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer owner = FakePeer.Join(network, "owner");
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => owner.IsReady && observer.IsReady, owner, observer);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            Pump(() => observer.Has(FakePeer.Owner), owner, observer);
            PumpFor(20, owner, observer);
            observer.WireReceived.Clear();

            // Both between two host ticks: starting to move again, and a teleport.
            uint netId = NetworkTestScene.Current.NetIdOf(root), generation = NetworkGeneration(root);
            owner.Send(FakePeer.Transform, w => WriteMovingFlagged(w, 1, netId, generation, new Float3(1, 0, 0),
                teleportLast ? 2f : null, teleportLast ? null : (byte)7));
            owner.Send(FakePeer.Transform, w => WriteMovingFlagged(w, 2, netId, generation, new Float3(2, 0, 0),
                teleportLast ? null : 2f, teleportLast ? (byte)7 : null));

            List<Networking.TransformRecord> Relayed() => observer.WireReceived.Where(m => m.Id == FakePeer.Transform)
                .SelectMany(m => ReadAsAClient(m.Data)).Where(r => r.NetId == netId).ToList();
            Pump(() => observer.WireReceived.Any(m => m.Id == FakePeer.Transform), owner, observer);
            PumpFor(20, owner, observer);

            // The teleport wins, with the sequence number its sender gave it.
            Networking.TransformRecord record = Assert.Single(Relayed());
            Assert.NotEqual((TransformChannels)0, record.Update.Channels & TransformChannels.Teleport);
            Assert.Equal((TransformChannels)0, record.Update.Channels & TransformChannels.Resumed);
            Assert.Equal(7, record.Update.Teleport);
            Assert.Equal(new Float3(2, 0, 0), new Float3(record.Grid.X / 100f, record.Grid.Y / 100f, record.Grid.Z / 100f));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ATransformRecordSaysHowLongBeforeItsSnapshotItWasTaken()
    {
        TransformUpdate update = new(TransformChannels.Position | TransformChannels.Exact, 0, new Float3(1, 2, 3),
            Quaternion.Identity, default);
        int Size(uint ticksAgo, uint age)
        {
            using NetWriter w = new();
            new Networking.TransformRecord(7, 1, update, TicksAgo: ticksAgo, AgeMilliseconds: age).Write(w);
            NetReader r = new(w.WrittenSpan);
            Networking.TransformRecord back = Networking.TransformRecord.Read(ref r);
            Assert.True(r.IsAtEnd);
            Assert.Equal((ticksAgo, age, new Float3(1, 2, 3)), (back.TicksAgo, back.AgeMilliseconds, back.Update.Position));
            return w.Length;
        }

        // Taken as it is sent it costs nothing, a moment before one byte, and a tick count one more.
        int plain = Size(0, 0);
        Assert.Equal(plain + 1, Size(0, 45));
        Assert.Equal(plain + 2, Size(0, 100));
        Assert.Equal(plain + 2, Size(3, 45));
        Assert.Equal(plain + 2, Size(3, 0));
    }

    [Fact]
    public void TheHostPassesOnTheLatestOfAClientsMovingTransformsWithItsOwnTick()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Driven");
        root.Network.Networked = true;
        root.AddComponent<NetworkTransform>();
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer owner = FakePeer.Join(network, "owner");
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => owner.IsReady && observer.IsReady, owner, observer);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            Pump(() => observer.Has(FakePeer.Owner), owner, observer);
            PumpFor(20, owner, observer);
            owner.WireReceived.Clear();
            observer.WireReceived.Clear();

            // Two updates between two host ticks: turning and moving, then only moving on.
            uint netId = NetworkTestScene.Current.NetIdOf(root), generation = NetworkGeneration(root);
            Quaternion turned = Quaternion.AxisAngle(Float3.UnitY, 1f);
            owner.Send(FakePeer.Transform, w => WriteMoving(w, 1, netId, generation, new Float3(1, 0, 0), turned));
            owner.Send(FakePeer.Transform, w => WriteMoving(w, 2, netId, generation, new Float3(2, 0, 0)));

            List<TransformRecord> Relayed(FakePeer peer) => peer.WireReceived.Where(m => m.Id == FakePeer.Transform)
                .SelectMany(m => ReadTransforms(m.Data)).Where(r => r.NetId == netId).ToList();
            Pump(() => Relayed(observer).Count > 0, owner, observer);
            PumpFor(20, owner, observer);

            // One record: where it is now, with the turn the earlier update made.
            TransformRecord record = Assert.Single(Relayed(observer));
            Assert.Equal(new Float3(2, 0, 0), record.Position);
            Assert.NotNull(record.Rotation);
            Assert.True(MathF.Abs(Quaternion.Dot(turned, record.Rotation!.Value)) > 0.999f);
            Assert.Empty(Relayed(owner));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
