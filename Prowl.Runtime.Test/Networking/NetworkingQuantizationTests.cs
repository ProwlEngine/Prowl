using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void PositionPrecisionIsValidatedAndPartOfTheSchema()
    {
        Assert.Equal(0.01f, new NetworkConfiguration().PositionPrecision);
        Assert.Equal(0.01f, new NetworkConfiguration { PositionPrecision = 0 }.ValidatedCopy().PositionPrecision);
        Assert.Equal(0.01f, new NetworkConfiguration { PositionPrecision = float.NaN }.ValidatedCopy().PositionPrecision);
        Assert.Equal(0.5f, new NetworkConfiguration { PositionPrecision = 0.5f }.ValidatedCopy().PositionPrecision);
        Assert.Equal(NetworkSchema.AppVersion(3), NetworkSchema.AppVersion(3, 0.01f));
        Assert.NotEqual(NetworkSchema.AppVersion(3, 0.01f), NetworkSchema.AppVersion(3, 0.02f));
    }

    [Fact]
    public void GridPositionsSnapToThePrecisionAndAddUp()
    {
        double step = GridPosition.Step(0.01f);
        GridPosition a = GridPosition.Of(new Float3(5, -6.25f, 1.234f), step);
        Assert.Equal(new GridPosition(500, -625, 123), a);
        Assert.Equal(new Float3(5, -6.25f, 1.23f), a.ToFloat3(step));
        GridPosition b = GridPosition.Of(new Float3(5.5f, -6, 1), step);
        Assert.Equal(b, a + (b - a));
        Assert.Equal(new Float3(1.5f, 0, 0), GridPosition.Of(new Float3(1.3f, 0, 0), GridPosition.Step(0.5f)).ToFloat3(GridPosition.Step(0.5f)));
    }

    [Fact]
    public void AMovingObjectSendsDeltasFromThePoseThePeerAcknowledged()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject mover = CreateGameObject("Mover");
            mover.AddComponent<NetworkTransform>();
            scene.Add(mover);
            mover.Network.SpawnSerialized();
            uint netId = NetworkTestScene.Current.NetIdOf(mover);

            float x = 1000;
            Pump(() =>
            {
                x += 0.25f;
                mover.Transform.LocalPosition = new Float3(x, 0, 0);
                return alice.WireReceived.Count(m => m.Id == FakePeer.Snapshot && RawRecords(m.Data).Any(r => r.NetId == netId && r.BaselineAge != 0)) >= 3;
            }, alice);

            List<(int Size, uint BaselineAge)> raw = alice.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
                .SelectMany(m => RawRecords(m.Data)).Where(r => r.NetId == netId && (r.Channels & PositionChannel) != 0)
                .Select(r => (r.Size, r.BaselineAge)).ToList();
            Assert.Equal(0u, raw[0].BaselineAge);
            Assert.All(raw.Where(r => r.BaselineAge != 0), r => Assert.True(r.Size < 12, $"delta position of {r.Size} bytes"));
            Pump(() => ReadTransforms(alice.WireReceived.Last(m => m.Id == FakePeer.Snapshot && ReadTransforms(m.Data).Any(r => r.NetId == netId)).Data)
                .Single(r => r.NetId == netId).Position == new Float3(x, 0, 0), alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ADeltaFromAnUnknownBaselineIsRejected()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Owned");
        scene.Add(root);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer owner = FakePeer.Join(network, "owner");
        try
        {
            Pump(() => owner.IsReady, owner);
            root.Network.SpawnSerialized(Networking.Networking.Session!.Peers.Single(p => p.Name == "owner"));
            uint netId = NetworkTestScene.Current.NetIdOf(root), generation = NetworkGeneration(root);
            owner.WireReceived.Clear();
            owner.Send(FakePeer.Snapshot, w =>
            {
                w.WriteVarUInt32(9); w.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration); w.WriteVarUInt32(50);
                w.WriteVarUInt32(1);
                w.WriteVarUInt32(50); w.WriteDouble(1); w.WriteVarUInt32(1);
                w.WriteVarUInt32(netId); w.WriteVarUInt32(generation); w.WriteByte(PositionChannel);
                w.WriteVarUInt32(5); w.WriteVarInt64(100); w.WriteVarInt64(0); w.WriteVarInt64(0);
            });

            Pump(() => owner.Has(FakePeer.SnapshotAck), owner);
            NetReader ack = new(owner.Last(FakePeer.SnapshotAck));
            Assert.Equal(1u, ack.ReadVarUInt32());
            Assert.Equal(9u, ack.ReadVarUInt32());
            Assert.Equal(1u, ack.ReadVarUInt32());
            Assert.Equal(netId, ack.ReadVarUInt32());
            Assert.Equal(Float3.Zero, root.Transform.LocalPosition);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    /// <summary>Each transform record of a Snapshot as sent: its channels, its position's byte size and the age of that position's baseline.</summary>
    private static List<(uint NetId, byte Channels, int Size, uint BaselineAge)> RawRecords(byte[] message)
    {
        NetReader r = new(message);
        r.ReadVarUInt32(); r.ReadVarUInt32(); r.ReadVarUInt32();
        List<(uint, byte, int, uint)> records = new();
        uint groups = r.ReadVarUInt32();
        for (uint g = 0; g < groups; g++)
        {
            r.ReadVarUInt32(); r.ReadDouble();
            uint count = r.ReadVarUInt32();
            for (uint i = 0; i < count; i++)
            {
                uint netId = r.ReadVarUInt32();
                r.ReadVarUInt32();
                byte channels = r.ReadByte();
                if ((channels & ResumedChannel) != 0) r.ReadSingle();
                if ((channels & TeleportChannel) != 0) r.ReadByte();
                int before = r.Remaining;
                uint age = 0;
                if ((channels & (PositionChannel | ExactChannel)) == (PositionChannel | ExactChannel)) r.ReadBytes(12);
                else if ((channels & PositionChannel) != 0)
                {
                    age = r.ReadVarUInt32();
                    r.ReadVarInt64(); r.ReadVarInt64(); r.ReadVarInt64();
                }

                int size = before - r.Remaining;
                if ((channels & RotationChannel) != 0) r.ReadBytes((channels & ExactChannel) != 0 ? 16 : 4);
                if ((channels & ScaleChannel) != 0) r.ReadBytes(12);
                records.Add((netId, channels, size, age));
            }
        }

        return records;
    }
}
