// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;

using Xunit;

namespace Prowl.Warp.Tests;

/// <summary>
/// KCP-only behaviour, over real UDP sockets on the loopback interface. The shared transport behaviour
/// is in <see cref="KcpTransportContractTests"/>.
/// </summary>
public class KcpTransportTests
{
    private static KcpTransportOptions FastOptions(LinkSimulation? simulation = null) => new()
    {
        Port = 0,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        Timeout = TimeSpan.FromSeconds(3),
        HandshakeResendInterval = TimeSpan.FromMilliseconds(50),
        Simulation = simulation,
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
        var (host, client, pump, toHost) = Connect();
        using (host)
        using (client)
        {
            host.Options.MaxDatagramsPerReceive = 4;
            for (int i = 0; i < 20; i++) toHost.Send(new byte[] { 1 }, Delivery.Unreliable);
            System.Threading.Thread.Sleep(20);
            host.Receive();
            int count = 0;
            while (host.Poll(out TransportEvent ev)) if (ev.Type == TransportEventType.Data) count++;
            Assert.InRange(count, 1, 4);
            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 20 - count, what: "remaining budgeted packets");
        }
    }

    [Fact]
    public void SlowReceiverIsDisconnectedBeforeReliableQueueGrows()
    {
        var (host, client, pump, toHost) = Connect();
        using (host)
        using (client)
        {
            client.Options.MaxPendingReliableBytes = 4096;
            // No receiver pumps or acknowledges these sends.
            for (int i = 0; i < 100 && toHost.IsConnected; i++)
                toHost.Send(new byte[1000], Delivery.Reliable);
            Assert.Equal(ConnectionState.Disconnected, toHost.State);
        }
    }

    [Fact]
    public void UnpolledDataIsBoundedPerConnection()
    {
        var (host, client, pump, toHost) = Connect();
        using (host)
        using (client)
        {
            host.MaxQueuedDataEventsPerConnection = 4;
            for (int i = 0; i < 20; i++) toHost.Send(new byte[] { 1 }, Delivery.Unreliable);
            System.Threading.Thread.Sleep(20);
            host.Receive();
            Assert.Empty(host.Connections);
            int count = 0;
            while (host.Poll(out TransportEvent ev)) if (ev.Type == TransportEventType.Data) count++;
            Assert.InRange(count, 0, 4);
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
            byte[] large = TestData.Pattern(max, seed: 1);
            byte[] empty = Array.Empty<byte>();

            toHost.Send(large, Delivery.Reliable);
            toHost.Send(empty, Delivery.Reliable);

            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 2, what: "large message");

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
            Assert.Equal(1200 - 5, max);
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
            Seed = 1234,
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
                toHost.Send(BitConverter.GetBytes(i), Delivery.Reliable);
            toHost.Send(TestData.Pattern(20_000, seed: 2), Delivery.Reliable);

            pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == count + 1, timeoutMs: 20_000, what: "messages over lossy link");

            Received[] data = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).ToArray();
            Assert.Equal(Enumerable.Range(0, count), data.Take(count).Select(e => BitConverter.ToInt32(e.Data)));
            Assert.Equal(TestData.Pattern(20_000, seed: 2), data[count].Data);
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
    public void ResolvesHostNamesAndIpv6Literals()
    {
        Assert.NotNull(KcpTransport.TryResolve("localhost:7777", 0, System.Net.Sockets.AddressFamily.InterNetworkV6));
        Assert.NotNull(KcpTransport.TryResolve("[::1]:7777", 0, System.Net.Sockets.AddressFamily.InterNetworkV6));
        Assert.NotNull(KcpTransport.TryResolve("127.0.0.1", 7777, System.Net.Sockets.AddressFamily.InterNetwork));
        Assert.Null(KcpTransport.TryResolve("[::1]:7777", 0, System.Net.Sockets.AddressFamily.InterNetwork));
        Assert.Null(KcpTransport.TryResolve("localhost:notaport", 0, System.Net.Sockets.AddressFamily.InterNetworkV6));
    }
}
