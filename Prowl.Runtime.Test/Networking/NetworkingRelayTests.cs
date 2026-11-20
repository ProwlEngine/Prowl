using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    /// <summary>A Snapshot of one moving record: a whole grid position, and a compact rotation when given.</summary>
    private static void WriteMoving(NetWriter w, uint tick, uint netId, uint generation, Float3 position, Quaternion? rotation = null)
    {
        w.WriteVarUInt32(0); w.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration); w.WriteVarUInt32(tick);
        w.WriteVarUInt32(1);
        w.WriteVarUInt32(tick); w.WriteDouble(tick / 30.0); w.WriteVarUInt32(1);
        w.WriteVarUInt32(netId); w.WriteVarUInt32(generation);
        w.WriteByte((byte)(PositionChannel | (rotation != null ? RotationChannel : 0)));
        w.WriteVarUInt32(0);
        w.WriteVarInt64((long)MathF.Round(position.X * 100)); w.WriteVarInt64((long)MathF.Round(position.Y * 100));
        w.WriteVarInt64((long)MathF.Round(position.Z * 100));
        if (rotation is { } turned) NetSerializers.WriteCompactQuaternion(w, turned);
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
            owner.Send(FakePeer.Snapshot, w => WriteMoving(w, 1, netId, generation, new Float3(1, 0, 0), turned));
            owner.Send(FakePeer.Snapshot, w => WriteMoving(w, 2, netId, generation, new Float3(2, 0, 0)));

            List<TransformRecord> Relayed(FakePeer peer) => peer.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
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
