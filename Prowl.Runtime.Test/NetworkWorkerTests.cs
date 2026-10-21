using System;
using System.Linq;
using System.Threading;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;
using Prowl.Warp;
using Prowl.Runtime.Networking;
using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkWorkerTests
{
    private static Transport Wrap(Transport inner) => new NetworkWorker(inner);

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
        using NetworkWorker hostTransport = new(Stack()), clientTransport = new(Stack());
        using NetSession host = NetSession.Host(hostTransport);
        using NetSession client = NetSession.Join(clientTransport, $"127.0.0.1:{hostTransport.LocalPort}", "client");
        WorkerSessionProbe hostProbe = new(host), clientProbe = new(client);
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
        Assert.False(hostTransport.IsWorkerRunning);
        Assert.False(clientTransport.IsWorkerRunning);
    }

    [Fact]
    public void KeepalivesContinueWhileTheGameThreadIsStalled()
    {
        KcpTransportOptions Options() => new() { Port = 0, Timeout = TimeSpan.FromMilliseconds(300), PingInterval = TimeSpan.FromMilliseconds(20) };
        KcpTransport rawHost = new(Options());
        using Transport host = Wrap(rawHost);
        using Transport client = Wrap(new KcpTransport(Options()));
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
        using Transport hostTransport = Wrap(new LocalTransport(network, "host"));
        using Transport clientTransport = Wrap(new LocalTransport(network, "client"));
        using NetSession host = NetSession.Host(hostTransport);
        using NetSession client = NetSession.Join(clientTransport, "host", "client");
        WorkerSessionProbe hostProbe = new(host), clientProbe = new(client);
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
        using NetworkWorker host = new(new LocalTransport(network, "host"));
        using NetworkWorker client = new(new LocalTransport(network, "client"));
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
        using NetworkWorker host = new(new LocalTransport(network, "host"));
        using NetworkWorker client = new(new LocalTransport(network, "client"), maxQueuedBytes: 64);
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
        using NetworkWorker host = new(new LocalTransport(network, "host"), maxQueuedBytes: 64);
        using Transport badClient = Wrap(new LocalTransport(network, "bad"));
        using Transport healthyClient = Wrap(new LocalTransport(network, "healthy"));
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
        using NetworkWorker transport = new(inner);
        int caller = Environment.CurrentManagedThreadId;
        transport.Listen();
        Assert.NotEqual(caller, inner.ListenThread);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref inner.ReceiveThread) != 0, 1000));
        Assert.Equal(inner.ListenThread, inner.ReceiveThread);
        inner.Fail = true;
        Assert.True(SpinWait.SpinUntil(() => !transport.IsWorkerRunning, 1000));
        Assert.Throws<InvalidOperationException>(transport.Receive);
        Assert.Throws<InvalidOperationException>(transport.Listen);
        transport.Shutdown();
        inner.Fail = false;
        transport.Listen();
        Assert.True(transport.IsWorkerRunning);
    }

    private sealed class ProbeTransport : Transport
    {
        public int ListenThread, ReceiveThread;
        public volatile bool Fail;
        public override bool IsListening => true;
        public override void Listen() => ListenThread = Environment.CurrentManagedThreadId;
        public override Connection Connect(string address) => throw new NotSupportedException();
        public override void Receive()
        {
            Volatile.Write(ref ReceiveThread, Environment.CurrentManagedThreadId);
            if (Fail) throw new InvalidOperationException("Injected worker failure.");
        }
        public override void Flush() { }
        public override void Shutdown() { }
    }
}
