using System;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchedUdpSessionsReassembleLargeMessagesAndPreserveShutdownReasons(bool encrypted)
    {
        Transport Stack()
        {
            Transport transport = new KcpTransport(new() { Port = 0 });
            if (encrypted) transport = new Prowl.Warp.Transports.Encryption.EncryptedTransport(transport, new byte[32]);
            return new Prowl.Warp.Transports.Batching.BatchingTransport(transport);
        }
        using NetworkingThread hostThread = new(Stack()), clientThread = new(Stack());
        using NetSession host = CreateHostSession(hostThread);
        using NetSession client = CreateClientSession(clientThread, $"127.0.0.1:{hostThread.LocalPort}", "client");
        WorkerSessionProbe hostProbe = new(host, hostThread), clientProbe = new(client, clientThread);
        WorkerSessionProbe.PumpUntil(() => client.State == SessionState.Active, hostProbe, clientProbe);
        byte[] payload = new byte[350_000];
        new Random(42).NextBytes(payload);
        byte[]? received = null;
        client.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery) => received = reader.ReadBytes(reader.Remaining).ToArray());
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
        host.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery) =>
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
        connection.Send(new byte[] { 42 }, Delivery.Reliable, SendFlags.Immediate);
        connection.Flush();
        pump.Until(() => Volatile.Read(ref transport.SendThread) != 0);
        Assert.Equal(transport.ListenThread, transport.SendThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, transport.SendThread);
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
            public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
            {
                owner.AssertWorkerThread();
                Volatile.Write(ref owner.SendThread, Environment.CurrentManagedThreadId);
            }
            public override void Disconnect() { }
        }
    }
}
