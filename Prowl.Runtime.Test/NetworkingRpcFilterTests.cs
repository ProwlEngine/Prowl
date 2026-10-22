using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BroadcastFiltersSelectRemoteAndLocalRecipientsAndRestoreTheOuterScope(bool isStatic)
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer harry = FakePeer.Join(network, "Harry"), garry = FakePeer.Join(network, "Garry");
        ClearStaticProbe();
        ushort messageId = isStatic ? FakePeer.StaticRpcFromHost : FakePeer.RpcFromHost;
        void Call() { if (isStatic) StaticRpcProbe.Call(901, 1); else state.Shout("bing"); }
        int LocalCalls() => isStatic ? StaticRpcProbe.Calls.Count : state.Shouts.Count;
        try
        {
            Pump(() => harry.IsReady && garry.IsReady, harry, garry);
            using (Rpc.FilterExclude(peer => peer.Name == "Harry"))
            {
                using (Rpc.FilterInclude(peer => peer.Name == "Garry")) Call();
                Pump(() => garry.Has(messageId), harry, garry);
                Assert.Equal(0, LocalCalls());
                Assert.False(harry.Has(messageId));
                Call(); // The outer exclusion still includes the host.
                Assert.Equal(1, LocalCalls());
            }
            Call();
            Pump(() => harry.Has(messageId), harry, garry);
            Assert.Equal(2, LocalCalls());
            Assert.Equal(3, garry.Received.Count(m => m.Id == messageId));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void HostAndOwnerFiltersCannotBroadenTheRpcsNormalRecipients()
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer harry = FakePeer.Join(network, "Harry"), garry = FakePeer.Join(network, "Garry");
        ClearStaticProbe();
        try
        {
            Pump(() => harry.IsReady && garry.IsReady, harry, garry);
            using (Rpc.FilterInclude(_ => false))
            {
                state.AskHost(1); state.TellOwner("hidden"); StaticRpcProbe.Call(902, 1);
            }
            Assert.Empty(state.Asked); Assert.Empty(state.Shouts); Assert.Empty(StaticRpcProbe.Calls);
            using (Rpc.FilterInclude(peer => peer.IsHost))
            {
                state.AskHost(2); state.TellOwner("host"); StaticRpcProbe.Call(902, 2);
            }
            Assert.Equal(new[] { 2 }, state.Asked);
            Assert.Single(state.Shouts); Assert.Single(StaticRpcProbe.Calls);

            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => p.Name == "Harry");
            state.GameObject.Network.AssignOwnership(owner);
            using (Rpc.FilterInclude(peer => peer.Name == "Garry")) state.TellOwner("wrong owner");
            using (Rpc.FilterInclude(peer => peer.Name == "Harry")) state.TellOwner("right owner");
            Pump(() => harry.Has(FakePeer.RpcFromHost), harry, garry);
            Assert.False(garry.Has(FakePeer.RpcFromHost));
            Assert.Equal(1, harry.Received.Count(m => m.Id == FakePeer.RpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    private static void SendFilteredCall(FakePeer client, NetTestComponent state, bool isStatic, params int[] recipients)
    {
        using NetWriter message = new();
        if (isStatic) WriteStaticRpc(message, 901, w => NetSerializer<int>.Write(w, 1));
        else
        {
            message.WriteGuid(state.Identifier); message.WriteUInt32(NetTestComponent.ShoutId);
            using NetWriter arguments = new(); arguments.WriteString("bing");
            message.WriteBytesWithLength(arguments.WrittenSpan);
            message.WriteBytesWithLength(new byte[] { 0 });
        }
        message.WriteVarUInt32((uint)recipients.Length);
        foreach (int id in recipients) message.WriteVarUInt32((uint)id);
        client.Session.SendRaw(client.Session.HostPeer!, isStatic ? FakePeer.StaticRpcToHost : FakePeer.RpcToHost,
            message.WrittenSpan);
        client.Session.Flush();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientFiltersSurviveRelayingAndCanExcludeTheHost(bool isStatic)
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer caller = FakePeer.Join(network, "caller"), harry = FakePeer.Join(network, "Harry"),
            garry = FakePeer.Join(network, "Garry");
        ClearStaticProbe();
        ushort messageId = isStatic ? FakePeer.StaticRpcFromHost : FakePeer.RpcFromHost;
        try
        {
            Pump(() => caller.IsReady && harry.IsReady && garry.IsReady, caller, harry, garry);
            SendFilteredCall(caller, state, isStatic, garry.Session.LocalPeer!.Id);
            Pump(() => garry.Has(messageId), caller, harry, garry);
            Assert.False(harry.Has(messageId)); Assert.False(caller.Has(messageId));
            Assert.Empty(state.Shouts); Assert.Empty(StaticRpcProbe.Calls);
            NetReader relay = new(garry.Last(messageId));
            Assert.Equal((uint)caller.Session.LocalPeer!.Id, relay.ReadVarUInt32());
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateRecipientListsAreRejectedBeforeExecutingOrRelaying(bool isStatic)
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer caller = FakePeer.Join(network, "caller"), garry = FakePeer.Join(network, "Garry");
        ClearStaticProbe();
        try
        {
            Pump(() => caller.IsReady && garry.IsReady, caller, garry);
            SendFilteredCall(caller, state, isStatic, garry.Session.LocalPeer!.Id, garry.Session.LocalPeer.Id);
            Pump(() => caller.Session.State == SessionState.Ended, caller, garry);
            Assert.Empty(state.Shouts); Assert.Empty(StaticRpcProbe.Calls);
            Assert.False(garry.Has(isStatic ? FakePeer.StaticRpcFromHost : FakePeer.RpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbientFiltersDoNotChangeIncomingRpcRecipients(bool isStatic)
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer caller = FakePeer.Join(network, "caller"), garry = FakePeer.Join(network, "Garry");
        ClearStaticProbe();
        try
        {
            Pump(() => caller.IsReady && garry.IsReady, caller, garry);
            using (Rpc.FilterExclude(_ => true))
            {
                SendFilteredCall(caller, state, isStatic, NetPeer.HostId, garry.Session.LocalPeer!.Id);
                Pump(() => garry.Has(isStatic ? FakePeer.StaticRpcFromHost : FakePeer.RpcFromHost), caller, garry);
            }
            Assert.Equal(1, isStatic ? StaticRpcProbe.Calls.Count : state.Shouts.Count);
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void FailedPredicatesSendNothingAndDisposalRestoresThePreviousFilter()
    {
        var (_, state, network) = ReadinessHost();
        using FakePeer garry = FakePeer.Join(network, "Garry");
        try
        {
            Pump(() => garry.IsReady, garry);
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (Rpc.FilterInclude(peer => peer.IsHost ? true : throw new InvalidOperationException("predicate")))
                    state.Shout("failed");
            });
            Assert.Empty(state.Shouts);
            state.Shout("restored");
            Pump(() => garry.Has(FakePeer.RpcFromHost), garry);
            Assert.Equal("restored", Assert.Single(state.Shouts).Text);
            Assert.Equal(1, garry.Received.Count(m => m.Id == FakePeer.RpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void FilterScopesEnforceReverseDisposalAndAllowRepeatedDisposal()
    {
        IDisposable outer = Rpc.FilterInclude(_ => true);
        IDisposable inner = Rpc.FilterExclude(_ => false);
        try
        {
            Assert.Throws<InvalidOperationException>(() => outer.Dispose());
            Assert.True(Rpc.HasFilters);
        }
        finally { inner.Dispose(); outer.Dispose(); outer.Dispose(); }
        Assert.False(Rpc.HasFilters);
        Assert.Throws<ArgumentNullException>(() => Rpc.FilterInclude(null!));
    }

    [Fact]
    public void ActualClientEncodesSelectedRecipientsAndSkipsExcludedLocalBodies()
    {
        var (_, original, _) = ReadinessHost();
        Guid objectId = original.GameObject.Identifier;
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(Scene.Current, snapshot);
        Networking.Networking.Shutdown();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network), garry = FakePeer.Join(network, "Garry");
        host.Session.PeerJoined += peer =>
        {
            if (peer.Name == "caller") host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        };
        ClearStaticProbe();
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "caller"), "host", "caller");
            Pump(() => Networking.Networking.IsReady && garry.Session.State == SessionState.Active, host, garry);
            NetTestComponent state = Find(Scene.Current, objectId).GetComponent<NetTestComponent>()!;
            using (Rpc.FilterInclude(peer => peer.Name == "Garry"))
            {
                state.Shout("only Garry"); StaticRpcProbe.Call(901, 1);
            }
            Pump(() => host.Has(FakePeer.RpcToHost) && host.Has(FakePeer.StaticRpcToHost), host, garry);
            Assert.Empty(state.Shouts); Assert.Empty(StaticRpcProbe.Calls);
            foreach (ushort kind in new[] { FakePeer.RpcToHost, FakePeer.StaticRpcToHost })
            {
                NetReader wire = new(host.WireReceived.Last(m => m.Id == kind).Data);
                if (kind == FakePeer.RpcToHost) wire.ReadGuid(); else wire.ReadString();
                wire.ReadUInt32(); wire.ReadBytesWithLength();
                if (kind == FakePeer.RpcToHost) wire.ReadBytesWithLength();
                Assert.Equal(1u, wire.ReadVarUInt32());
                Assert.Equal((uint)garry.Session.LocalPeer!.Id, wire.ReadVarUInt32());
                Assert.True(wire.IsAtEnd);
            }
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }
}
