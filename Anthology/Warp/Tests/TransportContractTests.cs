// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Warp.Tests;

/// <summary>
///     Behaviour every transport must share, run once per transport by the subclasses at the bottom.
///     Transport specific behaviour is tested in that transport's own test class.
/// </summary>
public abstract class TransportContractTests : IDisposable
{
    private readonly List<Transport> _created = new();

    /// <summary>An address nothing can be reached at, reported as <see cref="DisconnectReason.Unreachable" />.</summary>
    protected abstract string UnreachableAddress { get; }

    public void Dispose()
    {
        foreach (Transport transport in _created)
        {
            transport.Dispose();
        }
    }

    /// <summary>A new transport that is already listening.</summary>
    protected abstract Transport CreateHost();

    protected abstract Transport CreateClient();

    /// <summary>The address clients connect to for <paramref name="host" />.</summary>
    protected abstract string AddressOf(Transport host);

    protected T Track<T>(T transport) where T : Transport
    {
        _created.Add(transport);
        return transport;
    }

    private (Transport Host, Transport Client, Pump Pump, Connection ToHost) Connect()
    {
        Transport host = CreateHost();
        Transport client = CreateClient();
        Pump pump = new(host, client);

        Connection toHost = client.Connect(AddressOf(host));
        Assert.Equal(ConnectionState.Connecting, toHost.State);
        pump.Until(() => toHost.IsConnected && host.Connections.Count == 1, what: "connect");
        return (host, client, pump, toHost);
    }

