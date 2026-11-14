using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Prowl.Courier.Transports.Kcp;
using Prowl.Courier.Transports.Local;
using Prowl.Courier;
using Prowl.Runtime.Networking;
using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>The networking worker sessions run their transport on, as <see cref="Networking.Networking.CreateWorker" /> sets it up.</summary>
public class NetworkingWorkerTests
{
    private static ThreadedTransport CreateWorker(Transport inner) => Networking.Networking.CreateWorker(inner);

    [Fact]
    public void ShutdownTimeoutRetainsTransportUntilWorkerExits()
    {
        using BlockingTransport transport = new();
        ThreadedTransport worker = CreateWorker(transport);
        try
        {
            worker.Listen();
            Assert.True(transport.Entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.Throws<TimeoutException>(() => worker.Shutdown(TimeSpan.FromMilliseconds(20)));
            Assert.False(transport.Disposed);
        }
        finally { transport.Release.Set(); }
        worker.Shutdown(TimeSpan.FromSeconds(5));
        worker.Dispose();
        Assert.True(transport.Disposed);
    }

    internal sealed class BlockingTransport : Transport
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public bool Disposed;
        public override bool IsListening => true;
        public override void Listen() { }
        public override Connection Connect(string address) => throw new NotSupportedException();
        public override void Receive() { Entered.Set(); Release.Wait(); }
        public override void Flush() { }
        public override void Shutdown() { }
        protected override void DisposeCore() { Disposed = true; Entered.Dispose(); Release.Dispose(); }
    }

