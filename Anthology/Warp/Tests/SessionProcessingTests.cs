using System;
using System.Linq;
using Xunit;

namespace Prowl.Warp.Tests;

public class SessionProcessingTests
{
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
    public void EventsFromAnotherTransportAreRejected()
    {
        using ProbeTransport transport = new(), other = new();
        using NetSession host = CreateHost(transport);
        Connection foreign = other.Connect("other");
        Assert.Throws<ArgumentException>(() => host.ProcessEvent(new(TransportEventType.Connected, foreign)));
    }

    private static ProbeConnection JoinPeer(ProbeTransport transport, NetSession host)
    {
        ProbeConnection connection = transport.OpenPeer();
        host.ProcessEvent(new(TransportEventType.Connected, connection));
        using NetWriter request = new();
        request.WriteUInt16(NetSession.ReservedMessageIds);
        request.WriteUInt32(0);
        request.WriteString("client");
        request.WriteBytesWithLength(ReadOnlySpan<byte>.Empty);
        host.ProcessEvent(new(TransportEventType.Data, connection, request.WrittenMemory));
        return connection;
    }

    private sealed class ProbeTransport : Transport
    {
        public int ListenCalls, ConnectCalls, ReceiveCalls, FlushCalls, ShutdownCalls;
        public override bool IsListening => false;
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
            while (Poll(out _)) { }
            return connection;
        }
    }

    private sealed class ProbeConnection(Transport transport, int id) : Connection(transport, id, "host", true)
    {
        public int FlushCalls;
        public override TimeSpan RoundTripTime => TimeSpan.Zero;
        public override int GetMaxMessageSize(Delivery delivery) => 128;
        public override void Send(ReadOnlySpan<byte> data, Delivery delivery) { }
        public override void Disconnect() { }
        public override void Flush() => FlushCalls++;
    }
}
