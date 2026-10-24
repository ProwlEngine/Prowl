using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Serialization;
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
            Assert.Equal(OwnerTransfer.Fixed, root.Network.OwnerTransfer);
            RequestOwnership(first, root, generation, true);
            PumpFor(20, first, second);
            Assert.Null(root.Network.Owner);
            root.Network.OwnerTransfer = OwnerTransfer.Takeover;
            RequestOwnership(first, root, generation, true);
            Pump(() => root.Network.Owner != null, first, second);
            int winner = root.Network.Owner!.Id;
            Assert.Equal(first.Session.LocalPeer!.Id, winner);
            RequestOwnership(second, root, generation, true);
            PumpFor(20, first, second);
            Assert.Equal(winner, root.Network.Owner!.Id);
            Assert.NotEqual(generation, NetworkGeneration(root));

            using FakePeer loading = FakePeer.Join(network, "loading");
            loading.AutoReady = false;
            Pump(() => loading.Has(FakePeer.SceneSnapshot), first, second, loading);
            RequestOwnership(loading, root, NetworkGeneration(root), true);
            PumpFor(20, first, second, loading);
            Assert.Equal(winner, root.Network.Owner!.Id);
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
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Id == owner.Session.LocalPeer!.Id));
            uint generation = NetworkGeneration(root);
            RequestOwnership(other, root, generation, false);
            PumpFor(20, owner, other);
            Assert.NotNull(root.Network.Owner);
            // The owner's final values go ahead of the request, which carries its final pose.
            owner.Session.SendRaw(owner.Session.HostPeer!, FakePeer.Sync,
                ClientSync(state, owner.Session.LocalPeer!.Id, generation, 17, NetTestComponent.HealthSlot));
            RequestOwnership(owner, root, generation, false, Poses(root, 42));
            Pump(() => root.Network.Owner == null, owner, other);
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
        original.Network.OwnerTransfer = OwnerTransfer.Takeover;
        byte[] snapshot, values, handoff, ownerMessage;
        using (FakePeer observer = FakePeer.Join(network, "observer"))
        {
            try
            {
                Pump(() => observer.IsReady, observer);
                snapshot = observer.Last(FakePeer.SceneSnapshot);
                state.Health = 37;
                original.Transform.LocalPosition = new Float3(19, 0, 0);
                original.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
                Pump(() => observer.Has(FakePeer.Owner), observer);
                Assert.True(observer.Received.FindIndex(m => m.Id == FakePeer.OwnershipState) <
                    observer.Received.FindIndex(m => m.Id == FakePeer.Owner));
                handoff = observer.Last(FakePeer.OwnershipState);
                ownerMessage = observer.Last(FakePeer.Owner);
                // The host's values go ahead of the handoff, which carries only poses.
                int handoffIndex = observer.WireReceived.FindLastIndex(m => m.Id == FakePeer.OwnershipState);
                values = observer.WireReceived.Take(handoffIndex).Last(m => m.Id == FakePeer.Sync).Data;
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
            Assert.Equal(OwnerTransfer.Takeover, root.Network.OwnerTransfer);
            Assert.True(root.IsProxy);
            Assert.Throws<InvalidOperationException>(() => root.Network.OwnerTransfer = OwnerTransfer.Fixed);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            uint generation = NetworkGeneration(root);
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(generation); w.WriteByte((byte)OwnerTransfer.Fixed);
            }, client);
            Pump(() => root.Network.OwnerTransfer == OwnerTransfer.Fixed, host);
            Assert.False(root.Network.TakeOwnership());
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(generation - 1); w.WriteByte((byte)OwnerTransfer.Takeover);
            }, client);
            PumpFor(20, host);
            Assert.Equal(OwnerTransfer.Fixed, root.Network.OwnerTransfer);
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(generation); w.WriteByte((byte)OwnerTransfer.Takeover);
            }, client);
            Pump(() => root.Network.OwnerTransfer == OwnerTransfer.Takeover, host);
            Assert.True(root.Network.TakeOwnership());
            Assert.True(root.IsProxy);
            Pump(() => host.Has(FakePeer.OwnershipRequest), host);
            NetReader request = new(host.Last(FakePeer.OwnershipRequest));
            Assert.Equal(root.Identifier, request.ReadGuid()); request.ReadUInt32();
            Assert.True(request.ReadBool()); NetSerializers.ReadFloat3(ref request);
            Assert.Empty(request.ReadBytesWithLength().ToArray());
            host.Session.SendRaw(client, FakePeer.Sync, values);
            host.Send(FakePeer.OwnershipState, w => w.WriteBytes(handoff), client);
            host.Send(FakePeer.Owner, w => w.WriteBytes(ownerMessage), client);
            Pump(() => !root.IsProxy, host);
            Assert.Equal(37, received.Health);
            Assert.Equal(19f, root.Transform.LocalPosition.X);
            Assert.False(root.Network.TakeOwnership());
            host.Send(FakePeer.OwnerTransfer, w =>
            {
                w.WriteGuid(root.Identifier); w.WriteUInt32(NetworkGeneration(root)); w.WriteByte((byte)OwnerTransfer.Fixed);
            }, client);
            Pump(() => root.Network.OwnerTransfer == OwnerTransfer.Fixed, host);
            received.Health = 12;
            host.Received.RemoveAll(m => m.Id == FakePeer.OwnershipRequest);
            Assert.True(root.Network.DropOwnership());
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
        GameObject root = CreateGameObject("Root"); root.Network.Mode = NetworkMode.Object;
        GameObject child = CreateGameObject("Child"); child.SetParent(root);
        scene.Add(root); MakeCurrent(scene);
        Assert.False(child.Network.TakeOwnership());
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            Assert.False(child.Network.DropOwnership());
            Assert.True(child.Network.TakeOwnership());
            Assert.Same(Networking.Networking.Session!.LocalPeer, root.Network.Owner);
            Assert.Same(root.Network.Owner, child.Network.Owner);
            Assert.False(child.Network.TakeOwnership());
            Assert.True(child.Network.DropOwnership());
            Assert.Null(root.Network.Owner);
            Assert.False(child.IsProxy);
            Assert.Equal(NetworkMode.Snapshot, child.Network.Mode);
            Assert.Equal(OwnerTransfer.Fixed, root.Network.OwnerTransfer);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DropIgnoresStaleRequestsAndUnauthorizedValues(bool stale)
    {
        var (_, state, network) = ReadinessHost();
        GameObject root = state.GameObject;
        root.Network.Orphaned = NetworkOrphaned.ClearOwner;
        using FakePeer owner = FakePeer.Join(network, "owner");
        try
        {
            Pump(() => owner.IsReady, owner);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            uint generation = NetworkGeneration(root);
            // A client can't write a FromHost value, even on its way out.
            owner.Session.SendRaw(owner.Session.HostPeer!, FakePeer.Sync,
                ClientSync(state, owner.Session.LocalPeer!.Id, generation, 66, NetTestComponent.ScoreSlot));
            RequestOwnership(owner, root, stale ? generation - 1 : generation, false, Poses(root, 42));
            PumpFor(30, owner);
            Assert.Equal(SessionState.Active, owner.Session.State);
            Assert.Equal(0, state.Score);
            if (stale)
            {
                Assert.NotNull(root.Network.Owner);
                Assert.Equal(generation, NetworkGeneration(root));
                Assert.Equal(Float3.Zero, root.Transform.LocalPosition);
            }
            else
            {
                Assert.Null(root.Network.Owner);
                Assert.Equal(42f, root.Transform.LocalPosition.X);
            }
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
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Network.OwnerTransfer = (OwnerTransfer)255);
            root.Network.OwnerTransfer = OwnerTransfer.Takeover;
            Pump(() => client.Has(FakePeer.OwnerTransfer), client);
            NetReader policy = new(client.Last(FakePeer.OwnerTransfer));
            Assert.Equal(root.Identifier, policy.ReadGuid());
            Assert.Equal(NetworkGeneration(root), policy.ReadUInt32());
            Assert.Equal((byte)OwnerTransfer.Takeover, policy.ReadByte());
            Assert.True(policy.IsAtEnd);
            using FakePeer late = FakePeer.Join(network, "late");
            Pump(() => late.IsReady, client, late);
            NetReader snapshot = new(late.Last(FakePeer.SceneSnapshot));
            using Scene scene = NetworkTestScene.Current.ReadSnapshot(ref snapshot);
            Assert.Equal(OwnerTransfer.Takeover, Find(scene, root.Identifier).Network.OwnerTransfer);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