    [Fact]
    public void BatchedUdpSessionsReassembleLargeMessagesAndPreserveShutdownReasons()
    {
        ThreadedTransport hostWorker = CreateWorker(new KcpTransport(new() { Port = 0 }));
        ThreadedTransport clientWorker = CreateWorker(new KcpTransport(new() { Port = 0 }));
        using NetSession host = NetSession.Host(hostWorker);
        using NetSession client = NetSession.Join(clientWorker, $"127.0.0.1:{hostWorker.LocalPort}", "client");
        PumpUntil(() => client.State == SessionState.Active, host, client);
        byte[] payload = new byte[350_000];
        new Random(42).NextBytes(payload);
        byte[]? received = null;
        client.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery, byte channel) => received = reader.ReadBytes(reader.Remaining).ToArray());
        host.SendRaw(host.Peers.Single(p => !p.IsLocal), 20, payload);
        PumpUntil(() => received != null, host, client);
        Assert.Equal(payload, received);
        Assert.Equal(0, client.PendingReassemblyBytes);
        (SessionEndReason Reason, string? Message)? ended = null;
        client.Ended += (reason, message) => ended = (reason, message);
        host.Shutdown("restarting");
        PumpUntil(() => ended != null, host, client);
        Assert.Equal((SessionEndReason.HostShutdown, "restarting"), ended);
        Assert.False(hostWorker.IsListening);
    }

    [Fact]
    public void KeepalivesContinueWhileTheGameThreadIsStalled()
    {
        KcpTransportOptions Options() => new() { Port = 0, Timeout = TimeSpan.FromMilliseconds(300), PingInterval = TimeSpan.FromMilliseconds(20) };
        using ThreadedTransport host = CreateWorker(new KcpTransport(Options()));
        using ThreadedTransport client = CreateWorker(new KcpTransport(Options()));
        host.Listen();
        Assert.NotEqual(0, host.LocalPort);
        Connection connection = client.Connect($"127.0.0.1:{host.LocalPort}");
        TransportPump pump = new(host, client);
        pump.Until(() => connection.IsConnected && pump.EventsOf(host).Any(e => e.Type == TransportEventType.Connected));
        long sent = connection.Stats.PacketsSent;
        Thread.Sleep(700);
        Assert.True(connection.Stats.PacketsSent > sent + 3, "The networking worker must send keepalives without game-thread pumping.");
        pump.For(30);
        Assert.True(connection.IsConnected);
        Assert.DoesNotContain(pump.EventsOf(host), e => e.Type == TransportEventType.Disconnected);
    }

    [Fact]
    public void SendsCopyBorrowedDataAndCallbacksStayOnTheCallerThread()
    {
        LocalNetwork network = new();
        using NetSession host = NetSession.Host(CreateWorker(new LocalTransport(network, "host")));
        using NetSession client = NetSession.Join(CreateWorker(new LocalTransport(network, "client")), "host", "client");
        PumpUntil(() => client.State == SessionState.Active, host, client);
        int callerThread = Environment.CurrentManagedThreadId, callbackThread = 0;
        byte[]? received = null;
        host.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery, byte channel) =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            received = reader.ReadBytes(reader.Remaining).ToArray();
        });
        byte[] data = { 1, 2, 3 };
        client.SendRaw(client.HostPeer!, 20, data);
        data.AsSpan().Fill(99);
        Thread.Sleep(30);
        Assert.Null(received);
        PumpUntil(() => received != null, host, client);
        Assert.Equal(callerThread, callbackThread);
        Assert.Equal(new byte[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void ShutdownJoinsWorkerReclaimsQueuesAndAllowsReuse()
    {
        LocalNetwork network = new();
        ThreadedTransport host = CreateWorker(new LocalTransport(network, "host"));
        ThreadedTransport client = CreateWorker(new LocalTransport(network, "client"));
        Connection? last = null;
        for (int round = 0; round < 3; round++)
        {
            host.Listen();
            Connection connection = client.Connect("host");
            last = connection;
            TransportPump pump = new(host, client);
            pump.Until(() => connection.IsConnected);
            for (int i = 0; i < 32; i++) connection.Send(new byte[1024], Delivery.Reliable);
            client.Shutdown(TimeSpan.FromSeconds(5));
            host.Shutdown(TimeSpan.FromSeconds(5));
            Assert.Equal(0, connection.PendingSendBytes);
            Assert.False(host.IsListening);
            Assert.Empty(host.Connections);
            Assert.Empty(client.Connections);
            while (host.Poll(out _)) { }
            while (client.Poll(out _)) { }
        }
        host.Dispose();
        client.Dispose();
        // Connection handles may outlive a disposed transport; Disconnect remains idempotent.
        last!.Disconnect();
        Assert.Throws<ObjectDisposedException>(host.Listen);
        Assert.Throws<ObjectDisposedException>(() => host.Connect("host"));
    }

    [Fact]
    public void OutgoingBudgetClosesConnectionWithoutStoppingWorker()
    {
        LocalNetwork network = new();
        using ThreadedTransport host = CreateWorker(new LocalTransport(network, "host"));
        using ThreadedTransport client = CreateWorker(new LocalTransport(network, "client"));
        client.MaxQueuedSendBytes = 64;
        host.Listen();
        Connection connection = client.Connect("host");
        TransportPump pump = new(host, client);
        pump.Until(() => connection.IsConnected);
        connection.Send(new byte[1024], Delivery.Reliable);
        pump.Until(() => pump.EventsOf(client).Any(e => e.Type == TransportEventType.Disconnected));
        Assert.Equal(DisconnectReason.ResourceLimit, pump.EventsOf(client).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(0, connection.PendingSendBytes);
        Connection next = client.Connect("host");
        pump.Until(() => next.IsConnected);
    }

    [Fact]
    public void OutgoingBudgetClosesTheLargestBacklogNotTheLatestSender()
    {
        GatedTransport transport = new();
        using ThreadedTransport worker = CreateWorker(transport);
        worker.MaxQueuedSendBytes = 4096;
        worker.Listen();
        Connection big = worker.Connect("big"), small = worker.Connect("small");
        TransportPump pump = new(worker);
        pump.Until(() => big.IsConnected && small.IsConnected);
        // Hold the worker in Receive so sends queue up instead of draining.
        transport.Open.Reset();
        Assert.True(transport.Waiting.Wait(TimeSpan.FromSeconds(2)));
        big.Send(new byte[3000], Delivery.Reliable);
        small.Send(new byte[100], Delivery.Reliable);
        small.Send(new byte[1000], Delivery.Reliable);
        transport.Open.Set();
        pump.Until(() => pump.EventsOf(worker).Any(e => e.Type == TransportEventType.Disconnected) && transport.SentTo().Length == 2);
        pump.For(10);
        Assert.False(big.IsConnected);
        Assert.True(small.IsConnected);
        Assert.Equal(DisconnectReason.ResourceLimit,
            pump.EventsOf(worker).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(new[] { "small", "small" }, transport.SentTo());
        Assert.Equal(0, small.PendingSendBytes);
    }

    [Fact]
    public void ABurstAcrossManyPeersStaysWithinEachPeersLimit()
    {
        GatedTransport transport = new();
        using ThreadedTransport worker = CreateWorker(transport);
        worker.MaxQueuedSendsPerConnection = 8;
        worker.Listen();
        Connection[] peers = Enumerable.Range(0, 4).Select(i => worker.Connect($"peer{i}")).ToArray();
        TransportPump pump = new(worker);
        pump.Until(() => peers.All(p => p.IsConnected));
        transport.Open.Reset();
        Assert.True(transport.Waiting.Wait(TimeSpan.FromSeconds(2)));
        // 24 queued in all, more than the limit, but no more than 6 for any one peer.
        for (int i = 0; i < 6; i++)
            foreach (Connection peer in peers)
                peer.Send(new byte[] { 1 }, Delivery.Reliable);
        transport.Open.Set();
        pump.Until(() => transport.SentTo().Length == 24);
        Assert.All(peers, p => Assert.True(p.IsConnected));
        Assert.DoesNotContain(pump.EventsOf(worker), e => e.Type == TransportEventType.Disconnected);
    }

    [Fact]
    public void StalledGameThreadHasBoundedIncomingStorageAndHealthyPeersSurvive()
    {
        LocalNetwork network = new();
        using ThreadedTransport host = CreateWorker(new LocalTransport(network, "host"));
        host.MaxQueuedDataBytesPerConnection = 64;
        using ThreadedTransport badClient = CreateWorker(new LocalTransport(network, "bad"));
        using ThreadedTransport healthyClient = CreateWorker(new LocalTransport(network, "healthy"));
        host.Listen();
        Connection bad = badClient.Connect("host"), healthy = healthyClient.Connect("host");
        TransportPump pump = new(host, badClient, healthyClient);
        pump.Until(() => bad.IsConnected && healthy.IsConnected && pump.EventsOf(host).Count(e => e.Type == TransportEventType.Connected) == 2);
        bad.Send(new byte[1024], Delivery.Reliable);
        healthy.Send(new byte[] { 42 }, Delivery.Reliable);
        Thread.Sleep(30);
        pump.Until(() => pump.EventsOf(host).Any(e => e.Type == TransportEventType.Disconnected) &&
            pump.EventsOf(host).Any(e => e.Type == TransportEventType.Data));
        Assert.Equal(DisconnectReason.ResourceLimit, pump.EventsOf(host).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(new byte[] { 42 }, pump.EventsOf(host).Single(e => e.Type == TransportEventType.Data).Data);
        Assert.True(healthy.IsConnected);
    }

    [Fact]
    public void WorkerOwnsTransportPumpingAndFailuresAreReportedToCaller()
    {
        ProbeTransport inner = new();
        using ThreadedTransport worker = CreateWorker(inner);
        int caller = Environment.CurrentManagedThreadId;
        worker.Listen();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.ReceiveThread) != 0, 1000));
        Assert.NotEqual(caller, inner.ReceiveThread);
        inner.Fail = true;
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.Failed), 1000));
        Thread.Sleep(20);
        Assert.Throws<InvalidOperationException>(worker.Receive);
        worker.Shutdown(TimeSpan.FromSeconds(5));
        inner.Fail = false;
        Volatile.Write(ref inner.ReceiveThread, 0);
        worker.Listen();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.ReceiveThread) != 0, 1000));
    }

    [Fact]
    public void QueuedConnectionSendsAndFlushesStayOnTheNetworkingThread()
    {
        ProbeTransport transport = new();
        using ThreadedTransport worker = CreateWorker(transport);
        worker.Listen();
        Connection connection = worker.Connect("probe");
        TransportPump pump = new(worker);
        pump.Until(() => connection.IsConnected);
        Assert.Same(worker, connection.Transport);
        Assert.Equal(64, connection.GetBatchThreshold(Delivery.Reliable));
        connection.Send(new byte[] { 42 }, Delivery.Reliable, SendFlags.Immediate);
        connection.Flush();
        pump.Until(() => Volatile.Read(ref transport.SendThread) != 0);
        Assert.Equal(transport.ReceiveThread, transport.SendThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, transport.SendThread);
    }

    private static void PumpUntil(Func<bool> condition, params NetSession[] sessions)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Sessions did not reach the expected state.");
            foreach (NetSession session in sessions)
                session.Update();
            Thread.Sleep(1);
        }
    }

    /// <summary>Drains transports like a game loop would, recording every event with its data copied out.</summary>
    private sealed class TransportPump(params Transport[] transports)
    {
        private readonly Dictionary<Transport, List<(TransportEventType Type, Connection Connection, byte[] Data, DisconnectReason Reason)>> _events =
            transports.ToDictionary(t => t, _ => new List<(TransportEventType, Connection, byte[], DisconnectReason)>());

        public List<(TransportEventType Type, Connection Connection, byte[] Data, DisconnectReason Reason)> EventsOf(Transport transport) => _events[transport];

        public void Until(Func<bool> condition, int timeoutMs = 5000)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Timed out waiting for the transports.");
                Step();
                Thread.Sleep(1);
            }
        }

        public void For(int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                Step();
                Thread.Sleep(1);
            }
        }

        private void Step()
        {
            foreach (Transport transport in transports)
            {
                transport.Receive();
                while (transport.Poll(out TransportEvent ev))
                    _events[transport].Add((ev.Type, ev.Connection, ev.Data.ToArray(), ev.Reason));
                transport.Flush();
            }
        }
    }

    /// <summary>Connects instantly and can hold the worker in Receive, so sends queue up.</summary>
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
            if (Open.IsSet) return;
            Waiting.Set();
            Open.Wait();
        }
        public override void Flush() { }
        public override void Shutdown() => Open.Set();
        public string[] SentTo() { lock (_sent) return _sent.ToArray(); }

        private sealed class GatedConnection(GatedTransport owner, int id, string address) : Connection(owner, id, address, true)
        {
            public override TimeSpan RoundTripTime => TimeSpan.Zero;
            public override int GetMaxMessageSize(Delivery delivery) => 1 << 20;
            public override int GetBatchThreshold(Delivery delivery) => 1 << 20;
            protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel) { lock (owner._sent) owner._sent.Add(Address); }
            public override void Disconnect() => owner.RaiseDisconnected(this, DisconnectReason.Requested);
        }
    }

    /// <summary>Records which thread drives it, and asserts every call after the first comes from that thread.</summary>
    private sealed class ProbeTransport : Transport
    {
        public int ReceiveThread, SendThread;
        public volatile bool Fail, Failed;
        public override bool IsListening => true;
        public override void Listen() { }
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
            if (!Fail) return;
            Failed = true;
            throw new InvalidOperationException("Injected worker failure.");
        }
        public override void Flush() => AssertWorkerThread();
        public override void Shutdown() { }

        private void AssertWorkerThread()
        {
            int worker = Volatile.Read(ref ReceiveThread);
            if (worker != 0 && worker != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Transport I/O escaped the networking thread.");
        }

        private sealed class ProbeConnection(ProbeTransport owner, int id) : Connection(owner, id, "probe", true)
        {
            public override TimeSpan RoundTripTime => TimeSpan.Zero;
            public override int GetMaxMessageSize(Delivery delivery) => 128;
            public override int GetBatchThreshold(Delivery delivery) => 64;
            protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
            {
                owner.AssertWorkerThread();
                Volatile.Write(ref owner.SendThread, Environment.CurrentManagedThreadId);
            }
            public override void Disconnect() { }
        }
    }
}
