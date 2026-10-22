using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void OwnershipRejectsForeignPeersEvenWhenTheirIdsMatchCurrentPeers()
    {
        var (_, state, _) = ReadinessHost();
        using NetSession foreign = NetSession.Host(new LocalTransport(new(), "foreign"));
        GameObject root = state.GameObject;
        uint generation = Networking.Networking.GenerationOf(root);
        try
        {
            Assert.Equal(Networking.Networking.Session!.LocalPeer!.Id, foreign.LocalPeer!.Id);
            Assert.Throws<ArgumentException>(() => root.Network.AssignOwnership(foreign.LocalPeer));
            Assert.Throws<ArgumentException>(() => root.Network.Spawn(foreign.LocalPeer, NetworkOrphaned.ClearOwner));
            Assert.Null(root.Network.Owner);
            Assert.Equal(generation, Networking.Networking.GenerationOf(root));
            Assert.Equal(NetworkOrphaned.Destroy, root.Network.Orphaned);
            root.Network.AssignOwnership(Networking.Networking.Session.LocalPeer); // The actual session's host is valid.
            Assert.Same(Networking.Networking.Session.LocalPeer, root.Network.Owner);
            root.Network.AssignOwnership(null);
            Assert.Null(root.Network.Owner);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void InvalidSpawnOwnerIsRejectedBeforeAnyNetworkSettingsChange()
    {
        ReadinessHost();
        using NetSession foreign = NetSession.Host(new LocalTransport(new(), "foreign"));
        GameObject root = CreateGameObject();
        Scene.Current.Add(root);
        try
        {
            Assert.Throws<ArgumentException>(() => root.Network.Spawn(foreign.LocalPeer, NetworkOrphaned.ClearOwner));
            Assert.Equal(NetworkMode.Snapshot, root.Network.Mode);
            Assert.Equal(NetworkOrphaned.Destroy, root.Network.Orphaned);
            Assert.False(root.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void DisconnectedOwnersCannotBeReassignedOrChangeThePolicyThroughSpawn()
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        GameObject root = state.GameObject;
        try
        {
            Pump(() => client.IsReady, client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.Spawn(owner, NetworkOrphaned.ClearOwner);
            client.Session.Shutdown();
            Pump(() => root.Network.Owner == null, client);
            uint generation = Networking.Networking.GenerationOf(root);
            Assert.Throws<ArgumentException>(() => root.Network.AssignOwnership(owner));
            Assert.Throws<ArgumentException>(() => root.Network.Spawn(owner, NetworkOrphaned.Destroy));
            Assert.Equal(NetworkOrphaned.ClearOwner, root.Network.Orphaned);
            Assert.Equal(generation, Networking.Networking.GenerationOf(root));
            Assert.False(root.IsProxy);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(NetworkOrphaned.Destroy, NetworkOrphaned.ClearOwner)]
    [InlineData(NetworkOrphaned.ClearOwner, NetworkOrphaned.Destroy)]
    public void RuntimeOrphanPolicyChangesReachClientsLateJoinersAndDisconnectHandling(
        NetworkOrphaned initial, NetworkOrphaned updated)
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer ownerClient = FakePeer.Join(network, "owner");
        GameObject root = state.GameObject;
        try
        {
            Pump(() => ownerClient.IsReady, ownerClient);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.Spawn(owner, initial);
            Pump(() => ownerClient.Has(FakePeer.Owner), ownerClient);
            ownerClient.Received.RemoveAll(m => m.Id == FakePeer.Orphaned);
            uint generation = Networking.Networking.GenerationOf(root);
            root.Network.Orphaned = updated;
            Pump(() => ownerClient.Has(FakePeer.Orphaned), ownerClient);
            NetReader policy = new(ownerClient.Last(FakePeer.Orphaned));
            Assert.Equal(root.Identifier, policy.ReadGuid());
            Assert.Equal(generation, policy.ReadUInt32());
            Assert.Equal((byte)updated, policy.ReadByte());
            Assert.True(policy.IsAtEnd);
            Assert.Equal(generation, Networking.Networking.GenerationOf(root));

            using FakePeer lateJoiner = FakePeer.Join(network, "late");
            Pump(() => lateJoiner.IsReady, ownerClient, lateJoiner);
            NetReader snapshot = new(lateJoiner.Last(FakePeer.SceneSnapshot));
            using (Scene received = Networking.Networking.ReadSnapshot(ref snapshot))
                Assert.Equal(updated, Find(received, root.Identifier).Network.Orphaned);
            Assert.Equal(1u, snapshot.ReadVarUInt32());
            Assert.Equal(root.Identifier, snapshot.ReadGuid()); snapshot.ReadVarUInt32();
            Assert.Equal((byte)updated, snapshot.ReadByte());

            ownerClient.Session.Shutdown();
            if (updated == NetworkOrphaned.Destroy)
                Pump(() => root.IsDisposed, ownerClient, lateJoiner);
            else
            {
                Pump(() => root.Network.Owner == null, ownerClient, lateJoiner);
                Assert.True(root.IsValid());
                Assert.False(root.IsProxy);
            }
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ClientsApplyHostPolicyChangesAndRejectLocalChangesAndStaleGenerations()
    {
        var (_, state, _) = ReadinessHost();
        Guid objectId = state.GameObject.Identifier;
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(Scene.Current, snapshot);
        Networking.Networking.Shutdown();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            GameObject root = Find(Scene.Current, objectId);
            uint generation = Networking.Networking.GenerationOf(root);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            Assert.Throws<InvalidOperationException>(() => root.Network.Orphaned = NetworkOrphaned.ClearOwner);
            Assert.Equal(NetworkOrphaned.Destroy, root.Network.Orphaned);

            host.Send(FakePeer.Orphaned, w =>
            {
                w.WriteGuid(objectId); w.WriteUInt32(generation); w.WriteByte((byte)NetworkOrphaned.ClearOwner);
            }, client);
            Pump(() => root.Network.Orphaned == NetworkOrphaned.ClearOwner, host);
            host.Send(FakePeer.Orphaned, w =>
            {
                w.WriteGuid(objectId); w.WriteUInt32(generation - 1); w.WriteByte((byte)NetworkOrphaned.Destroy);
            }, client);
            PumpFor(20, host);
            Assert.Equal(NetworkOrphaned.ClearOwner, root.Network.Orphaned);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void InvalidOrphanPoliciesAreRejectedBeforeMutation()
    {
        ReadinessHost();
        GameObject root = CreateGameObject(); Scene.Current.Add(root);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Network.Orphaned = (NetworkOrphaned)255);
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Network.Spawn(orphaned: (NetworkOrphaned)255));
            Assert.Equal(NetworkOrphaned.Destroy, root.Network.Orphaned);
            Assert.Equal(NetworkMode.Snapshot, root.Network.Mode);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
