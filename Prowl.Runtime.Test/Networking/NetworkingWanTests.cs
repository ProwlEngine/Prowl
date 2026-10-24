using System.Diagnostics;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;
using Xunit;

namespace Prowl.Runtime.Test;

public sealed class NetworkingWanTests : RuntimeTestBase
{
    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void ReconnectsAndLargeTransfersSurviveSimulatedWan(NetworkExecution execution)
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("PROWL_NETWORK_STRESS_ROUNDS"), out int configured) ? configured : 3;
        Transport Stack(int seed) => new KcpTransport(new()
        {
            Port = 0,
            Simulation = new() { PacketLoss = 0.03, Latency = TimeSpan.FromMilliseconds(15),
                Jitter = TimeSpan.FromMilliseconds(10), Seed = seed }
        });
        NetworkConfiguration configuration = new() { Execution = execution };
        NetworkConfiguration previous = Networking.Networking.Configuration;
        try
        {
            Networking.Networking.Configure(configuration);
            Scene scene = CreateScene();
            Scene.Load(scene); Scene.ProcessPendingLoad();
            NetSession server = Networking.Networking.Host(Stack(42));
            List<int> ordered = new();
            server.OnRaw(42, (NetPeer peer, ref NetReader reader, Delivery delivery) => ordered.Add(reader.ReadInt32()));
            byte[] payload = new byte[350_000];
            new Random(42).NextBytes(payload);
            for (int round = 0; round < rounds; round++)
            {
                using Transport transport = Stack(round + 100);
                using NetSession session = NetSession.Join(transport, $"127.0.0.1:{Networking.Networking.LocalPort}", "client");
                void Pump() { Networking.Networking.Receive(); session.Update(); Networking.Networking.Flush(); }
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
                session.Shutdown();
                Until(() => server.Peers.Count == 1, Pump);
            }
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
        }
    }

    private static void Until(Func<bool> condition, Action pump)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5)) { pump(); Thread.Sleep(1); }
        Assert.True(condition());
    }
}
