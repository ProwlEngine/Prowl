// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using Prowl.Courier.Transports.Kcp;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Courier.Tests;

public sealed class ThreadedTransportTests
{
    // The worker runs until the transport is disposed, so whatever it copied from the thread that started it
    // would stay alive that long.
    [Fact]
    public void TheWorkerDoesNotKeepTheAsyncLocalsOfTheThreadThatStartedIt()
    {
        AsyncLocal<object?> local = new();
        WeakReference held = TestData.HoldInAsyncLocal(local);
        using ThreadedTransport transport = new(new LocalTransport(new LocalNetwork(), "host"));
        transport.Listen();
        local.Value = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(held.IsAlive, "the worker kept an object its starter held in an AsyncLocal");
    }

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

    [Fact]
    public void AShutdownLeavesNothingPendingAndTheTransportCanBeUsedAgain()
    {
        LocalNetwork network = new();
        ThreadedTransport host = new(new LocalTransport(network, "host"));
        ThreadedTransport client = new(new LocalTransport(network, "client"));
        Connection? last = null;
        for (int round = 0; round < 3; round++)
        {
            host.Listen();
            Connection connection = client.Connect("host");
            last = connection;
            Pump pump = new(host, client);
            pump.Until(() => connection.IsConnected);
            for (int i = 0; i < 32; i++)
            {
                connection.Send(new byte[1024], Delivery.Reliable);
            }

            client.Shutdown(TimeSpan.FromSeconds(5));
            host.Shutdown(TimeSpan.FromSeconds(5));

            // Whatever the worker last saw queued on the inner connection went with it.
            Assert.Equal(0, connection.PendingSendBytes);
            Assert.False(host.IsListening);
            Assert.Empty(host.Connections);
            Assert.Empty(client.Connections);
            while (host.Poll(out _))
            {
            }

            while (client.Poll(out _))
            {
            }
        }

        host.Dispose();
        client.Dispose();

        // A connection handle may outlive its transport, and disconnecting it again does nothing.
        last!.Disconnect();
        Assert.Throws<ObjectDisposedException>(host.Listen);
        Assert.Throws<ObjectDisposedException>(() => host.Connect("host"));
    }

    [Fact]
    public void KeepalivesGoOutWhileTheMainThreadIsStalled()
    {
        KcpTransportOptions Options() => new()
        {
            Port = 0,
            Timeout = TimeSpan.FromMilliseconds(300),
            PingInterval = TimeSpan.FromMilliseconds(20)
        };
        using ThreadedTransport host = new(new KcpTransport(Options()));
        using ThreadedTransport client = new(new KcpTransport(Options()));
        host.Listen();
        Assert.NotEqual(0, host.LocalPort);
        Connection connection = client.Connect($"127.0.0.1:{host.LocalPort}");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected && pump.EventsOf(host).Any(e => e.Type == TransportEventType.Connected));

