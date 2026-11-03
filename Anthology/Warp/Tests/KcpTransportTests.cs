// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    public void ImmediateSendFlushesOnlyItsOwnConnection()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using KcpTransport alice = new(FastOptions());
        using KcpTransport bob = new(FastOptions());
        Pump pump = new(host, alice, bob);
        alice.Connect($"127.0.0.1:{host.LocalPort}");
        bob.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => host.Connections.Count == 2, what: "connect");
        pump.For(50);
        Connection toAlice = host.Connections.First(c => c.Address.EndsWith($":{alice.LocalPort}"));
        Connection toBob = host.Connections.First(c => c != toAlice);

        toBob.Send(new byte[] { 1 }, Delivery.Reliable);
        toAlice.Send(new byte[] { 2 }, Delivery.Reliable, SendFlags.Immediate);
        Thread.Sleep(50);
        alice.Receive();
        bob.Receive();

        Assert.Contains(TransportEventType.Data, Drain(alice));
        Assert.DoesNotContain(TransportEventType.Data, Drain(bob));
    }

    private static TransportEventType[] Drain(Transport transport)
    {
        List<TransportEventType> events = new();
        while (transport.Poll(out TransportEvent ev))
        {
            events.Add(ev.Type);
        }

        return events.ToArray();
    }

    [Fact]
    public void SendersCanSeeTheirQueueAndBackOffBeforeTheLimit()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxPendingReliableBytes = 64 * 1024;
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(clientOptions: options);
        using (host)
        using (client)
        {
            Assert.Equal(0, toHost.PendingSendBytes);
            Assert.Equal(64 * 1024, toHost.MaxPendingSendBytes);

            // No receiver pumps or acknowledges these sends, so a sender that checks never trips the limit.
            byte[] message = new byte[1000];
            int sent = 0;
            while (toHost.PendingSendBytes + 2 * options.Mtu <= toHost.MaxPendingSendBytes)
            {
                toHost.Send(message, Delivery.Reliable);
                sent++;
            }

            Assert.True(toHost.IsConnected);
            Assert.True(sent > 10);
            pump.Until(() => toHost.PendingSendBytes == 0, what: "acknowledgements");
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 25)]
    [InlineData(0, 30)]
    [InlineData(40, 60)]
    [InlineData(5, 100)]
    public void RestartsAreRecognisedHoweverLongTheSessionRan(int startDays, int nowDays)
    {
        long start = startDays * 86_400_000L;
        long now = nowDays * 86_400_000L;
        Assert.True(KcpTransport.IssuedSince((uint)now, start, now));
        Assert.True(KcpTransport.IssuedSince((uint)(now - 5000), now - 10_000, now));
        Assert.False(KcpTransport.IssuedSince((uint)(now - 5000), now - 1000, now));
    }

    [Fact]
    public void ConnectingTwiceToOneAddressFailsLaterNotSynchronously()
    {
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect();
        using (host)
        using (client)
        {
            Connection again = client.Connect($"127.0.0.1:{host.LocalPort}");
            Assert.Equal(ConnectionState.Connecting, again.State);
            pump.Until(() => again.State == ConnectionState.Disconnected, what: "refusal");

            Assert.Equal(DisconnectReason.Unreachable,
                Assert.Single(pump.EventsOf(client), ev => ev.Connection == again).Reason);
            Assert.True(toHost.IsConnected);
        }
    }

    [Fact]
    public void StatsCountRetransmitsOnALossyLink()
    {
        LinkSimulation lossy = new() { PacketLoss = 0.3, Seed = 7 };
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) =
            Connect(clientOptions: FastOptions(lossy));
        using (host)
        using (client)
        {
            Assert.Equal(0, toHost.Stats.Retransmits);
            for (int i = 0; i < 50; i++)
            {
                toHost.Send(new byte[100], Delivery.Reliable);
            }

            pump.Until(() => toHost.PendingSendBytes == 0, what: "acknowledgements");
            Assert.True(toHost.Stats.Retransmits > 0);
        }
    }

    [Fact]
    public void SendRateCapSpreadsTrafficAndServesLowerChannelsFirst()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxSendRate = 50_000;
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(clientOptions: options);
        using (host)
        using (client)
        {
            for (int i = 0; i < 100; i++)
            {
                toHost.Send(new byte[1000], Delivery.Reliable, channel: 1);
            }

            pump.For(500);
            int Bulk() => pump.EventsOf(host).Count(ev => ev.Type == TransportEventType.Data && ev.Channel == 1);
            Assert.InRange(Bulk(), 1, 40);

            toHost.Send(new byte[] { 7 }, Delivery.Reliable);
            pump.Until(() => pump.EventsOf(host).Any(ev => ev.Type == TransportEventType.Data && ev.Channel == 0),
                timeoutMs: 300, what: "the channel 0 message");
            Assert.True(Bulk() < 100);

            pump.Until(() => Bulk() == 100, what: "the rest of the bulk transfer");
            Assert.True(toHost.IsConnected);
        }
    }

    [Theory]
    [InlineData(Delivery.Unreliable)]
    [InlineData(Delivery.UnreliableSequenced)]
    public void LargeUnreliableMessagesArriveWholeOrNotAtAll(Delivery delivery)
    {
        LinkSimulation lossy = new() { PacketLoss = 0.2, Seed = 3 };
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) =
            Connect(clientOptions: FastOptions(lossy));
        using (host)
        using (client)
        {
            Assert.True(toHost.GetMaxMessageSize(delivery) >= 32 * 1024);
            byte[] message = TestData.Pattern(20_000, 1);
            for (int i = 0; i < 30; i++)
            {
                toHost.Send(message, delivery);
                pump.For(5);
            }

            pump.For(100);
            Received[] arrived = pump.EventsOf(host).Where(ev => ev.Type == TransportEventType.Data).ToArray();
            Assert.NotEmpty(arrived);
            Assert.True(arrived.Length < 30);
            Assert.All(arrived, ev =>
            {
                Assert.Equal(message, ev.Data);
                Assert.Equal(delivery, ev.Delivery);
            });
        }
    }

    [Fact]
    public void SequencedMessagesNeverGoBackwards()
    {
        LinkSimulation jittery = new() { Latency = TimeSpan.FromMilliseconds(5), Jitter = TimeSpan.FromMilliseconds(40), Seed = 5 };
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) =
            Connect(clientOptions: FastOptions(jittery));
        using (host)
        using (client)
        {
            for (int i = 0; i < 200; i++)
            {
                toHost.Send(BitConverter.GetBytes(i), Delivery.UnreliableSequenced, channel: 2);
                toHost.Send(BitConverter.GetBytes(-i), Delivery.UnreliableSequenced, channel: 3);
                pump.Step();
            }

            pump.For(200);
            foreach (byte channel in new byte[] { 2, 3 })
            {
                int[] values = pump.EventsOf(host)
                    .Where(ev => ev.Type == TransportEventType.Data && ev.Channel == channel)
                    .Select(ev => Math.Abs(BitConverter.ToInt32(ev.Data))).ToArray();
                Assert.True(values.Length > 20);
                Assert.True(values.Length < 200);
                for (int i = 1; i < values.Length; i++)
                {
                    Assert.True(values[i] > values[i - 1], $"{values[i]} arrived after {values[i - 1]}");
                }
            }
        }
    }

    [Fact]
    public void IoThreadsReadTheSocketWhileTheGameThreadIsBusy()
    {
        KcpTransportOptions options = FastOptions();
        options.UseIoThreads = true;
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(options, options);
        using (host)
        using (client)
        {
            for (int i = 0; i < 10; i++)
            {
                toHost.Send(new byte[] { (byte)i }, Delivery.Unreliable);
            }

            pump.EventsOf(host).Clear();
            Thread.Sleep(100);
            Assert.True(host.QueuedDatagrams >= 10);

            host.Receive();
            int received = 0;
            while (host.Poll(out TransportEvent ev))
            {
                received += ev.Type == TransportEventType.Data ? 1 : 0;
            }

            Assert.Equal(10, received);
            Assert.Equal(0, host.QueuedDatagrams);
        }
    }

    [Fact]
    public void ACookieOpensOneSessionOnly()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);
        byte[] response = RawHandshake(raw, pump, 0x5555, out _);
        host.Connections.Single().Disconnect();
        pump.Step();

        raw.Send(response);
        pump.For(100);
        Assert.Empty(host.Connections);
    }

    [Fact]
    public void OneAddressCannotTakeMoreThanItsShareOfConnections()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxConnectionsPerAddress = 3;
        using KcpTransport host = new(options);
        host.Listen();
        KcpTransport[] clients = Enumerable.Range(0, 5).Select(_ => new KcpTransport(FastOptions())).ToArray();
        try
        {
            Pump pump = new(clients.Prepend(host).ToArray());
            Connection[] connections = clients.Select(c => c.Connect($"127.0.0.1:{host.LocalPort}")).ToArray();
            pump.Until(() => connections.All(c => c.State != ConnectionState.Connecting), what: "every attempt to finish");

            Assert.Equal(3, connections.Count(c => c.IsConnected));
            Assert.Equal(3, host.Connections.Count);
            Assert.All(connections.Where(c => !c.IsConnected), c =>
                Assert.Equal(DisconnectReason.Rejected,
                    pump.EventsOf(clients[Array.IndexOf(connections, c)]).Single(ev => ev.Connection == c).Reason));
        }
        finally
        {
            foreach (KcpTransport client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public void HandshakesFromOneAddressAreRateLimited()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxHandshakesPerAddressPerSecond = 4;
        using KcpTransport host = new(options);
        host.Listen();
        KcpTransport[] clients = Enumerable.Range(0, 12).Select(_ => new KcpTransport(FastOptions())).ToArray();
        try
        {
            Pump pump = new(clients.Prepend(host).ToArray());
            Connection[] connections = clients.Select(c => c.Connect($"127.0.0.1:{host.LocalPort}")).ToArray();
            pump.For(200);
            Assert.InRange(host.Connections.Count, 1, 6);

            pump.Until(() => connections.All(c => c.IsConnected), timeoutMs: 4000, what: "the rest, at the limited rate");
        }
        finally
        {
            foreach (KcpTransport client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public void AFloodOfTinyDatagramsCannotGrowTheIoQueuePastItsBudget()
    {
        KcpTransportOptions options = FastOptions();
        options.UseIoThreads = true;
        options.MaxIoQueueBytes = 64 * 1024;
        using KcpTransport host = new(options);
        host.Listen();
        using Socket raw = RawSocket(host);
        byte[] tiny = { 99 };
        for (int i = 0; i < 3000; i++)
        {
            raw.Send(tiny);
        }

        Thread.Sleep(300);
        Assert.InRange(host.QueuedDatagrams, 1, 64 * 1024 / 256);
    }

    [Fact]
    public void UnreliableTrafficOverTheRateCapIsDroppedAndDoesNotStallReliable()
    {
        KcpTransportOptions options = FastOptions();
        options.MaxSendRate = 50_000;
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(clientOptions: options);
        using (host)
        using (client)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int sent = 0;
            while (watch.ElapsedMilliseconds < 2000)
            {
                toHost.Send(new byte[1000], Delivery.Unreliable);
                toHost.Send(new byte[1000], Delivery.Unreliable);
                sent += 2;
                pump.Step();
                Thread.Sleep(10);
            }

            pump.For(100);
            int arrived = pump.EventsOf(host).Count(ev => ev.Type == TransportEventType.Data);
            Assert.InRange(arrived, 1, sent * 3 / 4);
            Assert.Equal(sent - arrived, toHost.Stats.PacketsDropped);

            toHost.Send(new byte[] { 7, 7 }, Delivery.Reliable);
            watch.Restart();
            pump.Until(() => pump.EventsOf(host).Any(ev => ev.Type == TransportEventType.Data && ev.Delivery == Delivery.Reliable),
                timeoutMs: 1000, what: "the reliable message");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IoThreadsCarryALargeBurstAndCountWhatTheyDrop(bool smallQueue)
    {
        KcpTransportOptions options = FastOptions();
        options.UseIoThreads = true;
        if (smallQueue)
        {
            options.MaxIoQueueBytes = 64 * 1024;
        }

        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect(options, options);
        using (host)
        using (client)
        {
            Connection toClient = host.Connections.Single();
            long before = toClient.Stats.PacketsSent;
            for (int i = 0; i < 3000; i++)
            {
                toClient.Send(new byte[1000], Delivery.Unreliable);
            }

            ConnectionStats stats = toClient.Stats;
            Assert.Equal(3000, stats.PacketsSent - before + stats.PacketsDropped);
            if (smallQueue)
            {
                Assert.True(stats.PacketsDropped > 0);
                return;
            }

            pump.Until(() => pump.EventsOf(client).Count(ev => ev.Type == TransportEventType.Data) == 3000,
                what: "the whole burst");
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
    public void UnreliableMessagesAreLimitedToThirtyTwoPieces()
    {
        (KcpTransport host, KcpTransport client, Pump pump, Connection toHost) = Connect();
        using (host)
        using (client)
        {
            int max = toHost.GetMaxMessageSize(Delivery.Unreliable);
            Assert.Equal(KcpPacket.MaxUnreliablePieces * (1200 - KcpPacket.FramedHeaderSize), max);
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

    [Fact]
    public void ReplayedRequestAndResponseDoNotReplaceRestartedSession()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);

        byte[] firstResponse = RawHandshake(raw, pump, 0x1111, out _);
        byte[] firstRequest = Request(0x1111);
        RawHandshake(raw, pump, 0x2222, out _);
        Connection restarted = Assert.Single(host.Connections);

        raw.Send(firstRequest);
        pump.For(100);
        raw.Send(firstResponse);
        pump.For(200);
        Assert.Same(restarted, Assert.Single(host.Connections));
        Assert.True(restarted.IsConnected);
    }

    [Fact]
    public void ForgedPongDoesNotInflateRoundTripTime()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);
        RawHandshake(raw, pump, 0x3333, out uint session);

        byte[] pong = new byte[KcpPacket.PingSize];
        pong[0] = (byte)KcpPacketType.Pong;
        BinaryPrimitives.WriteUInt32LittleEndian(pong.AsSpan(1), session);
        BinaryPrimitives.WriteUInt32LittleEndian(pong.AsSpan(KcpPacket.SessionHeaderSize), 0xF000_0000);
        raw.Send(pong);
        pump.For(100);

        Assert.True(Assert.Single(host.Connections).RoundTripTime < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SpoofedRequestsDoNotBlockAGenuineRestart()
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);

        RawHandshake(raw, pump, 0x1111, out _);
        Connection first = Assert.Single(host.Connections);

        raw.Send(Request(0x2222));
        byte[] challenge = Expect(raw, pump, KcpPacketType.Challenge);
        raw.Send(Request(0x9999));
        pump.For(100);
        raw.Send(Response(Request(0x2222), challenge));
        Expect(raw, pump, KcpPacketType.Accept);

        Connection restarted = Assert.Single(host.Connections);
        Assert.NotSame(first, restarted);
        Assert.True(restarted.IsConnected);
    }

    [Theory]
    [InlineData(KcpPacket.WireVersion + 1, 4, 1200, 256)]
    [InlineData(KcpPacket.WireVersion, 3, 1200, 256)]
    [InlineData(KcpPacket.WireVersion, 4, 1000, 256)]
    [InlineData(KcpPacket.WireVersion, 4, 1200, 128)]
    public void HandshakesWithOtherSettingsAreRejected(int version, int channels, int mtu, int window)
    {
        using KcpTransport host = new(FastOptions());
        host.Listen();
        using Socket raw = RawSocket(host);
        Pump pump = new(host);

        byte[] request = Request(0x5555);
        request[KcpPacket.HandshakeVersionOffset] = (byte)version;
        request[KcpPacket.HandshakeChannelsOffset] = (byte)channels;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(KcpPacket.HandshakeMtuOffset), (ushort)mtu);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(KcpPacket.HandshakeWindowOffset), (ushort)window);
        raw.Send(request);

        byte[] reject = Expect(raw, pump, KcpPacketType.Reject);
        Assert.Equal((byte)DisconnectReason.ProtocolError, reject[5]);
        Assert.Empty(host.Connections);
    }

    [Theory]
    [InlineData(2, 1200, 256u)]
    [InlineData(4, 1000, 256u)]
    [InlineData(4, 1200, 128u)]
    public void ClientsWithOtherSettingsAreRefused(int channels, int mtu, uint window)
    {
        KcpTransportOptions clientOptions = FastOptions();
        clientOptions.Channels = channels;
        clientOptions.Mtu = mtu;
        clientOptions.Reliability.ReceiveWindow = window;
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
        byte[] request = Request(nonce);
        raw.Send(request);
        byte[] challenge = Expect(raw, pump, KcpPacketType.Challenge);

        byte[] response = Response(request, challenge);
        raw.Send(response);
        byte[] accept = Expect(raw, pump, KcpPacketType.Accept);
        Assert.Equal(nonce, BinaryPrimitives.ReadUInt32LittleEndian(accept.AsSpan(1)));
        session = BinaryPrimitives.ReadUInt32LittleEndian(accept.AsSpan(5));
        return response;
    }

    /// <summary>A ConnectRequest with the default settings.</summary>
    private static byte[] Request(uint nonce)
    {
        KcpTransportOptions defaults = new();
        byte[] request = new byte[KcpPacket.HandshakeRequestSize];
        request[0] = (byte)KcpPacketType.ConnectRequest;
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(1), defaults.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(5), nonce);
        request[KcpPacket.HandshakeVersionOffset] = KcpPacket.WireVersion;
        request[KcpPacket.HandshakeChannelsOffset] = (byte)defaults.Channels;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(KcpPacket.HandshakeMtuOffset), (ushort)defaults.Mtu);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(KcpPacket.HandshakeWindowOffset),
            (ushort)defaults.Reliability.ReceiveWindow);
        return request;
    }

    /// <summary>The ChallengeResponse answering <paramref name="challenge" /> for <paramref name="request" />.</summary>
    private static byte[] Response(byte[] request, byte[] challenge)
    {
        byte[] response = (byte[])request.Clone();
        response[0] = (byte)KcpPacketType.ChallengeResponse;
        challenge.AsSpan(9, KcpPacket.CookieSize).CopyTo(response.AsSpan(9));
        challenge.AsSpan(5, 4).CopyTo(response.AsSpan(KcpPacket.HandshakeIssuedOffset));
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
