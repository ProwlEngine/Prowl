// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Warp.Tests;

public struct ChatMessage : INetMessage
{
    public static ushort Id => 1;

    public string Text;

    public ChatMessage(string text) => Text = text;

    public void Write(NetWriter writer) => writer.WriteString(Text);

    public void Read(ref NetReader reader) => Text = reader.ReadString();
}

public struct PositionMessage : INetMessage
{
    public static ushort Id => 2;

    public float X;
    public float Y;

    public void Write(NetWriter writer)
    {
        writer.WriteSingle(X);
        writer.WriteSingle(Y);
    }

    public void Read(ref NetReader reader)
    {
        X = reader.ReadSingle();
        Y = reader.ReadSingle();
    }
}

/// <summary>Records what a session reports, and pumps a group of sessions.</summary>
public sealed class SessionProbe
{
    public SessionProbe(NetSession session)
    {
        Session = session;
        session.Joined += () => JoinedCount++;
        session.PeerJoined += peer => PeersJoined.Add(peer);
        session.PeerLeft += (peer, reason) => PeersLeft.Add((peer, reason));
        session.Ended += (reason, message) => EndedWith = (reason, message);
        session.On<ChatMessage>((sender, message) => Chat.Add((sender, message.Text)));
    }

    public NetSession Session { get; }
    public int JoinedCount { get; private set; }
    public List<NetPeer> PeersJoined { get; } = new();
    public List<(NetPeer Peer, SessionEndReason Reason)> PeersLeft { get; } = new();
    public (SessionEndReason Reason, string? Message)? EndedWith { get; private set; }
    public List<(NetPeer Sender, string Text)> Chat { get; } = new();

    public static void PumpUntil(Func<bool> condition, params SessionProbe[] probes) => PumpUntil(condition, 5000, probes);

    public static void PumpUntil(Func<bool> condition, int timeoutMs, params SessionProbe[] probes)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Sessions never reached the expected state.");
            foreach (SessionProbe probe in probes)
                probe.Session.Update();
            Thread.Sleep(1);
        }
    }

    public static void PumpFor(int milliseconds, params SessionProbe[] probes)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            foreach (SessionProbe probe in probes)
                probe.Session.Update();
            Thread.Sleep(1);
        }
    }
}

/// <summary>The session suite, run once per transport by the subclasses at the bottom.</summary>
public abstract class NetSessionTests : IDisposable
{
    private readonly List<NetSession> _sessions = new();
    private string? _hostAddress;

    /// <summary>A transport for a host. <see cref="NetSession.Host"/> starts it listening.</summary>
    protected abstract Transport CreateHostTransport();

    protected abstract Transport CreateClientTransport(string name);

    /// <summary>The address clients connect to, for a host transport that is listening.</summary>
    protected abstract string AddressOf(Transport host);

    /// <summary>An address nothing can be reached at.</summary>
    protected abstract string UnreachableAddress { get; }

    public void Dispose()
    {
        foreach (NetSession session in _sessions)
            session.Dispose();
    }

    private SessionProbe Host(NetSessionOptions? options = null)
    {
        NetSession session = NetSession.Host(CreateHostTransport(), "Hosty", options);
        _sessions.Add(session);
        _hostAddress = AddressOf(session.Transport);
        return new SessionProbe(session);
    }

    private SessionProbe Join(string name, NetSessionOptions? options = null, byte[]? token = null, string? address = null)
    {
        NetSession session = NetSession.Join(CreateClientTransport(name), address ?? _hostAddress!, name, token, options);
        _sessions.Add(session);
        return new SessionProbe(session);
    }

    [Fact]
    public void HostIsPeerZero()
    {
        SessionProbe host = Host();

        Assert.Equal(SessionState.Active, host.Session.State);
        NetPeer self = Assert.Single(host.Session.Peers);
        Assert.Equal(NetPeer.HostId, self.Id);
        Assert.True(self.IsHost && self.IsLocal);
        Assert.Same(self, host.Session.LocalPeer);
        Assert.Same(self, host.Session.HostPeer);
    }