    [Fact]
    public void MessagesArriveOnTheChannelTheyWereSentOn()
    {
        (Transport host, _, Pump pump, Connection toHost) = Connect();
        toHost.Send(new byte[] { 1 }, Delivery.Reliable, channel: 2);
        toHost.Send(new byte[] { 2 }, Delivery.Unreliable);
        toHost.Send(new byte[] { 3 }, Delivery.Reliable, channel: 3);

        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 3, what: "data");
        Received[] data = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data)
            .OrderBy(e => e.Data[0]).ToArray();
        Assert.Equal((Delivery.Reliable, (byte)2), (data[0].Delivery, data[0].Channel));
        Assert.Equal((Delivery.Unreliable, (byte)0), (data[1].Delivery, data[1].Channel));
        Assert.Equal((Delivery.Reliable, (byte)3), (data[2].Delivery, data[2].Channel));
    }

    [Fact]
    public void ReliableMessagesStayInOrderWithinEachChannel()
    {
        (Transport host, _, Pump pump, Connection toHost) = Connect();
        for (int i = 0; i < 50; i++)
        {
            toHost.Send(BitConverter.GetBytes(i), Delivery.Reliable, channel: 0);
            toHost.Send(BitConverter.GetBytes(i), Delivery.Reliable, channel: 1);
        }

        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 100, what: "data");
        foreach (byte channel in new byte[] { 0, 1 })
        {
            Assert.Equal(Enumerable.Range(0, 50), pump.EventsOf(host)
                .Where(e => e.Type == TransportEventType.Data && e.Channel == channel)
                .Select(e => BitConverter.ToInt32(e.Data)));
        }
    }

    [Fact]
    public void ChannelsBeyondTheTransportAndUnreliableChannelsAreRejected()
    {
        (_, _, _, Connection toHost) = Connect();
        byte channels = (byte)toHost.Transport.Capabilities.Channels;
        Assert.Equal(4, channels);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            toHost.Send(new byte[] { 1 }, Delivery.Reliable, channel: channels));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            toHost.Send(new byte[] { 1 }, Delivery.Unreliable, channel: 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueueByteBudgetsReleaseMemoryAfterOverflow(bool global)
    {
        (Transport host, Transport client, Pump pump, Connection toHost) = Connect();
        if (global)
        {
            host.MaxQueuedDataBytes = 512;
        }
        else
        {
            host.MaxQueuedDataBytesPerConnection = 512;
        }

        toHost.Send(new byte[400], Delivery.Unreliable);
        toHost.Send(new byte[400], Delivery.Unreliable);
        pump.Until(() => !host.Connections.Any(), what: "queue budget disconnect");
        Assert.Single(pump.EventsOf(host), e => e.Type == TransportEventType.Disconnected);
        Assert.DoesNotContain(pump.EventsOf(host), e => e.Type == TransportEventType.Data);

        // A new connection can use the memory released by the offending peer.
        Connection second = client.Connect(AddressOf(host));
        pump.Until(() => second.IsConnected && host.Connections.Count == 1, what: "reconnect after overflow");
        second.Send(new byte[400], Delivery.Unreliable);
        pump.Until(() => pump.EventsOf(host).Any(e => e.Type == TransportEventType.Data),
            what: "reclaimed queue capacity");
        Assert.True(second.IsConnected);
    }

    [Fact]
    public void DisposedTransportsCannotReopenSocketsOrQueues()
    {
        Transport host = CreateHost();
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() => host.Listen());
        Assert.Throws<ObjectDisposedException>(() => host.Connect("127.0.0.1:7777"));
    }

    [Fact]
    public void ConnectsBothSides()
    {
        (Transport host, Transport client, Pump pump, Connection toHost) = Connect();

        Received atHost = Assert.Single(pump.EventsOf(host));
        Assert.Equal(TransportEventType.Connected, atHost.Type);
        Assert.False(atHost.Connection.IsOutgoing);
        Assert.Same(host, atHost.Connection.Transport);

        Received atClient = Assert.Single(pump.EventsOf(client));
        Assert.Equal(TransportEventType.Connected, atClient.Type);
        Assert.Same(toHost, atClient.Connection);
        Assert.True(toHost.IsOutgoing);
        Assert.Same(toHost, Assert.Single(client.Connections));
    }

    [Fact]
    public void DeliversBothDeliveriesBothWays()
    {
        (Transport host, Transport client, Pump pump, Connection toHost) = Connect();
        Connection toClient = host.Connections.Single();

        toHost.Send(new byte[] { 1, 2, 3 }, Delivery.Reliable);
        toHost.Send(new byte[] { 4, 5 }, Delivery.Unreliable);
        toClient.Send(new byte[] { 6 }, Delivery.Reliable);
        toClient.Send(new byte[] { 7 }, Delivery.Unreliable);

        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 2
                         && pump.EventsOf(client).Count(e => e.Type == TransportEventType.Data) == 2, what: "messages");

        List<Received> atHost = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).ToList();
        Assert.Contains(atHost,
            e => e.Delivery == Delivery.Reliable && e.Data.SequenceEqual(new byte[] { 1, 2, 3 }) &&
                 e.Connection == toClient);
        Assert.Contains(atHost, e => e.Delivery == Delivery.Unreliable && e.Data.SequenceEqual(new byte[] { 4, 5 }));

        List<Received> atClient = pump.EventsOf(client).Where(e => e.Type == TransportEventType.Data).ToList();
        Assert.Contains(atClient,
            e => e.Delivery == Delivery.Reliable && e.Data.SequenceEqual(new byte[] { 6 }) && e.Connection == toHost);
        Assert.Contains(atClient, e => e.Delivery == Delivery.Unreliable && e.Data.SequenceEqual(new byte[] { 7 }));
    }

    [Fact]
    public void ReliableMessagesArriveInOrder()
    {
        (Transport host, Transport _, Pump pump, Connection toHost) = Connect();

        const int count = 500;
        for (int i = 0; i < count; i++)
        {
            toHost.Send(BitConverter.GetBytes(i), Delivery.Reliable);
        }

        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == count,
            what: "all messages");

        int[] received = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data)
            .Select(e => BitConverter.ToInt32(e.Data)).ToArray();
        Assert.Equal(Enumerable.Range(0, count), received);
    }

    [Fact]
    public void LargeAndEmptyReliableMessagesArriveIntact()
    {
        (Transport host, Transport _, Pump pump, Connection toHost) = Connect();

        byte[] large = TestData.Pattern(Math.Min(toHost.GetMaxMessageSize(Delivery.Reliable), 100_000), 7);
        toHost.Send(large, Delivery.Reliable);
        toHost.Send(Array.Empty<byte>(), Delivery.Reliable);

        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 2, what: "messages");

        Received[] data = pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).ToArray();
        Assert.Equal(large, data[0].Data);
        Assert.Empty(data[1].Data);
    }

    [Fact]
    public void DisconnectNotifiesBothSidesOnce()
    {
        (Transport host, Transport client, Pump pump, Connection toHost) = Connect();

        toHost.Disconnect();
        toHost.Disconnect(); // a second call does nothing
        pump.Until(() => host.Connections.Count == 0, what: "host to notice");
        pump.For(20);

        Received clientSide = Assert.Single(pump.EventsOf(client), e => e.Type == TransportEventType.Disconnected);
        Assert.Equal(DisconnectReason.Requested, clientSide.Reason);
        Received hostSide = Assert.Single(pump.EventsOf(host), e => e.Type == TransportEventType.Disconnected);
        Assert.Equal(DisconnectReason.RemoteClosed, hostSide.Reason);
        Assert.Empty(client.Connections);
        Assert.Throws<InvalidOperationException>(() => toHost.Send(new byte[1], Delivery.Reliable));
    }

    [Fact]
    public void HostShutdownReachesClients()
    {
        (Transport host, Transport client, Pump pump, Connection toHost) = Connect();

        host.Shutdown();
        Assert.False(host.IsListening);
        pump.Until(() => toHost.State == ConnectionState.Disconnected, what: "client to notice");

        Assert.Equal(DisconnectReason.RemoteClosed,
            pump.EventsOf(client).Single(e => e.Type == TransportEventType.Disconnected).Reason);
        Assert.Equal(DisconnectReason.Requested,
            pump.EventsOf(host).Single(e => e.Type == TransportEventType.Disconnected).Reason);
    }

    [Fact]
    public void UnreachableAddressesFail()
    {
        Transport client = CreateClient();
        Pump pump = new(client);

        Connection connection = client.Connect(UnreachableAddress);
        pump.Until(() => connection.State == ConnectionState.Disconnected, what: "failure");

        Received ev = Assert.Single(pump.EventsOf(client));
        Assert.Equal(TransportEventType.Disconnected, ev.Type);
        Assert.Equal(DisconnectReason.Unreachable, ev.Reason);
    }

    [Fact]
    public void SendingBeforeConnectedThrows()
    {
        Transport host = CreateHost();
        Connection connection = CreateClient().Connect(AddressOf(host));

        Assert.Throws<InvalidOperationException>(() => connection.Send(new byte[1], Delivery.Reliable));
        Assert.Throws<InvalidOperationException>(() => connection.Send(new byte[1], Delivery.Unreliable));
    }

    [Fact]
    public void MessagesOverTheLimitAreRefused()
    {
        (Transport _, Transport _, Pump _, Connection toHost) = Connect();

        int max = toHost.GetMaxMessageSize(Delivery.Unreliable);
        if (max < 1_000_000)
        {
            Assert.Throws<ArgumentException>(() => toHost.Send(new byte[max + 1], Delivery.Unreliable));
        }
    }

    [Fact]
    public void TracksTraffic()
    {
        (Transport host, Transport _, Pump pump, Connection toHost) = Connect();

        toHost.Send(new byte[100], Delivery.Reliable);
        pump.Until(() => pump.EventsOf(host).Any(e => e.Type == TransportEventType.Data), what: "message");

        Assert.True(toHost.Stats.BytesSent >= 100);
        Assert.True(host.Connections.Single().Stats.BytesReceived >= 100);
    }
}

public sealed class LocalTransportContractTests : TransportContractTests
{
    private readonly LocalNetwork _network = new();
    private int _clients;

    protected override string UnreachableAddress => "nobody";

    protected override Transport CreateHost()
    {
        LocalTransport host = Track(new LocalTransport(_network, "host"));
        host.Listen();
        return host;
    }

    protected override Transport CreateClient() => Track(new LocalTransport(_network, $"client{_clients++}"));

    protected override string AddressOf(Transport host) => ((LocalTransport)host).Address;
}

public sealed class KcpTransportContractTests : TransportContractTests
{
    protected override string UnreachableAddress => "not a valid host name";

    internal static KcpTransportOptions Options() => new()
    {
        Port = 0,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        Timeout = TimeSpan.FromSeconds(3),
        HandshakeResendInterval = TimeSpan.FromMilliseconds(50)
    };

    protected override Transport CreateHost()
    {
        KcpTransport host = Track(new KcpTransport(Options()));
        host.Listen();
        return host;
    }

    protected override Transport CreateClient() => Track(new KcpTransport(Options()));

    protected override string AddressOf(Transport host) => $"127.0.0.1:{((KcpTransport)host).LocalPort}";
}
