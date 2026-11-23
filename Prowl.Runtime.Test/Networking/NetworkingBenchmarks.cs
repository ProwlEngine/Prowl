using System.Diagnostics;

using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
///     Host side cost of replication with many peers and objects. They only run with PROWL_NET_BENCH set to a file to
///     append the results to, and take their sizes from PROWL_NET_BENCH_PEERS and PROWL_NET_BENCH_OBJECTS.
/// </summary>
public partial class NetworkingTests
{
    private static string? BenchOutput => Environment.GetEnvironmentVariable("PROWL_NET_BENCH");

    private static int BenchSize(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;

    private sealed class BenchTimer
    {
        private readonly Stopwatch _watch = new();
        private long _allocated, _started;
        public int Samples;

        public void Start()
        {
            _started = GC.GetAllocatedBytesForCurrentThread();
            _watch.Start();
        }

        public void Stop()
        {
            _watch.Stop();
            _allocated += GC.GetAllocatedBytesForCurrentThread() - _started;
            Samples++;
        }

        public string Report(string name) =>
            $"{name,-34} {_watch.Elapsed.TotalMilliseconds / Math.Max(1, Samples),9:F3} ms  {_allocated / Math.Max(1, Samples),10:N0} B";
    }

    private static void DrainPeers(List<FakePeer> peers)
    {
        foreach (FakePeer peer in peers)
        {
            peer.Session.Update();
            peer.Received.Clear();
            peer.WireReceived.Clear();
            peer.Deliveries.Clear();
            peer.Channels.Clear();
        }
    }

    [Fact]
    public void BenchmarkHostReplication()
    {
        if (BenchOutput is not { } output)
        {
            return;
        }

        // The local transport takes 64 connections, so 63 clients is the most a run here can have.
        int peerCount = Math.Min(63, BenchSize("PROWL_NET_BENCH_PEERS", 32)), objectCount = BenchSize("PROWL_NET_BENCH_OBJECTS", 1000);
        int ticks = BenchSize("PROWL_NET_BENCH_TICKS", 120);
        Scene scene = CreateScene();
        List<GameObject> objects = [];
        List<RpcStateTestComponent> components = [];
        for (int i = 0; i < objectCount; i++)
        {
            GameObject go = CreateGameObject($"Bench {i}");
            go.Network.Networked = true;
            go.AddComponent<NetworkTransform>();
            components.Add(go.AddComponent<RpcStateTestComponent>());
            go.Transform.LocalPosition = new Float3(i % 100, 0, i / 100);
            scene.Add(go);
            objects.Add(go);
        }

        MakeCurrent(scene);
        LocalNetwork network = new();
        NetworkConfiguration previous = Networking.Networking.Configuration;
        // Ticks run back to back here, far faster than real time, so the per client message rate limit is off.
        Networking.Networking.Configure(new() { MaxClientMessagesPerSecond = 0 });
        Networking.Networking.Host(new LocalTransport(network, "host"));
        List<FakePeer> peers = [];
        List<string> lines = [$"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {peerCount} peers, {objectCount} objects, {ticks} ticks, per tick"];
        try
        {
            Stopwatch join = Stopwatch.StartNew();
            for (int i = 0; i < peerCount; i++) peers.Add(FakePeer.Join(network, $"peer{i}"));
            for (int i = 0; i < 200000 && !peers.All(p => p.IsReady); i++)
            {
                Networking.Networking.Receive();
                Networking.Networking.Flush();
                DrainPeers(peers);
            }

            Assert.True(peers.All(p => p.IsReady), "The peers never finished joining.");
            lines.Add($"{"join of all peers",-34} {join.Elapsed.TotalMilliseconds,9:F0} ms");

            NetworkSystem system = Networking.Networking.System!;
            void Run(string name, Action<int> change, Action? extra = null)
            {
                BenchTimer state = new(), transforms = new(), receive = new(), calls = new();
                for (int tick = 0; tick < ticks; tick++)
                {
                    calls.Start();
                    change(tick);
                    calls.Stop();
                    state.Start();
                    system.Sync.SendSyncChanges();
                    state.Stop();
                    transforms.Start();
                    system.Transforms.Send();
                    transforms.Stop();
                    Networking.Networking.Flush();
                    DrainPeers(peers);
                    extra?.Invoke();
                    receive.Start();
                    Networking.Networking.Receive();
                    receive.Stop();
                }

                lines.Add(calls.Report($"{name}: game changes"));
                lines.Add(state.Report($"{name}: state send"));
                lines.Add(transforms.Report($"{name}: transform send"));
                lines.Add(receive.Report($"{name}: receive"));
            }

            Run("idle", _ => { });
            Run("all move", tick =>
            {
                for (int i = 0; i < objects.Count; i++)
                    objects[i].Transform.LocalPosition = new Float3(i % 100 + tick * 0.1f, tick * 0.05f, i / 100);
            });
            Run("all change a value", tick =>
            {
                for (int i = 0; i < components.Count; i++)
                    components[i].HostValue = tick + i + 1;
            });
            Run("a tenth call an rpc", tick =>
            {
                for (int i = tick % 10; i < components.Count; i += 10)
                    components[i].Observe();
            });

            // Every peer drives its share of the objects, so the host takes their transforms in and passes them on.
            for (int i = 0; i < objects.Count; i++)
                objects[i].Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == $"peer{i % peerCount}"));
            for (int i = 0; i < 50; i++)
            {
                Networking.Networking.Receive();
                Networking.Networking.Flush();
                DrainPeers(peers);
            }

            uint sent = 1000;
            Run("clients move all", _ => { }, () =>
            {
                sent++;
                for (int p = 0; p < peers.Count; p++)
                {
                    int owner = p;
                    uint tick = sent;
                    // Moving positions, whole and on the grid, as a client sends an object it is driving.
                    peers[p].Send(FakePeer.Transform, w =>
                    {
                        int count = (objects.Count - owner + peerCount - 1) / peerCount;
                        w.WriteVarUInt32(0); w.WriteVarUInt32((uint)system.SceneGeneration); w.WriteVarUInt32(tick);
                        w.WriteDouble(tick / 30.0); w.WriteVarUInt32((uint)count);
                        for (int i = owner; i < objects.Count; i += peerCount)
                        {
                            w.WriteVarUInt32(system.NetIdOf(objects[i])); w.WriteVarUInt32(system.GenerationOf(objects[i]));
                            w.WriteByte(PositionChannel);
                            w.WriteVarUInt32(0);
                            w.WriteVarInt64((i % 100) * 100 + tick * 10); w.WriteVarInt64(100); w.WriteVarInt64(i / 100 * 100);
                        }
                    });
                }
            });
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
            foreach (FakePeer peer in peers) peer.Dispose();
        }

        File.AppendAllLines(output, lines);
    }
}
