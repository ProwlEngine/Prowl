using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Xunit;

namespace Prowl.Warp.Tests;

public class SessionProcessingTests
{
    [Fact]
    public void TimerIterationRecoversAfterMembershipHandlerThrows()
    {
        using ProbeTransport transport = new();
        using NetSession host = NetSession.CreateHost(transport,
            options: new NetSessionOptions
            {
                ReassemblyTimeout = TimeSpan.FromMilliseconds(20), GoodbyeLinger = TimeSpan.FromMilliseconds(20)
            });
        ProbeConnection connection = JoinPeer(transport, host);
        BeginPartialMessage(host, connection);
        Action<NetPeer, SessionEndReason>
            handler = (_, _) => throw new InvalidOperationException("Application failure");
        host.PeerLeft += handler;
        Thread.Sleep(30);
        Assert.Throws<InvalidOperationException>(host.Flush);
        host.PeerLeft -= handler;
        Thread.Sleep(30);
        host.Flush();
        Assert.Equal(1, connection.DisconnectCalls);
    }

    [Fact]
    public void MembershipHandlerCanDisposeSessionDuringTimerIteration()
    {
        using ProbeTransport transport = new();
        using NetSession host = NetSession.CreateHost(transport,
            options: new NetSessionOptions { ReassemblyTimeout = TimeSpan.FromMilliseconds(20) });
        ProbeConnection first = JoinPeer(transport, host), second = JoinPeer(transport, host);
        BeginPartialMessage(host, first);
        BeginPartialMessage(host, second);
        host.PeerLeft += (_, _) => host.Dispose();
        Thread.Sleep(30);
        host.Flush();
        Assert.Equal(SessionState.Ended, host.State);
        Assert.Equal(0, host.PendingReassemblyBytes);
    }

    private static void BeginPartialMessage(NetSession host, Connection peer)
    {
        using NetWriter fragment = new();
        fragment.WriteUInt16(NetSession.ReservedMessageIds + 5);
        fragment.WriteBool(true);
        fragment.WriteVarUInt32(1024);
        fragment.WriteByte(1);
        host.ProcessEvent(new TransportEvent(TransportEventType.Data, peer, fragment.WrittenMemory));
    }

    private static NetSession CreateHost(Transport transport) => NetSession.CreateHost(transport);
    private static NetSession CreateClient(Connection connection) => NetSession.CreateClient(connection, "client");

    [Fact]
    public void ExternallyPumpedHostDoesNotDriveTransportLifecycle()
    {
        using ProbeTransport transport = new();
        using NetSession session = CreateHost(transport);
        session.Update();
        session.Shutdown();
        Assert.Equal(0, transport.ListenCalls);
        Assert.Equal(0, transport.ReceiveCalls);
        Assert.Equal(0, transport.FlushCalls);
        Assert.Equal(0, transport.ShutdownCalls);
    }

    [Fact]
    public void ExternallyPumpedClientUsesTheSuppliedConnection()
    {
        using ProbeTransport transport = new();
        Connection connection = transport.Connect("host");
        using NetSession session = CreateClient(connection);
        session.Update();
        Assert.Equal(1, transport.ConnectCalls);
        Assert.Equal(0, transport.ReceiveCalls);
        Assert.Equal(0, transport.FlushCalls);
    }

    [Fact]
    public void ExternalEventsDriveAdmissionOnTheCallingThread()
    {
        using ProbeTransport transport = new();
        using NetSession host = CreateHost(transport);
        int callbackThread = 0;
        host.PeerJoined += _ => callbackThread = Environment.CurrentManagedThreadId;
        JoinPeer(transport, host);
        Assert.Equal(2, host.Peers.Count);
        Assert.Equal(Environment.CurrentManagedThreadId, callbackThread);
        Assert.Equal(0, transport.ListenCalls);
        Assert.Equal(0, transport.ReceiveCalls);
        Assert.True(host.IsExternallyPumped);
        Assert.Same(transport, host.Transport);
    }

    [Fact]
    public void FragmentedImmediateSendFlushesTheConnectionRatherThanItsPhysicalTransport()
    {
        using ProbeTransport transport = new();
        using NetSession host = CreateHost(transport);
        ProbeConnection connection = JoinPeer(transport, host);
        host.SendRaw(host.Peers.Single(p => !p.IsLocal), 20, new byte[512], flags: SendFlags.Immediate);
        Assert.Equal(1, connection.FlushCalls);
        Assert.Equal(0, transport.FlushCalls);
    }

