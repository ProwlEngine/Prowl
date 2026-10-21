using System.Diagnostics;
using Prowl.Runtime.Networking;
using Prowl.Warp;
using Prowl.Warp.Transports.Batching;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Encryption;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkingContextTests
{
    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void ReconnectsAndLargeTransfersSurviveSimulatedWan(NetworkExecution execution)
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("PROWL_NETWORK_STRESS_ROUNDS"), out int configured) ? configured : 3;
        Transport Stack(int seed) => new BatchingTransport(new EncryptedTransport(new KcpTransport(new()
        {
            Port = 0,
            Simulation = new() { PacketLoss = 0.03, Latency = TimeSpan.FromMilliseconds(15),
                Jitter = TimeSpan.FromMilliseconds(10), Seed = seed }
        }), new byte[32]));
        NetworkConfiguration configuration = new() { Execution = execution };
        using NetworkingContext host = new(configuration);
        NetSession server = host.Host(Stack(42));
        List<int> ordered = new();
        server.OnRaw(42, (NetPeer peer, ref NetReader reader, Delivery delivery) => ordered.Add(reader.ReadInt32()));
        byte[] payload = new byte[350_000];
        new Random(42).NextBytes(payload);
        for (int round = 0; round < rounds; round++)
        {
            using NetworkingContext client = new(configuration);
            NetSession session = client.Join(Stack(round + 100), $"127.0.0.1:{host.LocalPort}", "client");
            void Pump() { host.Update(); client.Update(); }
            Until(() => session.State == SessionState.Active && server.Peers.Count == 2, Pump);
            ordered.Clear();
            bool received = false;
            session.OnRaw(43, (NetPeer peer, ref NetReader reader, Delivery delivery) =>
                received = reader.ReadBytes(reader.Remaining).SequenceEqual(payload));
            server.SendRaw(server.Peers.Single(peer => !peer.IsLocal), 43, payload);
            for (int i = 0; i < 128; i++) session.SendRaw(session.HostPeer!, 42, BitConverter.GetBytes(i));
            Until(() => received && ordered.Count == 128, Pump);
            Assert.Equal(Enumerable.Range(0, 128), ordered);
            Assert.Equal(0, server.PendingReassemblyBytes);
            Assert.Equal(0, session.PendingReassemblyBytes);
            client.Shutdown();
            Until(() => server.Peers.Count == 1, Pump);
        }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void LobbyAndGameSessionsRunIndependently(NetworkExecution execution)
    {
        NetworkConfiguration configuration = new() { Execution = execution };
        using NetworkingContext lobbyHost = new(configuration), lobbyClient = new(configuration),
            gameHost = new(configuration), gameClient = new(configuration);
        LocalNetwork network = new();
        NetSession lobby = lobbyHost.Host(new LocalTransport(network, "lobby"));
        NetSession lobbyPeer = lobbyClient.Join(new LocalTransport(network, "lobby-client"), "lobby", "client");
        NetSession game = gameHost.Host(new BatchingTransport(new KcpTransport(new() { Port = 0 })));
        NetSession gamePeer = gameClient.Join(new BatchingTransport(new KcpTransport()), $"127.0.0.1:{gameHost.LocalPort}", "client");
        void Pump() { lobbyHost.Update(); lobbyClient.Update(); gameHost.Update(); gameClient.Update(); }
        Until(() => lobbyPeer.State == SessionState.Active && gamePeer.State == SessionState.Active, Pump);
        bool received = false;
        game.OnRaw(42, (NetPeer peer, ref NetReader reader, Delivery delivery) => received = reader.ReadByte() == 7);
        lobbyHost.Shutdown();
        Until(() => lobbyPeer.State == SessionState.Ended, Pump);
        Assert.Null(lobbyHost.Session);
        Assert.Equal(SessionState.Active, gamePeer.State);
        gamePeer.SendRaw(gamePeer.HostPeer!, 42, new byte[] { 7 });
        Until(() => received, Pump);
        Assert.Null(Networking.Networking.Session);
        Assert.Equal(SessionState.Ended, lobby.State);
        Assert.Equal(SessionState.Active, game.State);
    }

    [Fact]
    public void ContextCanRestartAndRejectsConcurrentSessions()
    {
        using NetworkingContext context = new();
        LocalNetwork network = new();
        context.Host(new LocalTransport(network, "host"));
        using Transport extra = new LocalTransport(network, "extra");
        Assert.Throws<InvalidOperationException>(() => context.Host(extra));
        context.Shutdown();
        context.Host(new LocalTransport(network, "host"));
        Assert.Equal(SessionState.Active, context.Session!.State);
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.Update());
    }

    [Fact]
    public void EndHandlerCanStartTheNextSession()
    {
        using NetworkingContext context = new();
        LocalNetwork network = new();
        NetSession first = context.Host(new LocalTransport(network, "first"));
        first.Ended += (_, _) => context.Host(new LocalTransport(network, "next"));
        context.Shutdown();
        Assert.NotNull(context.Session);
        Assert.Equal(SessionState.Active, context.Session!.State);
        context.Update();
    }

    [Fact]
    public void ContextRejectsPumpingFromAnotherThread()
    {
        using NetworkingContext context = new();
        Exception? failure = null;
        Thread thread = new(() => failure = Record.Exception(context.Update));
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
    }

    private static void Until(Func<bool> condition, Action pump)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5)) { pump(); Thread.Sleep(1); }
        Assert.True(condition());
    }
}
