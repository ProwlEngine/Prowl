using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;
using Prowl.Warp.Transports.Encryption;
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
                if ((data[i] & 128) == 0) { terminated = true; break; }
            }
            try
            {
                ulong actual = new NetReader(data).ReadVarUInt64();
                Assert.True(terminated && expected <= ulong.MaxValue);
                Assert.Equal((ulong)expected, actual);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
            { Assert.True(!terminated || expected > ulong.MaxValue); }
            try { new NetReader(data).ReadString(256); }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException) { }
            try { new NetReader(data).ReadBytesWithLength(256); }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException) { }
        }
    }

    [Fact]
    public void SeededKcpFuzzKeepsReceiveWindowsBounded()
    {
        Random random = new(42042);
        Prowl.Warp.Transports.Kcp.Protocol.Kcp channel = new(1, (_, _) => { });
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
                while (channel.PeekSize() >= 0) channel.Receive(output, output.Length);
            }
            Assert.InRange(channel.rcv_queue.Count, 0, 128);
            Assert.InRange(channel.rcv_buf.Count, 0, 128);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedSessionsAndBulkTransfersReleaseAllState(bool encrypted)
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("WARP_STRESS_ROUNDS"), out int configured) ? configured : 20;
        using KcpTransport rawHost = new(new() { Port = 0 });
        using Transport transport = encrypted ? new EncryptedTransport(rawHost, new byte[32]) : rawHost;
        using NetSession host = NetSession.Host(transport);
        SessionProbe hostProbe = new(host);
        for (int round = 0; round < rounds; round++)
        {
            KcpTransport inner = new(new() { Port = 0 });
            using Transport clientTransport = encrypted ? new EncryptedTransport(inner, new byte[32]) : inner;
            using NetSession client = NetSession.Join(clientTransport, $"127.0.0.1:{rawHost.LocalPort}", "stress");
            hostProbe.Chat.Clear();
            SessionProbe clientProbe = new(client);
            SessionProbe.PumpUntil(() => client.State == SessionState.Active, hostProbe, clientProbe);
            byte[] bulk = TestData.Pattern(350_000, round);
            byte[]? received = null;
            client.OnRaw(20, (NetPeer sender, ref NetReader reader, Delivery delivery) => received = reader.ReadBytes(reader.Remaining).ToArray());
            host.SendRaw(host.Peers.Single(p => !p.IsLocal), 20, bulk, Delivery.Bulk);
            client.SendToHost(new ChatMessage("alive"));
            SessionProbe.PumpUntil(() => received != null && hostProbe.Chat.Count == 1, hostProbe, clientProbe);
            Assert.Equal(bulk, received);
            Assert.Equal(0, host.PendingReassemblyBytes);
            Assert.Equal(0, client.PendingReassemblyBytes);
            client.Shutdown();
            SessionProbe.PumpUntil(() => host.Peers.Count == 1 && transport.Connections.Count == 0, hostProbe);
            Assert.Empty(rawHost.Connections);
        }
    }
}