    [Fact]
    public void ClientKeepsReliableMessagesOnOtherChannelsThatOvertakeAdmission()
    {
        using ProbeTransport transport = new();
        ProbeConnection connection = transport.OpenPeer();
        using NetSession client = CreateClient(connection);
        List<string> log = new();
        client.Joined += () => log.Add("joined");
        client.OnRaw(30, (_, ref reader, delivery, channel) => log.Add($"{delivery} {channel} {reader.ReadByte()}"));
        client.ProcessEvent(new TransportEvent(TransportEventType.Connected, connection));

        using NetWriter early = new();
        early.WriteUInt16(30);
        early.WriteByte(7);
        client.ProcessEvent(new TransportEvent(TransportEventType.Data, connection, early.WrittenMemory,
            Delivery.Reliable, default, 1));
        // Unreliable messages before joining are dropped.
        client.ProcessEvent(new TransportEvent(TransportEventType.Data, connection, early.WrittenMemory,
            Delivery.Unreliable));
        Assert.Empty(log);
        Assert.True(client.PendingReassemblyBytes > 0);

        using NetWriter accepted = new();
        accepted.WriteUInt16(NetSession.ReservedMessageIds + 1);
        accepted.WriteVarUInt32(1);
        accepted.WriteVarUInt32(2);
        accepted.WriteVarUInt32(NetPeer.HostId);
        accepted.WriteString("host");
        accepted.WriteVarUInt32(1);
        accepted.WriteString("client");
        accepted.WriteBool(false);
        client.ProcessEvent(new TransportEvent(TransportEventType.Data, connection, accepted.WrittenMemory));

        Assert.Equal(new[] { "joined", "Reliable 1 7" }, log);
        Assert.Equal(0, client.PendingReassemblyBytes);
    }

    [Fact]
    public void HostRefusesMessagesOnOtherChannelsBeforeJoining()
    {
        using ProbeTransport transport = new();
        using NetSession host = CreateHost(transport);
        ProbeConnection connection = transport.OpenPeer();
        host.ProcessEvent(new TransportEvent(TransportEventType.Connected, connection));

        using NetWriter message = new();
        message.WriteUInt16(30);
        host.ProcessEvent(new TransportEvent(TransportEventType.Data, connection, message.WrittenMemory,
            Delivery.Reliable, default, 1));
        // Treated as a protocol error, so the join request that follows is ignored.
        SendJoinRequest(host, connection);
        Assert.Single(host.Peers);
    }

    [Fact]
    public void EventsFromAnotherTransportAreRejected()
    {
        using ProbeTransport transport = new(), other = new();
        using NetSession host = CreateHost(transport);
        Connection foreign = other.Connect("other");
        Assert.Throws<ArgumentException>(() =>
            host.ProcessEvent(new TransportEvent(TransportEventType.Connected, foreign)));
    }

    private static ProbeConnection JoinPeer(ProbeTransport transport, NetSession host)
    {
        ProbeConnection connection = transport.OpenPeer();
        host.ProcessEvent(new TransportEvent(TransportEventType.Connected, connection));
        SendJoinRequest(host, connection);
        return connection;
    }

    private static void SendJoinRequest(NetSession host, Connection connection)
    {
        using NetWriter request = new();
        request.WriteUInt16(NetSession.ReservedMessageIds);
        request.WriteUInt32(0);
        request.WriteString("client");
        request.WriteBytesWithLength(ReadOnlySpan<byte>.Empty);
        request.WriteBool(true);
        host.ProcessEvent(new TransportEvent(TransportEventType.Data, connection, request.WrittenMemory));
    }

    private sealed class ProbeTransport : Transport
    {
        public int ListenCalls, ConnectCalls, ReceiveCalls, FlushCalls, ShutdownCalls;
        public override bool IsListening => false;

        public override TransportCapabilities Capabilities =>
            new(true, DeliverySupport.All, TransportExecution.WorkerPreferred, 4);

        public override void Listen() => ListenCalls++;

        public override Connection Connect(string address)
        {
            ConnectCalls++;
            return new ProbeConnection(this, NextConnectionId());
        }

        public override void Receive() => ReceiveCalls++;
        public override void Flush() => FlushCalls++;
        public override void Shutdown() => ShutdownCalls++;

        public ProbeConnection OpenPeer()
        {
            ProbeConnection connection = new(this, NextConnectionId());
            AddConnection(connection);
            RaiseConnected(connection);
            while (Poll(out _))
            {
            }

            return connection;
        }
    }

    private sealed class ProbeConnection(Transport transport, int id) : Connection(transport, id, "host", true)
    {
        public int FlushCalls, DisconnectCalls;
        public override TimeSpan RoundTripTime => TimeSpan.Zero;
        public override int GetMaxMessageSize(Delivery delivery) => 128;

        protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
        {
        }

        public override void Disconnect() => DisconnectCalls++;
        public override void Flush() => FlushCalls++;
    }
}
