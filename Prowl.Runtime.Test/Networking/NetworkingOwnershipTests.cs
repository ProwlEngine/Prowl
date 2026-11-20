using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

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
        uint generation = NetworkTestScene.Current.GenerationOf(root);
        try
        {
            Assert.Equal(Networking.Networking.Session!.LocalPeer!.Id, foreign.LocalPeer!.Id);
            Assert.Throws<ArgumentException>(() => root.Network.AssignOwnership(foreign.LocalPeer));
            Assert.Throws<ArgumentException>(() => root.SpawnBuilt(foreign.LocalPeer, NetworkOrphaned.ClearOwner));
            Assert.Null(root.Network.Owner);
            Assert.Equal(generation, NetworkTestScene.Current.GenerationOf(root));
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
        SceneInstance.Main.Add(root);
        try
        {
            Assert.Throws<ArgumentException>(() => root.SpawnBuilt(foreign.LocalPeer, NetworkOrphaned.ClearOwner));
            Assert.False(root.Network.Networked);
            Assert.Equal(NetworkOrphaned.Destroy, root.Network.Orphaned);
            Assert.False(root.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void DisconnectedOwnersCannotBeReassigned()
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        GameObject root = state.GameObject;
        try
        {
            Pump(() => client.IsReady, client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.Orphaned = NetworkOrphaned.ClearOwner;
            root.Network.AssignOwnership(owner);
            client.Session.Shutdown();
            Pump(() => root.Network.Owner == null, client);
            uint generation = NetworkTestScene.Current.GenerationOf(root);
            Assert.Throws<ArgumentException>(() => root.Network.AssignOwnership(owner));
            Assert.Equal(NetworkOrphaned.ClearOwner, root.Network.Orphaned);
            Assert.Equal(generation, NetworkTestScene.Current.GenerationOf(root));
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
            root.Network.Orphaned = initial;
            root.Network.AssignOwnership(owner);
            Pump(() => ownerClient.Has(FakePeer.Owner), ownerClient);
            ownerClient.Received.RemoveAll(m => m.Id == FakePeer.Orphaned);
            uint generation = NetworkTestScene.Current.GenerationOf(root);
            root.Network.Orphaned = updated;
            Pump(() => ownerClient.Has(FakePeer.Orphaned), ownerClient);
            NetReader policy = new(ownerClient.Last(FakePeer.Orphaned));
            Assert.Equal(root.Identifier, policy.ReadGuid());
            Assert.Equal(generation, policy.ReadUInt32());
            Assert.Equal((byte)updated, policy.ReadByte());
            Assert.True(policy.IsAtEnd);
            Assert.Equal(generation, NetworkTestScene.Current.GenerationOf(root));

            using FakePeer lateJoiner = FakePeer.Join(network, "late");
            Pump(() => lateJoiner.IsReady, ownerClient, lateJoiner);
            NetReader spawned = new(lateJoiner.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
            SpawnMessage lateSpawn = SpawnMessage.Read(ref spawned);
            Assert.Equal(root.Identifier, lateSpawn.Identifier);
            Assert.Equal(updated, lateSpawn.Orphaned);

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
        var (_, state, source) = ReadinessHost();
        Guid objectId = state.GameObject.Identifier;
        byte[] snapshot;
        byte[][] spawns;
        try
        {
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot, spawns);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            GameObject root = Find(SceneInstance.Main, objectId);
            uint generation = NetworkTestScene.Current.GenerationOf(root);
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
        GameObject root = CreateGameObject(); SceneInstance.Main.Add(root);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => root.Network.Orphaned = (NetworkOrphaned)255);
            Assert.Throws<ArgumentOutOfRangeException>(() => root.SpawnBuilt(orphaned: (NetworkOrphaned)255));
            Assert.Equal(NetworkOrphaned.Destroy, root.Network.Orphaned);
            Assert.False(root.Network.Networked);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
