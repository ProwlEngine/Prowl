using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    /// <summary>One client of the bandwidth run: the player it drives, and what it knows of what the host acknowledged.</summary>
    private sealed class BandwidthClient
    {
        public required FakePeer Peer;
        public required GameObject Player;
        public float X, Z, Heading;

        // Each client runs its own clock, so no two stamp a transform alike.
        public double Clock;
        public readonly Dictionary<uint, (long X, long Y, long Z)> Sent = new();
        public (uint Tick, (long X, long Y, long Z) Grid)? Baseline;

        // Acknowledgements on their way, each with the run tick it arrives at: to the host, and from it.
        public readonly Queue<(int Due, uint Id)> ToHost = new(), FromHost = new();
    }

    /// <summary>
    ///     Transform bytes per tick with every client driving one player in a 500 m square, with and without
    ///     acknowledged baselines, at two round trip times. Runs only with PROWL_NET_BENCH set, as the other benchmark.
    /// </summary>
    [Fact]
    public void BenchmarkTransformBandwidth()
    {
        if (BenchOutput is not { } output)
        {
            return;
        }

        int peerCount = Math.Min(63, BenchSize("PROWL_NET_BENCH_PEERS", 50)), ticks = BenchSize("PROWL_NET_BENCH_TICKS", 600);
        List<string> lines = [$"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss}  transform bandwidth, {peerCount} clients each driving a player in 500 m x 500 m, " +
                              $"5 m/s, {ticks} ticks at 30 a second, payload bytes without transport headers"];
        foreach (bool baselines in new[] { true, false })
        {
            foreach (int roundTrip in new[] { 0, 3, 6 })
            {
                if (!baselines && roundTrip != 0)
                {
                    continue;
                }

                lines.Add(MeasureTransformBandwidth(peerCount, ticks, baselines, roundTrip));
            }
        }

        File.AppendAllLines(output, lines);
    }

    private string MeasureTransformBandwidth(int peerCount, int ticks, bool baselines, int roundTrip)
    {
        const float area = 500f, speed = 5f / 30f;
        Random random = new(12345);
        SceneInstance scene = CreateScene();
        List<GameObject> players = [];
        for (int i = 0; i < peerCount; i++)
        {
            GameObject go = CreateGameObject($"Player {i}");
            go.Network.Networked = true;
            go.AddComponent<NetworkTransform>();
            scene.Add(go);
            players.Add(go);
        }

        MakeCurrent(scene);
        LocalNetwork network = new();
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { MaxClientMessagesPerSecond = 0 });
        SnapshotSystem.AcknowledgedBaselines = baselines;
        Networking.Networking.Host(new LocalTransport(network, "host"));
        List<BandwidthClient> clients = [];
        try
        {
            for (int i = 0; i < peerCount; i++)
            {
                FakePeer peer = FakePeer.Join(network, $"peer{i}");
                peer.AutoAck = false;
                clients.Add(new BandwidthClient
                {
                    Peer = peer, Player = players[i], X = random.NextSingle() * area, Z = random.NextSingle() * area,
                    Heading = random.NextSingle() * MathF.Tau, Clock = random.NextDouble() * 100,
                });
            }

            for (int i = 0; i < 200000 && !clients.All(c => c.Peer.IsReady); i++)
            {
                Networking.Networking.Receive();
                Networking.Networking.Flush();
                foreach (BandwidthClient client in clients) client.Peer.Session.Update();
            }

            Assert.True(clients.All(c => c.Peer.IsReady), "The peers never finished joining.");
            NetworkSystem system = Networking.Networking.System!;
            foreach (BandwidthClient client in clients)
                client.Player.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == client.Peer.Session.LocalPeer!.Name));

            const int warmup = 60;
            for (int tick = 0; tick < warmup + ticks; tick++)
            {
                if (tick == warmup)
                {
                    NetworkStatistics.Reset();
                }

                uint snapshotTick = 1000 + (uint)tick;
                foreach (BandwidthClient client in clients)
                {
                    // Wander: a little turn each tick, walking on, bouncing off the edges of the square.
                    client.Heading += (random.NextSingle() - 0.5f) * 0.3f;
                    client.X += MathF.Cos(client.Heading) * speed;
                    client.Z += MathF.Sin(client.Heading) * speed;
                    if (client.X < 0 || client.X > area || client.Z < 0 || client.Z > area)
                    {
                        client.Heading += MathF.PI;
                        client.X = Math.Clamp(client.X, 0, area);
                        client.Z = Math.Clamp(client.Z, 0, area);
                    }

                    SendPlayer(system, client, snapshotTick, baselines);
                }

                Networking.Networking.Receive();
                system.Snapshots.Send();
                Networking.Networking.Flush();

                foreach (BandwidthClient client in clients)
                {
                    client.Peer.Session.Update();
                    foreach ((ushort id, byte[] data) in client.Peer.WireReceived)
                    {
                        NetReader reader = new(data);
                        if (id == FakePeer.Snapshot)
                        {
                            uint snapshot = reader.ReadVarUInt32();
                            if (snapshot != 0) client.ToHost.Enqueue((tick + roundTrip / 2, snapshot));
                        }
                        else if (id == FakePeer.SnapshotAck)
                        {
                            uint count = reader.ReadVarUInt32();
                            for (uint i = 0; i < count; i++) client.FromHost.Enqueue((tick + roundTrip - roundTrip / 2, reader.ReadVarUInt32()));
                        }
                    }

                    client.Peer.WireReceived.Clear();
                    client.Peer.Received.Clear();
                    client.Peer.Deliveries.Clear();
                    client.Peer.Channels.Clear();

                    // A tick's acknowledgements go in one message, as the engine sends them.
                    using NetWriter ack = new();
                    List<uint> ids = [];
                    while (client.ToHost.Count > 0 && client.ToHost.Peek().Due <= tick) ids.Add(client.ToHost.Dequeue().Id);
                    if (ids.Count > 0)
                    {
                        ack.WriteVarUInt32((uint)ids.Count);
                        foreach (uint id in ids) ack.WriteVarUInt32(id);
                        ack.WriteVarUInt32(0);
                        client.Peer.Session.SendRaw(client.Peer.Session.HostPeer!, FakePeer.SnapshotAck, ack.WrittenSpan, Delivery.Unreliable);
                        client.Peer.Session.Flush();
                    }

                    while (client.FromHost.Count > 0 && client.FromHost.Peek().Due <= tick)
                    {
                        uint acked = client.FromHost.Dequeue().Id;
                        if (client.Sent.TryGetValue(acked, out (long X, long Y, long Z) grid) &&
                            (client.Baseline is not { } held || (int)(acked - held.Tick) > 0))
                        {
                            client.Baseline = (acked, grid);
                        }
                    }
                }
            }

            (string Kind, NetworkTraffic Received, NetworkTraffic Sent)[] kinds = [.. NetworkStatistics.ByKind()];
            NetworkTraffic Of(string kind, bool sent) => kinds.Where(k => k.Kind == kind).Select(k => sent ? k.Sent : k.Received)
                .FirstOrDefault();
            NetworkTraffic down = Of("Snapshot", true), downAcks = Of("SnapshotAck", true);
            NetworkTraffic up = Of("Snapshot", false), upAcks = Of("SnapshotAck", false);
            double perTick = 1.0 / ticks, perClientSecond = 30.0 / ticks / peerCount / 1000.0;
            long hostOut = down.Bytes + downAcks.Bytes, hostIn = up.Bytes + upAcks.Bytes;
            return $"{(baselines ? $"acknowledged, round trip {roundTrip} ticks" : "no acknowledgements         "),-34}" +
                   $" host out {hostOut * perTick,8:F0} B/tick ({down.Bytes * perTick:F0} transforms + {downAcks.Bytes * perTick:F0} acks, {(down.Messages + downAcks.Messages) * perTick:F0} msgs)" +
                   $" | host in {hostIn * perTick,6:F0} B/tick ({up.Bytes * perTick:F0} transforms + {upAcks.Bytes * perTick:F0} acks, {(up.Messages + upAcks.Messages) * perTick:F0} msgs)" +
                   $" | per client {hostOut * perClientSecond:F2} kB/s down, {hostIn * perClientSecond:F2} kB/s up";
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
            SnapshotSystem.AcknowledgedBaselines = true;
            foreach (BandwidthClient client in clients) client.Peer.Dispose();
        }
    }

    /// <summary>Sends a client's player as a client would: its position on the grid, a step from what the host acknowledged when it can be.</summary>
    private static void SendPlayer(NetworkSystem system, BandwidthClient client, uint tick, bool baselines)
    {
        (long X, long Y, long Z) grid = ((long)MathF.Round(client.X * 100), 100, (long)MathF.Round(client.Z * 100));
        (long X, long Y, long Z) step = grid;
        uint age = 0;
        if (baselines && client.Baseline is { } baseline && tick - baseline.Tick is > 0 and < 64)
        {
            age = tick - baseline.Tick;
            step = (grid.X - baseline.Grid.X, grid.Y - baseline.Grid.Y, grid.Z - baseline.Grid.Z);
        }

        if (baselines)
        {
            client.Sent[tick] = grid;
            client.Sent.Remove(tick - 96);
        }

        Quaternion facing = Quaternion.AxisAngle(Float3.UnitY, client.Heading);
        using NetWriter w = new();
        w.WriteVarUInt32(baselines ? tick : 0); w.WriteVarUInt32((uint)system.SceneGeneration); w.WriteVarUInt32(tick);
        w.WriteDouble(client.Clock + tick / 30.0); w.WriteVarUInt32(1);
        w.WriteVarUInt32(system.NetIdOf(client.Player)); w.WriteVarUInt32(system.GenerationOf(client.Player));
        w.WriteByte(PositionChannel | RotationChannel);
        w.WriteVarUInt32(age);
        w.WriteVarInt64(step.X); w.WriteVarInt64(step.Y); w.WriteVarInt64(step.Z);
        NetSerializers.WriteCompactQuaternion(w, facing);
        client.Peer.Session.SendRaw(client.Peer.Session.HostPeer!, FakePeer.Snapshot, w.WrittenSpan, Delivery.Unreliable);
        client.Peer.Session.Flush();
    }
}
