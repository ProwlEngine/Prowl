using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Warp.Tests;

public class EncryptedTransportTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static Transport Secure(Transport inner, byte[] key)
    {
        return new Prowl.Warp.Transports.Encryption.EncryptedTransport(inner, key, TimeSpan.FromSeconds(1));
    }

    private static byte[] PeekPacket(LocalTransport transport)
    {
        var inbox = (IEnumerable)typeof(LocalTransport).GetField("_inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transport)!;
        object packet = inbox.Cast<object>().First(p => p.GetType().GetProperty("Buffer")!.GetValue(p) != null);
        byte[] buffer = (byte[])packet.GetType().GetProperty("Buffer")!.GetValue(packet)!;
        int length = (int)packet.GetType().GetProperty("Length")!.GetValue(packet)!;
        return buffer.AsSpan(0, length).ToArray();
    }

    [Theory]
    [InlineData(Delivery.Reliable)]
    [InlineData(Delivery.Unreliable)]
    [InlineData(Delivery.Bulk)]
    public void PayloadIsEncryptedAndReplaysAreIgnored(Delivery delivery)
    {
        LocalNetwork network = new();
        LocalTransport hostInner = new(network, "host"), clientInner = new(network, "client");
        using Transport host = Secure(hostInner, Key);
        using Transport client = Secure(clientInner, Key);
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected && pump.EventsOf(host).Any(e => e.Type == TransportEventType.Connected), what: "encrypted handshake");
        byte[] secret = Encoding.UTF8.GetBytes("this is private game state");
        connection.Send(secret, delivery);
        byte[] wire = PeekPacket(hostInner);
        Assert.Equal(-1, wire.AsSpan().IndexOf(secret));
        pump.Until(() => pump.EventsOf(host).Any(e => e.Type == TransportEventType.Data), what: "decryption");
        clientInner.Connections.Single().Send(wire, delivery);
        pump.For(20);
        Received received = Assert.Single(pump.EventsOf(host), e => e.Type == TransportEventType.Data);
        Assert.Equal(secret, received.Data);
        Assert.Equal(delivery, received.Delivery);
    }

    [Theory]
    [InlineData(Delivery.Reliable)]
    [InlineData(Delivery.Unreliable)]
    [InlineData(Delivery.Bulk)]
    public void AlteredCiphertextNeverReachesApplicationCode(Delivery delivery)
    {
        LocalNetwork network = new();
        LocalTransport hostInner = new(network, "host"), clientInner = new(network, "client");
        using Transport host = Secure(hostInner, Key);
        using Transport client = Secure(clientInner, Key);
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected && pump.EventsOf(host).Any(e => e.Type == TransportEventType.Connected), what: "encrypted handshake");
        connection.Send(new byte[] { 42 }, delivery);
        var inbox = (IEnumerable)typeof(LocalTransport).GetField("_inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hostInner)!;
        object packet = inbox.Cast<object>().Single();
        byte[] buffer = (byte[])packet.GetType().GetProperty("Buffer")!.GetValue(packet)!;
        buffer[9] ^= 1;
        pump.For(30);
        Assert.DoesNotContain(pump.EventsOf(host), e => e.Type == TransportEventType.Data);
        Assert.Equal(delivery == Delivery.Unreliable, host.Connections.Any());
    }

    [Fact]
    public void WrongKeysNeverExposeAConnectedPeer()
    {
        LocalNetwork network = new();
        using Transport host = Secure(new LocalTransport(network, "host"), Key);
        using Transport client = Secure(new LocalTransport(network, "client"), new byte[32]);
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.For(100);
        Assert.DoesNotContain(pump.EventsOf(host), e => e.Type == TransportEventType.Connected);
        Assert.DoesNotContain(pump.EventsOf(client), e => e.Type == TransportEventType.Connected);
        Assert.Equal(ConnectionState.Disconnected, connection.State);
    }
}
