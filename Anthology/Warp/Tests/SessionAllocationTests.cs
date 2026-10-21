// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Xunit;

namespace Prowl.Warp.Tests;

[Collection("Allocations")]
public class SessionAllocationTests
{
    [Fact]
    public void EnumeratingPeersDoesNotAllocate()
    {
        using DirectTransport transport = new();
        using NetSession host = NetSession.Host(transport);
        int count = 0;
        for (int i = 0; i < 100; i++) foreach (NetPeer peer in host.Peers) count++;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) foreach (NetPeer peer in host.Peers) count++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1100, count);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(Delivery.Unreliable, 16)]
    [InlineData(Delivery.Reliable, 16)]
    [InlineData(Delivery.Reliable, 512)]
    [InlineData(Delivery.Bulk, 512)]
    public void MessageRoutingDoesNotAllocateAfterWarmup(Delivery delivery, int size)
    {
        using DirectTransport server = new();
        using DirectTransport remote = new() { Other = server };
        using NetSession host = NetSession.Host(server);
        using NetSession client = NetSession.Join(remote, "host", "client");
        for (int i = 0; i < 10; i++) { host.Update(); client.Update(); }
        Assert.Equal(SessionState.Active, client.State);

        int received = 0;
        host.OnRaw(10, (NetPeer peer, ref NetReader reader, Delivery channel) =>
        {
            received += reader.ReadBytes(reader.Remaining).Length;
        });
        client.OnRaw(10, (NetPeer peer, ref NetReader reader, Delivery channel) =>
        {
            received += reader.ReadBytes(reader.Remaining).Length;
        });
        byte[] payload = new byte[size];
        void Round()
        {
            client.SendRaw(client.HostPeer!, 10, payload, delivery);
            host.Update();
            host.BroadcastRaw(10, payload, delivery, includeLocal: true);
            host.Update();
            client.Update();
        }
        for (int i = 0; i < 100; i++) Round();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Round();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3300 * size, received);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TypedMessagesAndIdleUpdatesDoNotAllocateAfterWarmup()
    {
        using DirectTransport server = new();
        using DirectTransport remote = new() { Other = server };
        using NetSession host = NetSession.Host(server);
        using NetSession client = NetSession.Join(remote, "host", "client");
        for (int i = 0; i < 10; i++) { host.Update(); client.Update(); }
        int received = 0;
        host.On<PositionMessage>((peer, message) => received += (int)message.X);
        void Round()
        {
            client.SendToHost(new PositionMessage { X = 1 });
            host.Update();
            client.Update();
            host.Update();
            client.Update();
        }
        for (int i = 0; i < 100; i++) Round();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Round();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1100, received);
        Assert.Equal(0, allocated);
    }

    // No socket, timers, simulation, or per-send objects: measures the session with a warmed
    // transport event queue and pooled payload buffers, including real fragmentation and handshake.
    private sealed class DirectTransport : Transport
    {
        public DirectTransport? Other;
        private DirectConnection? _connection;
        public override bool IsListening { get; }
        public override void Listen() { }
        public override Connection Connect(string address)
        {
            DirectTransport other = Other!;
            _connection = new(this, NextConnectionId());
            other._connection = new(other, other.NextConnectionId());
            _connection.Remote = other._connection;
            other._connection.Remote = _connection;
            AddConnection(_connection);
            other.AddConnection(other._connection);
            RaiseConnected(_connection);
            other.RaiseConnected(other._connection);
            return _connection;
        }
        public override void Receive() { }
        public override void Flush() { }
        public override void Shutdown() => _connection?.Disconnect();

        private sealed class DirectConnection(DirectTransport owner, int id) : Connection(owner, id, "direct", false)
        {
            public DirectConnection Remote = null!;
            public override TimeSpan RoundTripTime => TimeSpan.Zero;
            public override int GetMaxMessageSize(Delivery delivery) => 128;
            public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
            {
                ValidateSend(data, delivery);
                ((DirectTransport)Remote.Transport).RaiseData(Remote, data, delivery);
            }
            public override void Disconnect()
            {
                owner.RaiseDisconnected(this, DisconnectReason.Requested);
                ((DirectTransport)Remote.Transport).RaiseDisconnected(Remote, DisconnectReason.Requested);
            }
        }
    }
}
