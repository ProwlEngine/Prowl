// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Courier.Tests;

public sealed class ThreadedTransportTests
{
    [Fact]
    public void AShutdownThatTimesOutKeepsTheTransportAndCanBeRetried()
    {
        BlockingTransport inner = new();
        ThreadedTransport transport = new(inner);
        transport.Listen();
        Assert.True(inner.Entered.Wait(TimeSpan.FromSeconds(2)));

        Assert.Throws<TimeoutException>(() => transport.Shutdown(TimeSpan.FromMilliseconds(20)));
        Assert.Throws<TimeoutException>(() => transport.Shutdown(TimeSpan.FromMilliseconds(20)));
        Assert.False(inner.Disposed);
        Assert.Equal(0, inner.Shutdowns);

        inner.Release.Set();
        transport.Shutdown(TimeSpan.FromSeconds(5));
        Assert.Equal(1, inner.Shutdowns);
        Assert.False(transport.IsListening);
        transport.Dispose();
        Assert.True(inner.Disposed);
    }

    [Fact]
    public void AShutdownWithTimeLeftWorksLikeAPlainOne()
    {
        LocalNetwork network = new();
        using ThreadedTransport host = new(new LocalTransport(network, "host"));
        using ThreadedTransport client = new(new LocalTransport(network, "client"));
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected, what: "connect");
        client.Shutdown(TimeSpan.FromSeconds(5));
        host.Shutdown(TimeSpan.FromSeconds(5));
        Assert.Empty(host.Connections);
        Assert.Empty(client.Connections);
        host.Listen();
        Connection again = client.Connect("host");
        pump.Until(() => again.IsConnected, what: "reconnect");
    }

    [Fact]
    public void QueuedSendsOverTheByteBudgetCloseThatConnectionWithoutStoppingTheWorker()
    {
        LocalNetwork network = new();
        using ThreadedTransport host = new(new LocalTransport(network, "host"));
        using ThreadedTransport client = new(new LocalTransport(network, "client")) { MaxQueuedSendBytes = 64 };
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected, what: "connect");

        connection.Send(new byte[1024], Delivery.Reliable);
        Assert.False(connection.IsConnected);
        pump.Until(() => pump.EventsOf(client).Any(e => e.Type == TransportEventType.Disconnected),
            what: "the backlog to close the connection");
        Assert.Equal(DisconnectReason.ResourceLimit,
            pump.EventsOf(client).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(0, connection.PendingSendBytes);

        Connection next = client.Connect("host");
        pump.Until(() => next.IsConnected, what: "the worker to keep running");
    }

    [Fact]
    public void TheConnectionWithTheLargestBacklogIsClosedNotTheLatestSender()
    {
        GatedTransport inner = new();
        using ThreadedTransport transport = new(inner) { MaxQueuedSendBytes = 4096 };
        transport.Listen();
        Connection big = transport.Connect("big"), small = transport.Connect("small");
        Pump pump = new(transport);
        pump.Until(() => big.IsConnected && small.IsConnected, what: "connect");

        // Hold the worker in Receive so sends queue up instead of draining.
        inner.Open.Reset();
        Assert.True(inner.Waiting.Wait(TimeSpan.FromSeconds(2)));
        big.Send(new byte[3000], Delivery.Reliable);
        small.Send(new byte[2000], Delivery.Reliable);
        Assert.False(big.IsConnected);
        Assert.True(small.IsConnected);
        inner.Open.Set();

        pump.Until(() => inner.SentTo().Length == 1, what: "the small send");
        pump.For(10);
        Assert.Equal(new[] { "small" }, inner.SentTo());
        Assert.True(small.IsConnected);
        Received closed = pump.EventsOf(transport).Single(e => e.Type == TransportEventType.Disconnected);
        Assert.Same(big, closed.Connection);
        Assert.Equal(DisconnectReason.ResourceLimit, closed.Reason);
    }

    [Fact]
    public void TooManyQueuedSendsForOneConnectionCloseOnlyThatConnection()
    {
        GatedTransport inner = new();
        using ThreadedTransport transport = new(inner) { MaxQueuedSendsPerConnection = 8 };
        transport.Listen();
        Connection[] peers = Enumerable.Range(0, 4).Select(i => transport.Connect($"peer{i}")).ToArray();
        Pump pump = new(transport);
        pump.Until(() => peers.All(p => p.IsConnected), what: "connect");

        inner.Open.Reset();
        Assert.True(inner.Waiting.Wait(TimeSpan.FromSeconds(2)));
        // 24 queued in all, more than one connection's limit, but no more than 6 for any one of them.
        for (int i = 0; i < 6; i++)
        {
            foreach (Connection peer in peers)
            {
                peer.Send(new byte[] { 1 }, Delivery.Reliable);
            }
        }

        Assert.All(peers, p => Assert.True(p.IsConnected));
        for (int i = 0; i < 3; i++)
        {
            peers[0].Send(new byte[] { 1 }, Delivery.Reliable);
        }

        Assert.False(peers[0].IsConnected);
        inner.Open.Set();
        pump.Until(() => inner.SentTo().Length == 18, what: "the queued sends");
        pump.For(10);
        Assert.Equal(18, inner.SentTo().Length);
        Assert.DoesNotContain("peer0", inner.SentTo());
        Assert.All(peers.Skip(1), p => Assert.True(p.IsConnected));
        Assert.Equal(DisconnectReason.ResourceLimit,
            pump.EventsOf(transport).Single(e => e.Type == TransportEventType.Disconnected).Reason);
    }

    [Fact]
    public void QueueLimitsDefaultToFourThousandSendsAndSixtyFourMegabytes()
    {
        using ThreadedTransport transport = new(new LocalTransport(new LocalNetwork(), "host"));
        Assert.Equal(4096, transport.MaxQueuedSendsPerConnection);
        Assert.Equal(64L * 1024 * 1024, transport.MaxQueuedSendBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.MaxQueuedSendsPerConnection = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.MaxQueuedSendBytes = 0);
    }

    private sealed class BlockingTransport : Transport
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public bool Disposed;
        public int Shutdowns;
        public override bool IsListening => true;
        public override void Listen() { }
        public override Connection Connect(string address) => throw new NotSupportedException();

        public override void Receive()
        {
            Entered.Set();
            Release.Wait();
        }

        public override void Flush() { }
        public override void Shutdown() => Shutdowns++;
        protected override void DisposeCore() => Disposed = true;
    }

    /// <summary>Connects at once and can hold the worker in Receive, so sends queue up.</summary>
    private sealed class GatedTransport : Transport
    {
        public readonly ManualResetEventSlim Open = new(true), Waiting = new(false);
        private readonly List<string> _sent = new();
        public override bool IsListening => true;
        public override void Listen() { }

        public override Connection Connect(string address)
        {
            GatedConnection connection = new(this, NextConnectionId(), address);
            AddConnection(connection);
            RaiseConnected(connection);
            return connection;
        }

        public override void Receive()
        {
            if (Open.IsSet)
            {
                return;
            }

            Waiting.Set();
            Open.Wait();
        }

        public override void Flush() { }
        public override void Shutdown() => Open.Set();

        public string[] SentTo()
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }

        private sealed class GatedConnection(GatedTransport owner, int id, string address)
            : Connection(owner, id, address, true)
        {
            public override TimeSpan RoundTripTime => TimeSpan.Zero;
            public override int GetMaxMessageSize(Delivery delivery) => 1 << 20;
            public override int GetBatchThreshold(Delivery delivery) => 1 << 20;

            protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
            {
                lock (owner._sent)
                {
                    owner._sent.Add(Address);
                }
            }

            public override void Disconnect() => owner.RaiseDisconnected(this, DisconnectReason.Requested);
        }
    }
}
