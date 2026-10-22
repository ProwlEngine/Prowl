using System;

using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Warp.Tests;

public class TransportCapabilitiesTests
{
    [Fact]
    public void UnsupportedDeliveryIsRejectedBeforeBackendSend()
    {
        using Transport transport = new ReliableBackend();
        Connection peer = transport.Connect("peer");
        Assert.Throws<NotSupportedException>(() => peer.Send(new byte[0], Delivery.Unreliable));
    }

    [Fact]
    public void SessionRejectsUnavailableBackendBeforeStartup()
    {
        using Transport transport = new ReliableBackend(available: false);
        Assert.Throws<NotSupportedException>(() => NetSession.CreateHost(transport));
        Assert.Throws<NotSupportedException>(() => NetSession.Host(transport));
    }

    [Fact]
    public void SessionRequiresReliableDelivery()
    {
        using Transport transport = new ReliableBackend(DeliverySupport.Unreliable);
        Assert.Throws<NotSupportedException>(() => NetSession.CreateHost(transport));
    }

    private sealed class ReliableBackend(DeliverySupport deliveries = DeliverySupport.Reliable, bool available = true)
        : Transport
    {
        public override TransportCapabilities Capabilities =>
            new(available, deliveries, TransportExecution.WorkerPreferred);

        public override bool IsListening => false;

        public override void Listen()
        {
        }

        public override Connection Connect(string address)
        {
            Connection peer = new Peer(this);
            AddConnection(peer);
            RaiseConnected(peer);
            return peer;
        }

        public override void Receive()
        {
        }

        public override void Flush()
        {
        }

        public override void Shutdown()
        {
        }

        private sealed class Peer(Transport transport) : Connection(transport, 1, "peer", true)
        {
            public override TimeSpan RoundTripTime => default;
            public override int GetMaxMessageSize(Delivery delivery) => 65536;
            public override void Send(ReadOnlySpan<byte> data, Delivery delivery) => ValidateSend(data, delivery);

            public override void Disconnect()
            {
            }
        }
    }
}
