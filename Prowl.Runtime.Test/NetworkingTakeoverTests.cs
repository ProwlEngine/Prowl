using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static void RequestOwnership(FakePeer peer, GameObject root, uint generation, bool take,
        byte[]? state = null) => peer.Send(FakePeer.OwnershipRequest, w =>
        {
            w.WriteGuid(root.Identifier); w.WriteUInt32(generation); w.WriteBool(take);
            NetSerializers.WriteFloat3(w, Float3.Zero);
            w.WriteBytesWithLength(state ?? Array.Empty<byte>());
        });

    [Fact]
    public void TakeoverRequiresReadyPeerAndPolicyAndRejectsCompetingStaleRequests()
    {
        var (_, state, network) = ReadinessHost();
        GameObject root = state.GameObject;
        using FakePeer first = FakePeer.Join(network, "first");
        using FakePeer second = FakePeer.Join(network, "second");
        try
        {
            Pump(() => first.IsReady && second.IsReady, first, second);
            uint generation = NetworkGeneration(root);
            Assert.Equal(OwnerTransfer.Fixed, root.OwnerTransfer);
            RequestOwnership(first, root, generation, true);
            PumpFor(20, first, second);
            Assert.Null(root.NetworkOwner);
            root.OwnerTransfer = OwnerTransfer.Takeover;
            RequestOwnership(first, root, generation, true);
            Pump(() => root.NetworkOwner != null, first, second);
            int winner = root.NetworkOwner!.Id;
            Assert.Equal(first.Session.LocalPeer!.Id, winner);
            RequestOwnership(second, root, generation, true);
            PumpFor(20, first, second);
            Assert.Equal(winner, root.NetworkOwner!.Id);
            Assert.NotEqual(generation, NetworkGeneration(root));

            using FakePeer loading = FakePeer.Join(network, "loading");
            loading.AutoReady = false;
            Pump(() => loading.Has(FakePeer.SceneSnapshot), first, second, loading);
            RequestOwnership(loading, root, NetworkGeneration(root), true);
            PumpFor(20, first, second, loading);
            Assert.Equal(winner, root.NetworkOwner!.Id);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void OnlyOwnerCanDropFixedObjectAndFinalStateSurvivesTheHandoff()
    {
        var (_, state, network) = ReadinessHost();
        GameObject root = state.GameObject;
        using FakePeer owner = FakePeer.Join(network, "owner");
        using FakePeer other = FakePeer.Join(network, "other");
        try
        {
            Pump(() => owner.IsReady && other.IsReady, owner, other);
            root.SetNetworkOwner(Networking.Networking.Session!.Peers.Single(p => p.Id == owner.Session.LocalPeer!.Id));
            uint generation = NetworkGeneration(root);
            RequestOwnership(other, root, generation, false);
            PumpFor(20, owner, other);
            Assert.NotNull(root.NetworkOwner);
            // Reuse the normal dependent-state encoding, including the owner's final transform.
            byte[] rpc = StateRpc(root, state, 0, owner.Session.LocalPeer!.Id, generation, 100, 17, 42,
                NetTestComponent.HealthSlot);
            NetReader reader = new(rpc);
            reader.ReadGuid(); reader.ReadUInt32(); reader.ReadBytesWithLength();
            byte[] frames = reader.ReadBytesWithLength().ToArray();
            RequestOwnership(owner, root, generation, false, frames);
            Pump(() => root.NetworkOwner == null, owner, other);
            Assert.False(root.IsProxy);
            Assert.Equal(17, state.Health);
            Assert.Equal(42f, root.Transform.LocalPosition.X);
            Assert.NotEqual(generation, NetworkGeneration(root));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void HandoffArrivesBeforeOwnershipAndClientTakeDropWaitForConfirmation()
    {
        var (_, state, network) = ReadinessHost();
        GameObject original = state.GameObject;
        original.OwnerTransfer = OwnerTransfer.Takeover;
        byte[] snapshot, handoff, ownerMessage;
        using (FakePeer observer = FakePeer.Join(network, "observer"))
        {
            try
            {
                Pump(() => observer.IsReady, observer);
                snapshot = observer.Last(FakePeer.SceneSnapshot);
                state.Health = 37;
                original.Transform.LocalPosition = new Float3(19, 0, 0);
                original.SetNetworkOwner(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
                Pump(() => observer.Has(FakePeer.Owner), observer);
                Assert.True(observer.Received.FindIndex(m => m.Id == FakePeer.OwnershipState) <
                    observer.Received.FindIndex(m => m.Id == FakePeer.Owner));
                handoff = observer.Last(FakePeer.OwnershipState);
                ownerMessage = observer.Last(FakePeer.Owner);
            }
            finally { Networking.Networking.Shutdown(); }
        }

        LocalNetwork receiving = new();
        using FakePeer host = FakePeer.Host(receiving);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(receiving, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            GameObject root = Find(Scene.Current, original.Identifier);
            NetTestComponent received = root.GetComponent<NetTestComponent>()!;
            Assert.Equal(OwnerTransfer.Takeover, root.OwnerTransfer);
            Assert.True(root.IsProxy);
            Assert.Throws<InvalidOperationException>(() => root.OwnerTransfer = OwnerTransfer.Fixed);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            uint generation = NetworkGeneration(root);
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(generation); w.WriteByte((byte)OwnerTransfer.Fixed);
            }, client);
            Pump(() => root.OwnerTransfer == OwnerTransfer.Fixed, host);
            Assert.False(root.TakeOwnership());
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(generation - 1); w.WriteByte((byte)OwnerTransfer.Takeover);
            }, client);
            PumpFor(20, host);
            Assert.Equal(OwnerTransfer.Fixed, root.OwnerTransfer);
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(generation); w.WriteByte((byte)OwnerTransfer.Takeover);
            }, client);
            Pump(() => root.OwnerTransfer == OwnerTransfer.Takeover, host);
            Assert.True(root.TakeOwnership());
            Assert.True(root.IsProxy);
            Pump(() => host.Has(FakePeer.OwnershipRequest), host);
            NetReader request = new(host.Last(FakePeer.OwnershipRequest));
            Assert.Equal(root.Identifier, request.ReadGuid()); request.ReadUInt32();
            Assert.True(request.ReadBool()); NetSerializers.ReadFloat3(ref request);
            Assert.Empty(request.ReadBytesWithLength().ToArray());
            host.Send(FakePeer.OwnershipState, w => w.WriteBytes(handoff), client);
            host.Send(FakePeer.Owner, w => w.WriteBytes(ownerMessage), client);
            Pump(() => !root.IsProxy, host);
            Assert.Equal(37, received.Health);
            Assert.Equal(19f, root.Transform.LocalPosition.X);
            Assert.False(root.TakeOwnership());
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(NetworkGeneration(root)); w.WriteByte((byte)OwnerTransfer.Fixed);
            }, client);
            Pump(() => root.OwnerTransfer == OwnerTransfer.Fixed, host);
            received.Health = 12;
            host.Received.RemoveAll(m => m.Id == FakePeer.OwnershipRequest);
            Assert.True(root.DropOwnership());
            Assert.False(root.IsProxy);
            Pump(() => host.Has(FakePeer.OwnershipRequest), host);
            NetReader drop = new(host.Last(FakePeer.OwnershipRequest));
            Assert.Equal(root.Identifier, drop.ReadGuid());
            Assert.Equal(NetworkGeneration(root), drop.ReadUInt32());
            Assert.False(drop.ReadBool()); NetSerializers.ReadFloat3(ref drop);
            Assert.NotEmpty(drop.ReadBytesWithLength().ToArray());
            // Old-generation handoffs cannot roll the new owner's state back.
            host.Send(FakePeer.OwnershipState, w => w.WriteBytes(handoff), client);
            PumpFor(20, host);
            Assert.Equal(12, received.Health);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void HostCanTakeAndDropThroughChildrenWithoutChangingTheirSettings()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Root"); root.NetworkMode = NetworkMode.Object;
        GameObject child = CreateGameObject("Child"); child.SetParent(root);
        scene.Add(root); MakeCurrent(scene);
        Assert.False(child.TakeOwnership());
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            Assert.False(child.DropOwnership());
            Assert.True(child.TakeOwnership());
            Assert.Same(Networking.Networking.Session!.LocalPeer, root.NetworkOwner);
            Assert.Same(root.NetworkOwner, child.NetworkOwner);
            Assert.False(child.TakeOwnership());
            Assert.True(child.DropOwnership());
            Assert.Null(root.NetworkOwner);
            Assert.False(child.IsProxy);
            Assert.Equal(NetworkMode.Snapshot, child.NetworkMode);
            Assert.Equal(OwnerTransfer.Fixed, root.OwnerTransfer);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DropRejectsStaleOrUnauthorizedState(bool stale)
    {
        var (_, state, network) = ReadinessHost();
        GameObject root = state.GameObject;
        root.NetworkOrphaned = NetworkOrphaned.ClearOwner;
        using FakePeer owner = FakePeer.Join(network, "owner");
        try
        {
            Pump(() => owner.IsReady, owner);
            root.SetNetworkOwner(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            uint generation = NetworkGeneration(root);
            byte[] rpc = StateRpc(root, state, 0, owner.Session.LocalPeer!.Id,
                stale ? generation - 1 : generation, 100, 66, 42,
                stale ? NetTestComponent.HealthSlot : NetTestComponent.ScoreSlot);
            NetReader reader = new(rpc);
            reader.ReadGuid(); reader.ReadUInt32(); reader.ReadBytesWithLength();
            RequestOwnership(owner, root, generation, false, reader.ReadBytesWithLength().ToArray());
            PumpFor(30, owner);
            Assert.Equal(100, state.Health);
            Assert.Equal(0, state.Score);
            Assert.Equal(Float3.Zero, root.Transform.LocalPosition);
            if (stale) Assert.Equal(generation, NetworkGeneration(root));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void TransferPolicyUpdatesReplicateAndAppearInLateJoinSnapshots()
    {
        var (_, state, network) = ReadinessHost();
        GameObject root = state.GameObject;
        using FakePeer client = FakePeer.Join(network, "client");
        try
        {
            Pump(() => client.IsReady, client);
            Assert.Throws<ArgumentOutOfRangeException>(() => root.OwnerTransfer = (OwnerTransfer)255);
            root.OwnerTransfer = OwnerTransfer.Takeover;
            Pump(() => client.Has(FakePeer.OwnerTransfer), client);
            NetReader policy = new(client.Last(FakePeer.OwnerTransfer));
            Assert.Equal(root.Identifier, policy.ReadGuid());
            Assert.Equal(NetworkGeneration(root), policy.ReadUInt32());
            Assert.Equal((byte)OwnerTransfer.Takeover, policy.ReadByte());
            Assert.True(policy.IsAtEnd);
            using FakePeer late = FakePeer.Join(network, "late");
            Pump(() => late.IsReady, client, late);
            NetReader snapshot = new(late.Last(FakePeer.SceneSnapshot));
            using Scene scene = Networking.Networking.ReadSnapshot(ref snapshot);
            Assert.Equal(OwnerTransfer.Takeover, Find(scene, root.Identifier).OwnerTransfer);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
