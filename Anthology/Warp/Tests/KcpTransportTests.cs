// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Warp.Transports.Kcp;

using Xunit;

namespace Prowl.Warp.Tests;

/// <summary>
///     KCP-only behaviour, over real UDP sockets on the loopback interface. The shared transport behaviour
///     is in <see cref="KcpTransportContractTests" />.
/// </summary>
public class KcpTransportTests
{
    private static KcpTransportOptions FastOptions(LinkSimulation? simulation = null) => new()
    {
        Port = 0,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        Timeout = TimeSpan.FromSeconds(3),
        HandshakeResendInterval = TimeSpan.FromMilliseconds(50),
        Simulation = simulation
    };

    private static (KcpTransport Host, KcpTransport Client, Pump Pump, Connection ToHost) Connect(
        KcpTransportOptions? hostOptions = null, KcpTransportOptions? clientOptions = null)
    {
        KcpTransport host = new(hostOptions ?? FastOptions());
        host.Listen();
        KcpTransport client = new(clientOptions ?? FastOptions());

        Pump pump = new(host, client);
        Connection toHost = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => toHost.IsConnected && host.Connections.Count == 1, what: "connect");
        return (host, client, pump, toHost);
    }

    [Fact]
    public void ReceiveWorkIsBoundedPerCall()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxDatagramsPerReceive = 4;
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(options);
        using (host)
        using (client)
        {
            for (int i = 0; i < 20; i++)
            {
                toHost.Send(new byte[] { 1 }, Delivery.Unreliable);
            }

            Thread.Sleep(20);
            host.Receive();
            int count = 0;
            while (host.Poll(out TransportEvent ev))
            {
                if (ev.Type == TransportEventType.Data)
                {
                    count++;
                }
            }

            Assert.InRange(count, 1, 4);
            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 20 - count,
                what: "remaining budgeted packets");
        }
    }

    [Fact]
    public void SlowReceiverIsDisconnectedBeforeReliableQueueGrows()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxPendingReliableBytes = 4096;
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(clientOptions: options);
        using (host)
        using (client)
        {
            // No receiver pumps or acknowledges these sends.
            for (int i = 0; i < 100 && toHost.IsConnected; i++)
            {
                toHost.Send(new byte[1000], Delivery.Reliable);
            }

            Assert.Equal(ConnectionState.Disconnected, toHost.State);
        }
    }

    [Fact]
    public void UnpolledDataIsBoundedPerConnection()
    {
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect();
        using (host)
        using (client)
        {
            host.MaxQueuedDataEventsPerConnection = 4;
            for (int i = 0; i < 20; i++)
            {
                toHost.Send(new byte[] { 1 }, Delivery.Unreliable);
            }

            Thread.Sleep(20);
            host.Receive();
            Assert.Empty(host.Connections);
            int count = 0;
            while (host.Poll(out TransportEvent ev))
            {
                if (ev.Type == TransportEventType.Data)
                {
                    count++;
                }
            }

            Assert.InRange(count, 0, 4);
        }
    }

    [Fact]
    public void LargeReliableMessagesPreserveOrdering()
    {
        (KcpTransport host, KcpTransport client, Pump pump, Connection connection) = Connect();
        using (host)
        using (client)
        {
            byte[] bulk = TestData.Pattern(100_000, 42);
            connection.Send(bulk, Delivery.Reliable);
            connection.Send(new byte[] { 7 }, Delivery.Reliable);
            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 2,
                what: "bulk completion");
            Received received = pump.EventsOf(host).Single(e => e.Type == TransportEventType.Data && e.Data.Length > 1);
            Assert.Equal(Delivery.Reliable, received.Delivery);
            Assert.Equal(bulk, received.Data);
            Assert.Equal(bulk, pump.EventsOf(host).First(e => e.Type == TransportEventType.Data).Data);
            Assert.Equal(new byte[] { 7 }, pump.EventsOf(host).Last(e => e.Type == TransportEventType.Data).Data);
        }
    }

    [Fact]
    public void LargeReliableMessagesAreReassembled()
    {
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect();
        using (host)
        using (client)
        {
            int max = toHost.GetMaxMessageSize(Delivery.Reliable);
            byte[] large = TestData.Pattern(max, 1);
            byte[] empty = Array.Empty<byte>();

            toHost.Send(large, Delivery.Reliable);
            toHost.Send(empty, Delivery.Reliable);

            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 2,
                what: "large message");

            Received[] data = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).ToArray();
            Assert.Equal(large, data[0].Data);
            Assert.Empty(data[1].Data);
            Assert.Throws<ArgumentException>(() => toHost.Send(new byte[max + 1], Delivery.Reliable));
        }
    }

    [Fact]
    public void UnreliableMessagesAreLimitedToOnePacket()
    {
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect();
        using (host)
        using (client)
        {
            int max = toHost.GetMaxMessageSize(Delivery.Unreliable);
            Assert.Equal(1200 - KcpPacket.SessionHeaderSize, max);
            toHost.Send(new byte[max], Delivery.Unreliable);
            Assert.Throws<ArgumentException>(() => toHost.Send(new byte[max + 1], Delivery.Unreliable));
        }
    }

    [Fact]
    public void ReliableSurvivesLossLatencyAndJitter()
    {
        LinkSimulation bad = new()
        {
            PacketLoss = 0.2,
            Latency = TimeSpan.FromMilliseconds(20),
            Jitter = TimeSpan.FromMilliseconds(20),
            Seed = 1234
        };
        KcpTransportOptions options = FastOptions(bad);
        options.Timeout = TimeSpan.FromSeconds(10);
        options.ConnectTimeout = TimeSpan.FromSeconds(10);

        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(options, FastOptions(bad));
        using (host)
        using (client)
        {
            const int count = 200;
            for (int i = 0; i < count; i++)
            {
                toHost.Send(BitConverter.GetBytes(i), Delivery.Reliable);
            }

            toHost.Send(TestData.Pattern(20_000, 2), Delivery.Reliable);

            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == count + 1, 20_000,
                "messages over lossy link");

            Received[] data = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).ToArray();
            Assert.Equal(Enumerable.Range(0, count), data.Take(count).Select(e => BitConverter.ToInt32(e.Data)));
            Assert.Equal(TestData.Pattern(20_000, 2), data[count].Data);
        }
    }

    [Fact]
    public void SilentPeerTimesOut()
    {
        KcpTransportOptions clientOptions = FastOptions();
        clientOptions.Timeout = TimeSpan.FromMilliseconds(500);
        (KcpTransport host, KcpTransport client, Pump _, Connection toHost) = Connect(clientOptions: clientOptions);
        using (host)
        using (client)
        {
            // Stop pumping the host, as if it froze or lost its network.
            Pump clientOnly = new(client);
            clientOnly.Until(() => toHost.State == ConnectionState.Disconnected, what: "timeout");
            Assert.Equal(DisconnectReason.Timeout, clientOnly.EventsOf(client).Single().Reason);
        }
    }

    [Fact]
    public void ConnectingToNobodyTimesOut()
    {
        // Bind a host only to find a free port, then close it again.
        int port;
        using (KcpTransport probe = new(FastOptions()))
        {
            probe.Listen();
            port = probe.LocalPort;
        }

        KcpTransportOptions options = FastOptions();
        options.ConnectTimeout = TimeSpan.FromMilliseconds(400);
        using KcpTransport client = new(options);
        Pump pump = new(client);

        Connection connection = client.Connect($"127.0.0.1:{port}");
        pump.Until(() => connection.State == ConnectionState.Disconnected, what: "connect timeout");
        Assert.Equal(DisconnectReason.Timeout, pump.EventsOf(client).Single().Reason);
    }

    [Fact]
    public void MismatchedProtocolIdNeverConnects()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();

        KcpTransportOptions options = FastOptions();
        options.ProtocolId = 0xDEADBEEF;
        options.ConnectTimeout = TimeSpan.FromMilliseconds(400);
        using KcpTransport client = new(options);
        Pump pump = new(host, client);

        Connection connection = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => connection.State == ConnectionState.Disconnected, what: "connect timeout");

        Assert.Equal(DisconnectReason.Timeout, pump.EventsOf(client).Single().Reason);
        Assert.Empty(pump.EventsOf(host));
    }

    [Fact]
    public void FullHostRejects()
    {
        KcpTransportOptions hostOptions = FastOptions();
        hostOptions.MaxConnections = 1;
        (KcpTransport host, KcpTransport first, Pump pump, Connection _) = Connect(hostOptions);
        using (host)
        using (first)
        using (KcpTransport second = new(FastOptions()))
        {
            Pump both = new(host, second);
            Connection rejected = second.Connect($"127.0.0.1:{host.LocalPort}");
            both.Until(() => rejected.State == ConnectionState.Disconnected, what: "rejection");

            Assert.Equal(DisconnectReason.ServerFull, both.EventsOf(second).Single().Reason);
            Assert.Single(host.Connections);
        }
    }

    [Fact]
    public void MeasuresRoundTripTime()
    {
        KcpTransportOptions options = FastOptions(new LinkSimulation { Latency = TimeSpan.FromMilliseconds(25) });
        options.PingInterval = TimeSpan.FromMilliseconds(50);
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(options, options);
        using (host)
        using (client)
        {
            pump.For(600);
            // 25 ms added each way.
            Assert.InRange(toHost.RoundTripTime.TotalMilliseconds, 40, 200);
            Assert.True(toHost.Stats.PacketsSent > 0 && toHost.Stats.PacketsReceived > 0);
        }
    }

    [Fact]
    public async Task ResolvesHostNamesAndIpv6Literals()
    {
        Assert.NotNull(await KcpTransport.TryResolveAsync("localhost:7777", 0, AddressFamily.InterNetworkV6, default));
        Assert.NotNull(await KcpTransport.TryResolveAsync("[::1]:7777", 0, AddressFamily.InterNetworkV6, default));
        Assert.NotNull(await KcpTransport.TryResolveAsync("127.0.0.1", 7777, AddressFamily.InterNetwork, default));
        Assert.Null(await KcpTransport.TryResolveAsync("[::1]:7777", 0, AddressFamily.InterNetwork, default));
        Assert.Null(await KcpTransport.TryResolveAsync("localhost:notaport", 0, AddressFamily.InterNetworkV6, default));
    }

    [Fact]
    public void FormatsAddressesFromEitherSocketFamily()
    {
        Assert.Equal("127.0.0.1:7777",
            KcpTransport.Format(new IPEndPoint(IPAddress.Loopback, 7777).Serialize()));
        Assert.Equal("127.0.0.1:7777",
            KcpTransport.Format(new IPEndPoint(IPAddress.Loopback.MapToIPv6(), 7777).Serialize()));
        Assert.Equal("[::1]:7777", KcpTransport.Format(new IPEndPoint(IPAddress.IPv6Loopback, 7777).Serialize()));
    }

    [Fact]
    public void ReplayedHandshakeDoesNotReplaceLiveConnection()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        raw.Connect(new IPEndPoint(IPAddress.Loopback, host.LocalPort));
        Pump pump = new(host);

        byte[] first = RawHandshake(raw, pump, 0x1111, out _);
        Connection firstConnection = Assert.Single(host.Connections);

        // The client restarts from the same address: the new attempt replaces the old session.
        RawHandshake(raw, pump, 0x2222, out _);
        Connection restarted = Assert.Single(host.Connections);
        Assert.NotSame(firstConnection, restarted);

        // A late copy of the first attempt's response must not tear the new session down.
        raw.Send(first);
        pump.For(200);
        Assert.Same(restarted, Assert.Single(host.Connections));
        Assert.True(restarted.IsConnected);
    }

    [Fact]
    public void AStalledReliableChannelDoesNotHoldUpAnother()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);
        RawHandshake(raw, pump, 0x4444, out uint session);

        // Channel 0 gets its second segment while the first is still missing, so it must wait.
        // Channel 1's first segment is complete and is delivered regardless.
        raw.Send(RawSegment(session, 0, 1, 10));
        raw.Send(RawSegment(session, 1, 0, 20));
        pump.For(200);

        Received delivered = Assert.Single(pump.EventsOf(host), e => e.Type == TransportEventType.Data);
        Assert.Equal((byte)1, delivered.Channel);
        Assert.Equal(20, delivered.Data[0]);

        raw.Send(RawSegment(session, 0, 0, 9));
        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 3,
            what: "channel 0 to catch up");
        Assert.Equal(new byte[] { 20, 9, 10 }, pump.EventsOf(host)
            .Where(e => e.Type == TransportEventType.Data)
            .Select(e => e.Data[0]));
    }

    [Theory]
    [InlineData(KcpPacket.WireVersion + 1, 4)]
    [InlineData(KcpPacket.WireVersion, 3)]
    public void HandshakesFromAnotherWireVersionOrChannelCountAreRejected(int version, int channels)
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);

        byte[] request = new byte[KcpPacket.HandshakeRequestSize];
        request[0] = (byte)KcpPacketType.ConnectRequest;
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(1), new KcpTransportOptions().ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(5), 0x5555);
        request[KcpPacket.HandshakeVersionOffset] = (byte)version;
        request[KcpPacket.HandshakeVersionOffset + 1] = (byte)channels;
        raw.Send(request);

        byte[] reject = Expect(raw, pump, KcpPacketType.Reject);
        Assert.Equal((byte)DisconnectReason.ProtocolError, reject[5]);
        Assert.Empty(host.Connections);
    }

    [Fact]
    public void ClientsWithAnotherChannelCountAreRefused()
    {
        KcpTransportOptions clientOptions = FastOptions();
        clientOptions.Channels = 2;
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using KcpTransport client = new(clientOptions);
        Pump pump = new(host, client);

        Connection toHost = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => toHost.State == ConnectionState.Disconnected, what: "refusal");
        Assert.Equal(DisconnectReason.ProtocolError,
            Assert.Single(pump.EventsOf(client), e => e.Type == TransportEventType.Disconnected).Reason);
    }

    private static Socket RawSocket(KcpTransport host)
    {
        Socket raw = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        raw.Connect(new IPEndPoint(IPAddress.Loopback, host.LocalPort));
        return raw;
    }

    /// <summary>A reliable packet holding one KCP data segment of a single byte. Channel 0 goes without a channel byte.</summary>
    private static byte[] RawSegment(uint session, byte channel, uint sequence, byte value)
    {
        int header = channel == 0 ? KcpPacket.SessionHeaderSize : KcpPacket.ChannelHeaderSize;
        byte[] packet = new byte[header + Transports.Kcp.Protocol.Kcp.OVERHEAD + 1];
        packet[0] = (byte)(channel == 0 ? KcpPacketType.Reliable : KcpPacketType.ReliableChannel);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(1), session);
        if (channel != 0) packet[KcpPacket.SessionHeaderSize] = channel;
        Span<byte> segment = packet.AsSpan(header);
        BinaryPrimitives.WriteUInt32LittleEndian(segment, session); // conversation
        segment[4] = Transports.Kcp.Protocol.Kcp.CMD_PUSH;
        segment[5] = 0; // not fragmented
        BinaryPrimitives.WriteUInt16LittleEndian(segment[6..], 128); // window
        BinaryPrimitives.WriteUInt32LittleEndian(segment[12..], sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(segment[20..], 1); // length
        segment[24] = value;
        return packet;
    }

    /// <summary>Runs the client half of the handshake by hand, returning the ChallengeResponse it sent.</summary>
    private static byte[] RawHandshake(Socket raw, Pump pump, uint nonce, out uint session)
    {
        byte[] request = new byte[KcpPacket.HandshakeRequestSize];
        request[0] = (byte)KcpPacketType.ConnectRequest;
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(1), new KcpTransportOptions().ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(5), nonce);
        request[KcpPacket.HandshakeVersionOffset] = KcpPacket.WireVersion;
        request[KcpPacket.HandshakeVersionOffset + 1] = (byte)new KcpTransportOptions().Channels;
        raw.Send(request);
        byte[] challenge = Expect(raw, pump, KcpPacketType.Challenge);

        byte[] response = (byte[])request.Clone();
        response[0] = (byte)KcpPacketType.ChallengeResponse;
        challenge.AsSpan(5, KcpPacket.CookieSize).CopyTo(response.AsSpan(9));
        raw.Send(response);
        byte[] accept = Expect(raw, pump, KcpPacketType.Accept);
        Assert.Equal(nonce, BinaryPrimitives.ReadUInt32LittleEndian(accept.AsSpan(1)));
        session = BinaryPrimitives.ReadUInt32LittleEndian(accept.AsSpan(5));
        return response;
    }

    private static byte[] Expect(Socket raw, Pump pump, KcpPacketType type)
    {
        byte[] buffer = new byte[KcpPacket.MaxDatagramSize];
        int length = 0;
        pump.Until(() => raw.Available > 0 && (length = raw.Receive(buffer)) > 0 && buffer[0] == (byte)type,
            what: type.ToString());
        return buffer[..length];
    }
}
