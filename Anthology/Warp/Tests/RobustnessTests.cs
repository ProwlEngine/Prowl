using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;

using Prowl.Warp.Transports.Kcp;

using Xunit;

namespace Prowl.Warp.Tests;

public class RobustnessTests
{
    [Fact]
    public void SeededReaderFuzzMatchesAnUnboundedIntegerOracle()
    {
        Random random = new(0x57415250);
        for (int iteration = 0; iteration < 100_000; iteration++)
        {
            byte[] data = new byte[random.Next(21)];
            random.NextBytes(data);
            BigInteger expected = 0;
            bool terminated = false;
            for (int i = 0; i < Math.Min(10, data.Length); i++)
            {
                expected |= new BigInteger(data[i] & 127) << (i * 7);
                if ((data[i] & 128) == 0)
                {
                    terminated = true;
                    break;
                }
            }

            try
            {
                ulong actual = new NetReader(data).ReadVarUInt64();
                Assert.True(terminated && expected <= ulong.MaxValue);
                Assert.Equal((ulong)expected, actual);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
            {
                Assert.True(!terminated || expected > ulong.MaxValue);
            }

            try
            {
                new NetReader(data).ReadString(256);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
            {
            }

            try
            {
                new NetReader(data).ReadBytesWithLength(256);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void SeededKcpFuzzKeepsReceiveWindowsBounded()
    {
        Random random = new(42042);
        Transports.Kcp.Protocol.Kcp channel = new(1, (_, _) => { });
        channel.SetWindowSize(128, 128);
        byte[] output = new byte[128 * 1400];
        for (int iteration = 0; iteration < 50_000; iteration++)
        {
            byte[] data = new byte[random.Next(1201)];
            random.NextBytes(data);
            if (data.Length >= 24 && iteration % 2 == 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
                data[4] = (byte)(81 + random.Next(4));
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)random.Next(512));
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), (uint)(data.Length - 24));
            }

            channel.Input(data, 0, data.Length);
            if (iteration % 64 == 0)
            {
                channel.Update((uint)iteration);
                while (channel.PeekSize() >= 0)
                {
                    channel.Receive(output, output.Length);
                }
            }

            Assert.InRange(channel.rcv_queue.Count, 0, 128);
            Assert.InRange(channel.rcv_buf.Count, 0, 128);
        }
    }

    [Fact]
    public void ThirtyTwoClientsRemainResponsiveWhenOneStopsReading()
    {
        using KcpTransport transport = new(new KcpTransportOptions { Port = 0, MaxPendingReliableBytes = 1024 * 1024 });
        using NetSession host = NetSession.Host(transport);
        SessionProbe hostProbe = new(host);
        SessionProbe[] clients = Enumerable.Range(0, 32).Select(i => new SessionProbe(NetSession.Join(
                new KcpTransport(new KcpTransportOptions { Port = 0 }), $"127.0.0.1:{transport.LocalPort}",
                $"client{i}")))
            .ToArray();
        try
        {
            SessionProbe[] everyone = new[] { hostProbe }.Concat(clients).ToArray();
            SessionProbe.PumpUntil(() => clients.All(c => c.Session.State == SessionState.Active), everyone);
            NetPeer stalled = host.Peers.Single(p => p.Name == "client31");
            for (int i = 0; i < 40; i++)
            {
                host.SendRaw(stalled, 20, new byte[32_000]);
            }

            host.Broadcast(new ChatMessage("still responsive"));
            SessionProbe[] healthy = new[] { hostProbe }.Concat(clients.Take(31)).ToArray();
            SessionProbe.PumpUntil(() => clients.Take(31).All(c => c.Chat.Count == 1) && host.Peers.Count == 32,
                healthy);
            Assert.Equal(SessionEndReason.ResourceLimit, hostProbe.PeersLeft.Single().Reason);
            Assert.All(clients.Take(31), c => Assert.Equal(SessionState.Active, c.Session.State));
        }
        finally
        {
            foreach (SessionProbe client in clients)
            {
                client.Session.Dispose();
            }
        }
    }

    [Fact]
    public void LargeAndSmallReliableMessagesSurviveLoss()
    {
        KcpTransportOptions Options() => new()
        {
            Port = 0,
            Simulation = new LinkSimulation
            {
                PacketLoss = 0.1,
                Latency = TimeSpan.FromMilliseconds(3),
                Jitter = TimeSpan.FromMilliseconds(3),
                Seed = 9182
            }
        };

        using KcpTransport host = new(Options());
        using KcpTransport client = new(Options());
        host.Listen();
        Connection connection = client.Connect($"127.0.0.1:{host.LocalPort}");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected && pump.EventsOf(host).Any(e => e.Type == TransportEventType.Connected),
            10_000, "lossy handshake");
        byte[] bulk = TestData.Pattern(50_000, 20);
        connection.Send(bulk, Delivery.Reliable);
        for (int i = 0; i < 20; i++)
        {
            connection.Send(BitConverter.GetBytes(i), Delivery.Reliable);
        }

        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 21, 10_000,
            "lossy channels");
        Assert.Equal(bulk,
            pump.EventsOf(host).Single(e =>
                e.Delivery == Delivery.Reliable && e.Type == TransportEventType.Data && e.Data.Length > 4).Data);
        Assert.Equal(Enumerable.Range(0, 20),
            pump.EventsOf(host)
                .Where(e => e.Delivery == Delivery.Reliable && e.Type == TransportEventType.Data && e.Data.Length == 4)
                .Select(e => BitConverter.ToInt32(e.Data)));
    }

    [Fact]
    public void RepeatedSessionsAndLargeTransfersReleaseAllState()
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("WARP_STRESS_ROUNDS"), out int configured)
            ? configured
            : 20;
        using KcpTransport transport = new(new KcpTransportOptions { Port = 0 });
        using NetSession host = NetSession.Host(transport);
        SessionProbe hostProbe = new(host);
        for (int round = 0; round < rounds; round++)
        {
            using KcpTransport clientTransport = new(new KcpTransportOptions { Port = 0 });
            using NetSession client = NetSession.Join(clientTransport, $"127.0.0.1:{transport.LocalPort}", "stress");
            hostProbe.Chat.Clear();
            SessionProbe clientProbe = new(client);
            SessionProbe.PumpUntil(() => client.State == SessionState.Active, hostProbe, clientProbe);
            byte[] bulk = TestData.Pattern(350_000, round);
            byte[]? received = null;
            client.OnRaw(20, (sender, ref reader, delivery) => received = reader.ReadBytes(reader.Remaining).ToArray());
            host.SendRaw(host.Peers.Single(p => !p.IsLocal), 20, bulk);
            client.SendToHost(new ChatMessage("alive"));
            SessionProbe.PumpUntil(() => received != null && hostProbe.Chat.Count == 1, hostProbe, clientProbe);
            Assert.Equal(bulk, received);
            Assert.Equal(0, host.PendingReassemblyBytes);
            Assert.Equal(0, client.PendingReassemblyBytes);
            client.Shutdown();
            SessionProbe.PumpUntil(() => host.Peers.Count == 1 && transport.Connections.Count == 0, hostProbe);
            Assert.Empty(transport.Connections);
        }
    }
}
