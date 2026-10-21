using System;
using System.Linq;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Warp.Tests;

public class BatchingTransportTests
{
    private static Transport Wrap(Transport inner) => new Prowl.Warp.Transports.Batching.BatchingTransport(inner, 128);

    private static void Immediate(Connection connection, byte[] data) => connection.Send(data, Delivery.Reliable, SendFlags.Immediate);

    [Fact]
    public void ImmediateSendFlushesEarlierMessagesInOrder()
    {
        LocalNetwork network = new();
        using Transport host = Wrap(new LocalTransport(network, "host"));
        LocalTransport inner = new(network, "client");
        using Transport client = Wrap(inner);
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected);
        connection.Send(new byte[] { 1 }, Delivery.Reliable);
        Immediate(connection, new byte[] { 2 });
        Assert.Equal(1, inner.Connections.Single().Stats.PacketsSent);
        host.Receive();
        Assert.True(host.Poll(out TransportEvent first));
        // Pump consumed the connection event during setup.
        Assert.Equal(new byte[] { 1 }, first.Data.ToArray());
        Assert.True(host.Poll(out TransportEvent second));
        Assert.Equal(new byte[] { 2 }, second.Data.ToArray());
    }

    [Theory]
    [InlineData(Delivery.Reliable)]
    [InlineData(Delivery.Unreliable)]
    public void SmallMessagesShareOnePacketAndKeepTheirBoundaries(Delivery delivery)
    {
        LocalNetwork network = new();
        using Transport host = Wrap(new LocalTransport(network, "host"));
        LocalTransport inner = new(network, "client");
        using Transport client = Wrap(inner);
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected);
        for (byte i = 0; i < 10; i++) connection.Send(new[] { i }, delivery);
        Assert.Equal(0, inner.Connections.Single().Stats.PacketsSent);
        client.Flush();
        Assert.Equal(1, inner.Connections.Single().Stats.PacketsSent);
        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == 10);
        Assert.Equal(Enumerable.Range(0, 10), pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).Select(e => (int)Assert.Single(e.Data)));
        Assert.All(pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data), e => Assert.Equal(delivery, e.Delivery));
    }

    [Fact]
    public void LargeAndEmptyMessagesKeepReliableOrdering()
    {
        LocalNetwork network = new();
        using Transport host = Wrap(new LocalTransport(network, "host"));
        using Transport client = Wrap(new LocalTransport(network, "client"));
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected);
        byte[][] messages = { new byte[] { 1 }, TestData.Pattern(512, 42), Array.Empty<byte>(), new byte[] { 2 } };
        foreach (byte[] message in messages) connection.Send(message, Delivery.Reliable);
        pump.Until(() => pump.EventsOf(host).Count(e => e.Type == TransportEventType.Data) == messages.Length);
        Assert.Equal(messages, pump.EventsOf(host).Where(e => e.Type == TransportEventType.Data).Select(e => e.Data));
    }

    [Fact]
    public void MalformedFramesDisconnectWithoutDeliveringPartialMessages()
    {
        LocalNetwork network = new();
        using Transport host = Wrap(new LocalTransport(network, "host"));
        LocalTransport inner = new(network, "client");
        using Transport client = Wrap(inner);
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected);
        // Valid first message followed by a truncated second length prefix.
        inner.Connections.Single().Send(new byte[] { 1, 0, 0, 0, 42, 1 }, Delivery.Reliable);
        pump.Until(() => pump.EventsOf(host).Any(e => e.Type == TransportEventType.Disconnected));
        Assert.DoesNotContain(pump.EventsOf(host), e => e.Type == TransportEventType.Data);
        Assert.Equal(DisconnectReason.ProtocolError, pump.EventsOf(host).Single(e => e.Type == TransportEventType.Disconnected).Reason);
    }
}