        // Longer than the timeout with nobody pumping: only the worker's pings keep it alive.
        long sent = connection.Stats.PacketsSent;
        Thread.Sleep(700);
        Assert.True(connection.Stats.PacketsSent > sent + 3, "the worker sent no keepalives while the main thread slept");
        pump.For(30);
        Assert.True(connection.IsConnected);
        Assert.DoesNotContain(pump.EventsOf(host), e => e.Type == TransportEventType.Disconnected);
    }

    [Fact]
    public void ASessionOnAWorkerCopiesWhatItSendsAndRunsHandlersOnTheCallingThread()
    {
        LocalNetwork network = new();
        using NetSession host = NetSession.Host(new ThreadedTransport(new LocalTransport(network, "host")));
        using NetSession client = NetSession.Join(new ThreadedTransport(new LocalTransport(network, "client")), "host", "client");
        UpdateUntil(() => client.State == SessionState.Active, host, client);
        int callerThread = Environment.CurrentManagedThreadId, handlerThread = 0;
        byte[]? received = null;
        host.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery, byte channel) =>
        {
            handlerThread = Environment.CurrentManagedThreadId;
            received = reader.ReadBytes(reader.Remaining).ToArray();
        });

        // The caller's buffer is its own again as soon as the send returns.
        byte[] data = [1, 2, 3];
        client.SendRaw(client.HostPeer!, 20, data);
        data.AsSpan().Fill(99);

        // Nothing is handled until the host is updated, however long the worker has had the message.
        Thread.Sleep(30);
        Assert.Null(received);
        UpdateUntil(() => received != null, host, client);
        Assert.Equal(callerThread, handlerThread);
        Assert.Equal(new byte[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void TheWorkerDrivesTheInnerTransportAndItsFailureReachesTheCaller()
    {
        ProbeTransport inner = new();
        using ThreadedTransport transport = new(inner);
        int caller = Environment.CurrentManagedThreadId;
        transport.Listen();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.ReceiveThread) != 0, 1000));
        Assert.NotEqual(caller, inner.ReceiveThread);

        inner.Fail = true;
        Assert.True(SpinWait.SpinUntil(() => inner.Failed, 1000));
        Thread.Sleep(20);
        Assert.Throws<InvalidOperationException>(transport.Receive);

        // A shutdown clears the failure, and listening again starts a new worker.
        transport.Shutdown(TimeSpan.FromSeconds(5));
        inner.Fail = false;
        Volatile.Write(ref inner.ReceiveThread, 0);
        transport.Listen();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.ReceiveThread) != 0, 1000));
    }

    [Fact]
    public void SendsFlushesTrustAndBacklogGoThroughTheWorker()
    {
        ProbeTransport inner = new();
        using ThreadedTransport transport = new(inner);
        transport.Listen();
        Connection connection = transport.Connect("probe");
        Pump pump = new(transport);
        pump.Until(() => connection.IsConnected);
        Assert.Same(transport, connection.Transport);
        Assert.Equal(64, connection.GetBatchThreshold(Delivery.Reliable));

        // The probe throws if anything but the worker touches it.
        connection.Send(new byte[] { 42 }, Delivery.Reliable, SendFlags.Immediate);
        connection.Flush();
        pump.Until(() => Volatile.Read(ref inner.SendThread) != 0);
        Assert.Equal(inner.ReceiveThread, inner.SendThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, inner.SendThread);

        connection.MarkTrusted();
        pump.Until(() => Volatile.Read(ref inner.Trusted));
        Volatile.Write(ref inner.Backlog, 1234);
        pump.Until(() => connection.PendingSendBytes >= 1234);
    }

    private static void UpdateUntil(Func<bool> condition, params NetSession[] sessions)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 5000)
            {
                throw new TimeoutException("The sessions did not reach the expected state.");
            }

            foreach (NetSession session in sessions)
            {
                session.Update();
            }

            Thread.Sleep(1);
        }
    }

    /// <summary>Records which thread drives it, and throws when a later call comes from another one.</summary>
    private sealed class ProbeTransport : Transport
    {
        public int ReceiveThread, SendThread;
        public long Backlog;
        public volatile bool Fail, Failed;
        public bool Trusted;

        public override bool IsListening => true;

        public override void Listen()
        {
        }

        public override Connection Connect(string address)
        {
            AssertWorkerThread();
            ProbeConnection connection = new(this, NextConnectionId());
            AddConnection(connection);
            RaiseConnected(connection);
            return connection;
        }

        public override void Receive()
        {
            Interlocked.CompareExchange(ref ReceiveThread, Environment.CurrentManagedThreadId, 0);
            AssertWorkerThread();
            if (!Fail)
            {
                return;
            }

            Failed = true;
            throw new InvalidOperationException("Injected worker failure.");
        }

        public override void Flush() => AssertWorkerThread();

        public override void Shutdown()
        {
        }

        private void AssertWorkerThread()
        {
            int worker = Volatile.Read(ref ReceiveThread);
            if (worker != 0 && worker != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("Transport I/O left the worker thread.");
            }
        }

        private sealed class ProbeConnection(ProbeTransport owner, int id) : Connection(owner, id, "probe", true)
        {
            public override TimeSpan RoundTripTime => TimeSpan.Zero;

            public override long PendingSendBytes => Volatile.Read(ref owner.Backlog);

            public override int GetMaxMessageSize(Delivery delivery) => 128;

            public override int GetBatchThreshold(Delivery delivery) => 64;

            protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
            {
                owner.AssertWorkerThread();
                Volatile.Write(ref owner.SendThread, Environment.CurrentManagedThreadId);
            }

            protected override void OnTrusted()
            {
                owner.AssertWorkerThread();
                Volatile.Write(ref owner.Trusted, true);
            }

            public override void Disconnect()
            {
            }
        }
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