    [Fact]
    public void ClientsJoinAndEveryoneAgreesOnThePeerList()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);
        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => bob.Session.State == SessionState.Active && alice.PeersJoined.Count == 1, host, alice, bob);

        foreach (SessionProbe probe in new[] { host, alice, bob })
        {
            Assert.Equal(
                new[] { (0, "Hosty"), (1, "alice"), (2, "bob") },
                probe.Session.Peers.OrderBy(p => p.Id).Select(p => (p.Id, p.Name)));
        }

        Assert.Equal(1, alice.JoinedCount);
        Assert.Equal("alice", alice.Session.LocalPeer!.Name);
        Assert.True(alice.Session.HostPeer!.IsHost);
        Assert.NotNull(alice.Session.HostPeer.Connection);
        Assert.Null(alice.Session.Peers.Single(p => p.Name == "bob").Connection);

        Assert.Equal(new[] { "alice", "bob" }, host.PeersJoined.Select(p => p.Name));
        Assert.Equal("bob", Assert.Single(alice.PeersJoined).Name);
        Assert.Empty(bob.PeersJoined);
    }

    [Fact]
    public void MessagesFlowBetweenHostAndClients()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => host.Session.Peers.Count == 3 && bob.Session.State == SessionState.Active && alice.Session.Peers.Count == 3, host, alice, bob);

        alice.Session.SendToHost(new ChatMessage("hi host"));
        host.Session.Broadcast(new ChatMessage("hi all"), except: host.Session.Peers.Single(p => p.Name == "bob"), includeLocal: true);
        host.Session.Send(host.Session.Peers.Single(p => p.Name == "bob"), new ChatMessage("just bob"));

        SessionProbe.PumpUntil(() => host.Chat.Count == 2 && alice.Chat.Count == 1 && bob.Chat.Count == 1, host, alice, bob);

        Assert.Contains(host.Chat, c => c.Text == "hi host" && c.Sender.Name == "alice");
        Assert.Contains(host.Chat, c => c.Text == "hi all" && c.Sender.IsLocal);
        Assert.Equal(("Hosty", "hi all"), (alice.Chat[0].Sender.Name, alice.Chat[0].Text));
        Assert.Equal("just bob", bob.Chat[0].Text);
    }

    [Fact]
    public void ClientsCannotMessageEachOtherDirectly()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => alice.Session.Peers.Count == 3, host, alice, bob);

        NetPeer bobSeenByAlice = alice.Session.Peers.Single(p => p.Name == "bob");
        Assert.Throws<InvalidOperationException>(() => alice.Session.Send(bobSeenByAlice, new ChatMessage("psst")));
        Assert.Throws<InvalidOperationException>(() => alice.Session.Broadcast(new ChatMessage("all")));
    }

    [Fact]
    public void UnhandledMessagesAreDropped()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);

        alice.Session.SendToHost(new PositionMessage { X = 1, Y = 2 });
        alice.Session.SendToHost(new ChatMessage("after"));
        SessionProbe.PumpUntil(() => host.Chat.Count == 1, host, alice);

        Assert.Equal(SessionState.Active, alice.Session.State);
    }

    [Fact]
    public void ApprovalCanRejectWithAMessage()
    {
        SessionProbe host = Host();
        host.Session.JoinRequested += pending =>
        {
            if (pending.Request.Name == "mallory")
                pending.Reject("No mallorys.");
            else
                pending.Accept();
        };

        SessionProbe mallory = Join("mallory");
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => mallory.EndedWith != null && alice.Session.State == SessionState.Active, host, mallory, alice);

        Assert.Equal((SessionEndReason.Rejected, "No mallorys."), mallory.EndedWith);
        Assert.Equal(new[] { "alice" }, host.PeersJoined.Select(p => p.Name));
    }

    [Fact]
    public void ApprovalCanBeDeferredAndSeesTheToken()
    {
        SessionProbe host = Host();
        PendingJoin? waiting = null;
        host.Session.JoinRequested += pending => waiting = pending;

        SessionProbe alice = Join("alice", token: new byte[] { 42 });
        SessionProbe.PumpUntil(() => waiting != null, host, alice);
        SessionProbe.PumpFor(30, host, alice);
        Assert.Equal(SessionState.Joining, alice.Session.State);
        Assert.Equal(new byte[] { 42 }, waiting!.Request.Token.ToArray());

        waiting.Accept();
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);
        Assert.True(waiting.IsResolved);
    }

    [Fact]
    public void UndecidedJoinsTimeOut()
    {
        NetSessionOptions options = new() { JoinTimeout = TimeSpan.FromMilliseconds(200) };
        SessionProbe host = Host(options);
        host.Session.JoinRequested += _ => { };

        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.EndedWith != null, host, alice);
        Assert.Equal(SessionEndReason.Timeout, alice.EndedWith!.Value.Reason);
    }

    [Fact]
    public void VersionMismatchIsTurnedAway()
    {
        SessionProbe host = Host(new NetSessionOptions { AppVersion = 2 });
        SessionProbe old = Join("old", new NetSessionOptions { AppVersion = 1 });
        SessionProbe.PumpUntil(() => old.EndedWith != null, host, old);

        Assert.Equal(SessionEndReason.VersionMismatch, old.EndedWith!.Value.Reason);
        Assert.Contains("version 2", old.EndedWith.Value.Message);
    }

    [Fact]
    public void FullSessionIsTurnedAway()
    {
        SessionProbe host = Host(new NetSessionOptions { MaxPeers = 1 });
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);

        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => bob.EndedWith != null, host, alice, bob);
        Assert.Equal(SessionEndReason.ServerFull, bob.EndedWith!.Value.Reason);
    }

    [Fact]
    public void KickEndsTheClientAndTellsEveryone()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => alice.Session.Peers.Count == 3 && bob.Session.Peers.Count == 3, host, alice, bob);

        host.Session.Kick(host.Session.Peers.Single(p => p.Name == "bob"), "Be nice.");
        SessionProbe.PumpUntil(() => bob.EndedWith != null && alice.PeersLeft.Count == 1, host, alice, bob);

        Assert.Equal((SessionEndReason.Kicked, "Be nice."), bob.EndedWith);
        Assert.Equal(("bob", SessionEndReason.Kicked), (alice.PeersLeft[0].Peer.Name, alice.PeersLeft[0].Reason));
        Assert.Equal(("bob", SessionEndReason.Kicked), (host.PeersLeft[0].Peer.Name, host.PeersLeft[0].Reason));
        Assert.Equal(2, host.Session.Peers.Count);
        Assert.Equal(2, alice.Session.Peers.Count);
    }

    [Fact]
    public void ClientLeavingIsSeenByEveryone()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => alice.Session.Peers.Count == 3 && bob.Session.Peers.Count == 3, host, alice, bob);

        bob.Session.Shutdown();
        Assert.Equal((SessionEndReason.Left, (string?)null), bob.EndedWith);

        SessionProbe.PumpUntil(() => alice.PeersLeft.Count == 1, host, alice);
        Assert.Equal(SessionEndReason.Left, alice.PeersLeft[0].Reason);
        Assert.Equal(SessionEndReason.Left, host.PeersLeft[0].Reason);
    }

    [Fact]
    public void HostShutdownEndsEveryClient()
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);

        host.Session.Shutdown("Server restarting.");
        SessionProbe.PumpUntil(() => alice.EndedWith != null, alice);

        Assert.Equal((SessionEndReason.Left, "Server restarting."), host.EndedWith);
        Assert.Equal((SessionEndReason.HostShutdown, "Server restarting."), alice.EndedWith);
        Assert.Empty(alice.Session.Peers);
    }

    [Fact]
    public void MalformedMessagesGetTheSenderRemoved()
    {
        SessionProbe host = Host();
        host.Session.OnRaw(10, (NetPeer sender, ref NetReader reader, Delivery delivery) => reader.ReadInt64());

        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);

        alice.Session.SendRaw(alice.Session.HostPeer!, 10, new byte[] { 1, 2 });
        SessionProbe.PumpUntil(() => alice.EndedWith != null, host, alice);

        Assert.Equal(SessionEndReason.ProtocolError, alice.EndedWith!.Value.Reason);
        Assert.Equal(SessionEndReason.ProtocolError, host.PeersLeft.Single().Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedFragmentsAreRejected(bool toClient)
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);
        using NetWriter chat = new();
        chat.WriteUInt16(ChatMessage.Id);
        chat.WriteString("nested");
        using NetWriter inner = Fragment(chat.WrittenSpan);
        using NetWriter outer = Fragment(inner.WrittenSpan);
        Connection connection = toClient ? host.Session.Peers.Single(p => !p.IsLocal).Connection! : alice.Session.HostPeer!.Connection!;
        connection.Send(outer.WrittenSpan, Delivery.Reliable);
        SessionProbe.PumpFor(100, host, alice);
        Assert.Empty(toClient ? alice.Chat : host.Chat);
        Assert.Equal(SessionState.Ended, alice.Session.State);
    }

    private static NetWriter Fragment(ReadOnlySpan<byte> payload)
    {
        NetWriter writer = new();
        writer.WriteUInt16(NetSession.ReservedMessageIds + 5);
        writer.WriteBool(true);
        writer.WriteVarUInt32((uint)payload.Length);
        writer.WriteBytes(payload);
        return writer;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteFragmentsAreReleasedOnShutdown(bool toClient)
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);
        Connection connection = toClient ? host.Session.Peers.Single(p => !p.IsLocal).Connection! : alice.Session.HostPeer!.Connection!;
        using NetWriter fragment = new();
        fragment.WriteUInt16(NetSession.ReservedMessageIds + 5);
        fragment.WriteBool(true);
        fragment.WriteVarUInt32(100_000);
        fragment.WriteByte(1);
        connection.Send(fragment.WrittenSpan, Delivery.Reliable);
        SessionProbe.PumpFor(30, host, alice);
        NetSession receiver = toClient ? alice.Session : host.Session;
        object? incoming;
        object? remote = null;
        if (toClient)
            incoming = typeof(NetSession).GetField("_incomingFromHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver);
        else
        {
            var remotes = (System.Collections.IDictionary)typeof(NetSession).GetField("_remotes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver)!;
            remote = remotes.Values.Cast<object>().Single();
            incoming = remote.GetType().GetField("Incoming")!.GetValue(remote);
        }
        Assert.NotNull(incoming);
        receiver.Shutdown();
        Assert.Empty((byte[])incoming!.GetType().GetField("Buffer")!.GetValue(incoming)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceLimitReasonSurvivesTheSessionLayer(bool atClient)
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);
        NetSession receiver = atClient ? alice.Session : host.Session;
        receiver.Transport.MaxQueuedDataEventsPerConnection = 1;
        if (atClient)
        {
            NetPeer peer = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(peer, 20, new byte[] { 1 });
            host.Session.SendRaw(peer, 20, new byte[] { 2 });
        }
        else
        {
            alice.Session.SendRaw(alice.Session.HostPeer!, 20, new byte[] { 1 });
            alice.Session.SendRaw(alice.Session.HostPeer!, 20, new byte[] { 2 });
        }
        SessionProbe.PumpFor(100, host, alice);
        string reason = atClient ? alice.EndedWith!.Value.Reason.ToString() : host.PeersLeft.Single().Reason.ToString();
        Assert.Equal("ResourceLimit", reason);
    }

    private static void SendPartial(Connection connection, int total)
    {
        using NetWriter fragment = new();
        fragment.WriteUInt16(NetSession.ReservedMessageIds + 5);
        fragment.WriteBool(true);
        fragment.WriteVarUInt32((uint)total);
        fragment.WriteByte(1);
        connection.Send(fragment.WrittenSpan, Delivery.Reliable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialMessagesExpireEvenWhileTheConnectionIsAlive(bool atClient)
    {
        NetSessionOptions options = new();
        options.ReassemblyTimeout = TimeSpan.FromMilliseconds(30);
        SessionProbe host = Host(options);
        SessionProbe alice = Join("alice", options);
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);
        SendPartial(atClient ? host.Session.Peers.Single(p => !p.IsLocal).Connection! : alice.Session.HostPeer!.Connection!, 100_000);
        SessionProbe.PumpFor(150, host, alice);
        Assert.Equal(SessionState.Ended, alice.Session.State);
        Assert.Equal(SessionEndReason.Timeout, atClient ? alice.EndedWith!.Value.Reason : host.PeersLeft.Single().Reason);
    }

    [Fact]
    public void ReassemblyHasAnAggregateMemoryBudget()
    {
        NetSessionOptions options = new();
        options.MaxReassemblyBytes = 131072;
        SessionProbe host = Host(options);
        SessionProbe alice = Join("alice");
        SessionProbe bob = Join("bob");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active && bob.Session.State == SessionState.Active, host, alice, bob);
        SendPartial(alice.Session.HostPeer!.Connection!, 100_000);
        SendPartial(bob.Session.HostPeer!.Connection!, 100_000);
        SessionProbe.PumpFor(100, host, alice, bob);
        Assert.Equal(2, host.Session.Peers.Count);
        Assert.Equal(SessionEndReason.ResourceLimit, host.PeersLeft.Single().Reason);
    }

    [Fact]
    public void PendingJoinsCannotAllocateFragmentBuffers()
    {
        SessionProbe host = Host();
        bool pending = false;
        host.Session.JoinRequested += _ => pending = true;
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => pending, host, alice);
        SendPartial(alice.Session.Transport.Connections.Single(), 100_000);
        SessionProbe.PumpFor(30, host, alice);
        var remotes = (System.Collections.IDictionary)typeof(NetSession).GetField("_remotes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host.Session)!;
        Assert.All(remotes.Values.Cast<object>(), remote => Assert.Null(remote.GetType().GetField("Incoming")!.GetValue(remote)));
    }

    [Theory]
    [InlineData(Delivery.Reliable)]
    [InlineData(Delivery.Bulk)]
    public void LargeReliableMessagesArriveWhole(Delivery delivery)
    {
        SessionProbe host = Host();
        SessionProbe alice = Join("alice");
        byte[]? received = null;
        alice.Session.OnRaw(20, (NetPeer sender, ref NetReader reader, Delivery delivery) => received = reader.ReadBytes(reader.Remaining).ToArray());
        SessionProbe.PumpUntil(() => host.Session.Peers.Count == 2 && alice.Session.State == SessionState.Active, host, alice);

        byte[] large = TestData.Pattern(1_500_000, seed: 3);
        host.Session.SendRaw(host.Session.Peers.Single(p => !p.IsLocal), 20, large, delivery);
        host.Session.SendToHost(new ChatMessage("still in order after"));
        alice.Session.SendToHost(new ChatMessage("and the other way"));
        SessionProbe.PumpUntil(() => received != null && host.Chat.Count == 2, 10_000, host, alice);

        Assert.Equal(large, received);
    }

    [Fact]
    public void OversizedMessagesGetTheSenderRemoved()
    {
        SessionProbe host = Host(new NetSessionOptions { MaxMessageSize = 10_000 });
        SessionProbe alice = Join("alice");
        SessionProbe.PumpUntil(() => alice.Session.State == SessionState.Active, host, alice);

        alice.Session.SendRaw(alice.Session.HostPeer!, 20, new byte[500_000]);
        SessionProbe.PumpUntil(() => alice.EndedWith != null, host, alice);

        Assert.Equal(SessionEndReason.ProtocolError, alice.EndedWith!.Value.Reason);
    }

    [Fact]
    public void HostCannotBeReached()
    {
        SessionProbe lonely = Join("lonely", address: UnreachableAddress);
        SessionProbe.PumpUntil(() => lonely.EndedWith != null, lonely);
        Assert.Equal(SessionEndReason.Unreachable, lonely.EndedWith!.Value.Reason);
    }

    [Fact]
    public void ReservedAndDuplicateHandlerIdsAreRefused()
    {
        SessionProbe host = Host();
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Session.OnRaw(NetSession.ReservedMessageIds, (NetPeer s, ref NetReader r, Delivery d) => { }));
        Assert.Throws<InvalidOperationException>(() => host.Session.On<ChatMessage>((s, m) => { }));
        Assert.True(host.Session.Off<ChatMessage>());
        host.Session.On<ChatMessage>((s, m) => { });
    }

    [Fact]
    public void SendingWhileJoiningThrows()
    {
        SessionProbe host = Host();
        host.Session.JoinRequested += _ => { };
        SessionProbe alice = Join("alice");

        Assert.Throws<InvalidOperationException>(() => alice.Session.SendToHost(new ChatMessage("too early")));
    }
}

public sealed class LocalNetSessionTests : NetSessionTests
{
    private readonly LocalNetwork _network = new();

    protected override Transport CreateHostTransport() => new LocalTransport(_network, "host");

    protected override Transport CreateClientTransport(string name) => new LocalTransport(_network, name);

    protected override string AddressOf(Transport host) => ((LocalTransport)host).Address;

    protected override string UnreachableAddress => "nobody";
}

public sealed class KcpNetSessionTests : NetSessionTests
{
    protected override Transport CreateHostTransport() => new KcpTransport(KcpTransportContractTests.Options());

    protected override Transport CreateClientTransport(string name) => new KcpTransport(KcpTransportContractTests.Options());

    protected override string AddressOf(Transport host) => $"127.0.0.1:{((KcpTransport)host).LocalPort}";

    protected override string UnreachableAddress => "not a valid host name";
}
