using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;
using Prowl.Warp;
using Prowl.Runtime.Networking;
using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkingThreadTests
{
    [Fact]
    public void ShutdownTimeoutRetainsTransportUntilWorkerExits()
    {
        using BlockingTransport transport = new();
        using NetworkingThread thread = new(transport);
        try
        {
            thread.Listen();
            Assert.True(transport.Entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.Throws<TimeoutException>(() => thread.Shutdown(TimeSpan.FromMilliseconds(20)));
            Assert.False(transport.Disposed);
            Assert.True(thread.IsWorkerRunning);
        }
        finally { transport.Release.Set(); }
        thread.Shutdown();
        Assert.False(thread.IsWorkerRunning);
        thread.Dispose();
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

    private static NetworkingThread CreateThread(Transport inner) => new NetworkingThread(inner);


    private static NetSession CreateHostSession(NetworkingThread thread)
    {
        thread.Listen();
        NetSession session = NetSession.CreateHost(thread.Transport);
        session.Ended += (_, _) => thread.Shutdown();
        return session;
    }

    private static NetSession CreateClientSession(NetworkingThread thread, string address, string name)
    {
        NetSession session = NetSession.CreateClient(thread.Connect(address), name);
        session.Ended += (_, _) => thread.Shutdown();
        return session;
    }

    [Fact]
    public void BatchedUdpSessionsReassembleLargeMessagesAndPreserveShutdownReasons()
    {
        Transport Stack() => new KcpTransport(new() { Port = 0 });
        using NetworkingThread hostThread = new(Stack()), clientThread = new(Stack());
        using NetSession host = CreateHostSession(hostThread);
        using NetSession client = CreateClientSession(clientThread, $"127.0.0.1:{hostThread.LocalPort}", "client");
        WorkerSessionProbe hostProbe = new(host, hostThread), clientProbe = new(client, clientThread);
        WorkerSessionProbe.PumpUntil(() => client.State == SessionState.Active, hostProbe, clientProbe);
        byte[] payload = new byte[350_000];
        new Random(42).NextBytes(payload);
        byte[]? received = null;
        client.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery, byte channel) => received = reader.ReadBytes(reader.Remaining).ToArray());
        host.SendRaw(host.Peers.Single(p => !p.IsLocal), 20, payload);
        WorkerSessionProbe.PumpUntil(() => received != null, hostProbe, clientProbe);
        Assert.Equal(payload, received);
        Assert.Equal(0, client.PendingReassemblyBytes);
        (SessionEndReason Reason, string? Message)? ended = null;
        client.Ended += (reason, message) => ended = (reason, message);
        host.Shutdown("restarting");
        WorkerSessionProbe.PumpUntil(() => ended != null, hostProbe, clientProbe);
        Assert.Equal((SessionEndReason.HostShutdown, "restarting"), ended);
        Assert.False(hostThread.IsWorkerRunning);
        Assert.False(clientThread.IsWorkerRunning);
    }

    [Fact]
    public void KeepalivesContinueWhileTheGameThreadIsStalled()
    {
        KcpTransportOptions Options() => new() { Port = 0, Timeout = TimeSpan.FromMilliseconds(300), PingInterval = TimeSpan.FromMilliseconds(20) };
        KcpTransport rawHost = new(Options());
        using NetworkingThread host = CreateThread(rawHost);
        using NetworkingThread client = CreateThread(new KcpTransport(Options()));
        host.Listen();
        Assert.NotEqual(0, host.LocalPort);
        Connection connection = client.Connect($"127.0.0.1:{host.LocalPort}");
        WorkerPump pump = new(host, client);
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
        using NetworkingThread hostThread = CreateThread(new LocalTransport(network, "host"));
        using NetworkingThread clientThread = CreateThread(new LocalTransport(network, "client"));
        using NetSession host = CreateHostSession(hostThread);
        using NetSession client = CreateClientSession(clientThread, "host", "client");
        WorkerSessionProbe hostProbe = new(host, hostThread), clientProbe = new(client, clientThread);
        WorkerSessionProbe.PumpUntil(() => client.State == SessionState.Active, hostProbe, clientProbe);
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
        WorkerSessionProbe.PumpUntil(() => received != null, hostProbe, clientProbe);
        Assert.Equal(callerThread, callbackThread);
        Assert.Equal(new byte[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void ShutdownJoinsWorkerReclaimsQueuesAndAllowsReuse()
    {
        LocalNetwork network = new();
        using NetworkingThread host = new(new LocalTransport(network, "host"));
        using NetworkingThread client = new(new LocalTransport(network, "client"));
        Connection? last = null;
        for (int round = 0; round < 3; round++)
        {
            host.Listen();
            Connection connection = client.Connect("host");
            last = connection;
            WorkerPump pump = new(host, client);
            pump.Until(() => connection.IsConnected);
            for (int i = 0; i < 32; i++) connection.Send(new byte[1024], Delivery.Reliable);
            client.Shutdown(); host.Shutdown();
            Assert.False(host.IsWorkerRunning);
            Assert.False(client.IsWorkerRunning);
            Assert.Equal(0, client.PendingSendBytes);
            Assert.Equal(0, host.PendingReceiveBytes);
            Assert.Empty(host.Connections);
            Assert.Empty(client.Connections);
            while (host.TryReadEvent(out _)) { }
            while (client.TryReadEvent(out _)) { }
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
        using NetworkingThread host = new(new LocalTransport(network, "host"));
        using NetworkingThread client = new(new LocalTransport(network, "client"), maxQueuedBytes: 64);
        host.Listen();
        Connection connection = client.Connect("host");
        WorkerPump pump = new(host, client);
        pump.Until(() => connection.IsConnected);
        connection.Send(new byte[1024], Delivery.Reliable);
        pump.Until(() => !connection.IsConnected);
        Assert.Equal(DisconnectReason.ResourceLimit, pump.EventsOf(client).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(0, client.PendingSendBytes);
        Assert.True(host.IsWorkerRunning);
    }

    [Fact]
    public void OutgoingBudgetClosesTheLargestBacklogNotTheLatestSender()
    {
        GatedTransport transport = new();
        using NetworkingThread thread = new(transport, maxQueuedBytes: 4096);
        thread.Listen();
        Connection big = thread.Connect("big"), small = thread.Connect("small");
        WorkerPump pump = new(thread);
        pump.Until(() => big.IsConnected && small.IsConnected);
        // Hold the worker in Receive so sends queue up instead of draining.
        transport.Open.Reset();
        Assert.True(transport.Waiting.Wait(TimeSpan.FromSeconds(2)));
        big.Send(new byte[3000], Delivery.Reliable);
        small.Send(new byte[100], Delivery.Reliable);
        transport.Open.Set();
        pump.Until(() => !big.IsConnected);
        pump.For(10);
        Assert.True(small.IsConnected);
        Assert.Equal(DisconnectReason.ResourceLimit,
            pump.EventsOf(thread).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(new[] { "small" }, transport.SentTo());
        Assert.Equal(0, thread.PendingSendBytes);
    }

    [Fact]
    public void ABurstAcrossManyPeersStaysWithinEachPeersLimit()
    {
        GatedTransport transport = new();
        using NetworkingThread thread = new(transport, maxQueuedMessages: 8);
        thread.Listen();
        Connection[] peers = Enumerable.Range(0, 4).Select(i => thread.Connect($"peer{i}")).ToArray();
        WorkerPump pump = new(thread);
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
        Assert.DoesNotContain(pump.EventsOf(thread), e => e.Type == TransportEventType.Disconnected);
    }

    [Fact]
    public void StalledGameThreadHasBoundedIncomingStorageAndHealthyPeersSurvive()
    {
        LocalNetwork network = new();
        using NetworkingThread host = new(new LocalTransport(network, "host"), maxQueuedBytes: 64);
        using NetworkingThread badClient = CreateThread(new LocalTransport(network, "bad"));
        using NetworkingThread healthyClient = CreateThread(new LocalTransport(network, "healthy"));
        host.Listen();
        Connection bad = badClient.Connect("host"), healthy = healthyClient.Connect("host");
        WorkerPump pump = new(host, badClient, healthyClient);
        pump.Until(() => bad.IsConnected && healthy.IsConnected && pump.EventsOf(host).Count(e => e.Type == TransportEventType.Connected) == 2);
        bad.Send(new byte[1024], Delivery.Reliable);
        healthy.Send(new byte[] { 42 }, Delivery.Reliable);
        Thread.Sleep(30);
        Assert.InRange(host.PendingReceiveBytes, 0, 64);
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
        using NetworkingThread transport = new(inner);
        int caller = Environment.CurrentManagedThreadId;
        transport.Listen();
        Assert.NotEqual(caller, inner.ListenThread);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.ReceiveThread) != 0, 1000));
        Assert.Equal(inner.ListenThread, inner.ReceiveThread);
        inner.Fail = true;
        Assert.True(SpinWait.SpinUntil(() => !transport.IsWorkerRunning, 1000));
        Assert.Throws<InvalidOperationException>(() => transport.TryReadEvent(out _));
        Assert.Throws<InvalidOperationException>(transport.Listen);
        transport.Shutdown();
        inner.Fail = false;
        transport.Listen();
        Assert.True(transport.IsWorkerRunning);
    }

    [Fact]
    public void QueuedConnectionSendsAndFlushesStayOnTheNetworkingThread()
    {
        ProbeTransport transport = new();
        using NetworkingThread thread = new(transport);
        thread.Listen();
        Connection connection = thread.Connect("probe");
        WorkerPump pump = new(thread);
        pump.Until(() => connection.IsConnected);
        Assert.Same(transport, connection.Transport);
        Assert.Equal(64, connection.GetBatchThreshold(Delivery.Reliable));
        connection.Send(new byte[] { 42 }, Delivery.Reliable, SendFlags.Immediate);
        connection.Flush();
        pump.Until(() => Volatile.Read(ref transport.SendThread) != 0);
        Assert.Equal(transport.ListenThread, transport.SendThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, transport.SendThread);
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

    private sealed class ProbeTransport : Transport
    {
        public int ListenThread, ReceiveThread, SendThread;
        public volatile bool Fail;
        public override bool IsListening => true;
        public override void Listen() => ListenThread = Environment.CurrentManagedThreadId;
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
            AssertWorkerThread();
            Volatile.Write(ref ReceiveThread, Environment.CurrentManagedThreadId);
            if (Fail) throw new InvalidOperationException("Injected worker failure.");
        }
        public override void Flush() => AssertWorkerThread();
        public override void Shutdown() { ListenThread = 0; ReceiveThread = 0; }

        private void AssertWorkerThread()
        {
            if (ListenThread != 0 && ListenThread != Environment.CurrentManagedThreadId)
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
