// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Reflection;

using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Warp.Tests;

/// <summary>Local-only behaviour. The shared transport behaviour is in <see cref="LocalTransportContractTests" />.</summary>
public class LocalTransportTests
{
    private static void FillControlInbox(LocalTransport transport)
    {
        while (InboxCount(transport) < transport.MaxInboxPackets + transport.MaxConnections * 2)
        {
            transport.Connect("missing").Disconnect();
        }
    }

    [Fact]
    public void FailedAcceptReleasesBothConnectionSlots()
    {
        LocalNetwork network = new();
        using LocalTransport host = new(network, "host") { MaxConnections = 1 };
        using LocalTransport client = new(network, "client") { MaxConnections = 1, MaxInboxPackets = 1 };
        host.Listen();
        FillControlInbox(client);

        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Step();

        Assert.Equal(ConnectionState.Disconnected, connection.State);
        Assert.Empty(host.Connections);
        Assert.Empty(client.Connections);
        Assert.DoesNotContain(pump.EventsOf(host), ev => ev.Type == TransportEventType.Connected);
        Assert.Single(pump.EventsOf(client), ev => ev.Connection == connection);

        Connection retry = client.Connect("host");
        pump.Step();
        Assert.True(retry.IsConnected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectionSurvivesAFullControlInbox(bool serverFull)
    {
        LocalNetwork network = new();
        using LocalTransport host = new(network, "host") { MaxConnections = 1 };
        using LocalTransport occupant = new(network, "occupant");
        using LocalTransport client = new(network, "client") { MaxConnections = 1, MaxInboxPackets = 1 };
        if (serverFull)
        {
            host.Listen();
            occupant.Connect("host");
            host.Receive();
            occupant.Receive();
        }

        FillControlInbox(client);
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Step();

        Assert.Equal(ConnectionState.Disconnected, connection.State);
        Received rejection = Assert.Single(pump.EventsOf(client), ev => ev.Connection == connection);
        Assert.Equal(serverFull ? DisconnectReason.ServerFull : DisconnectReason.Unreachable, rejection.Reason);
        Assert.Empty(client.Connections);
    }

    [Fact]
    public void DisconnectSurvivesAFullControlInboxAndPreservesEarlierData()
    {
        LocalNetwork network = new();
        using LocalTransport host = new(network, "host")
        {
            MaxConnections = 2, MaxInboxPackets = 1, MaxPacketsPerReceive = 1
        };
        using LocalTransport client = new(network, "client");
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Step();
        pump.EventsOf(host).Clear();

        connection.Send(new byte[] { 42 }, Delivery.Reliable);
        FillControlInbox(host);
        connection.Disconnect();
        Assert.Equal(host.MaxInboxPackets + host.MaxConnections * 2, InboxCount(host));

        pump.Step();
        Assert.Equal(new byte[] { 42 }, Assert.Single(pump.EventsOf(host), ev =>
            ev.Type == TransportEventType.Data).Data);
        Assert.Single(host.Connections);
        Connection later = host.Connect("missing");
        for (int i = 1; i < host.MaxInboxPackets + host.MaxConnections * 2; i++)
        {
            pump.Step();
        }

        Assert.Same(later, Assert.Single(host.Connections));
        Assert.Equal(ConnectionState.Connecting, later.State);
        Received closed = Assert.Single(pump.EventsOf(host), ev =>
            ev.Type == TransportEventType.Disconnected && !ev.Connection.IsOutgoing);
        Assert.Equal(DisconnectReason.RemoteClosed, closed.Reason);
        Assert.Null(((LocalConnection)closed.Connection).Peer);
        Assert.Equal(0, host.PendingInboxBytes);
        Assert.Equal(1, InboxCount(host)); // Later traffic cannot postpone the earlier disconnect.
        pump.Step();
        Assert.Empty(host.Connections);
        Assert.Single(pump.EventsOf(host), ev => ev.Connection == closed.Connection &&
                                                 ev.Type == TransportEventType.Disconnected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShutdownClosesPendingHandshakeDespiteAFullRemoteInbox(bool acceptQueued)
    {
        LocalNetwork network = new();
        using LocalTransport host = new(network, "host") { MaxConnections = 2, MaxInboxPackets = 1 };
        using LocalTransport client = new(network, "client") { MaxConnections = 2, MaxInboxPackets = 1 };
        host.Listen();
        Connection connection = client.Connect("host");
        if (acceptQueued)
        {
            host.Receive();
            FillControlInbox(host);
            client.Shutdown();
            host.Receive();
        }
        else
        {
            FillControlInbox(client);
            host.Shutdown();
            client.Receive();
            Assert.Equal(ConnectionState.Disconnected, connection.State);
        }

        Assert.Empty(host.Connections);
        Assert.Empty(client.Connections);
        Assert.Equal(0, host.PendingInboxBytes);
        Assert.Equal(0, client.PendingInboxBytes);
    }

    [Fact]
    public void MaximumInboxPacketLimitDoesNotOverflowTheControlReserve()
    {
        using LocalTransport client = new(new LocalNetwork()) { MaxInboxPackets = int.MaxValue };
        Connection connection = client.Connect("missing");
        client.Receive();
        Assert.Equal(ConnectionState.Disconnected, connection.State);
        Assert.True(client.Poll(out TransportEvent rejection));
        Assert.Equal(DisconnectReason.Unreachable, rejection.Reason);
        Assert.False(client.Poll(out _));
    }

    private static int InboxCount(LocalTransport transport)
    {
        object inbox = typeof(LocalTransport).GetField("_inbox", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(transport)!;
        return (int)inbox.GetType().GetProperty("Count")!.GetValue(inbox)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShutdownReleasesInboxAndRejectsFurtherData(bool sendAfterShutdown)
    {
        LocalNetwork network = new();
        using LocalTransport host = new(network, "host");
        using LocalTransport client = new(network, "client");
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected, what: "connect");
        connection.Send(new byte[1000], Delivery.Reliable);
        host.Shutdown();
        if (sendAfterShutdown)
        {
            connection.Send(new byte[1000], Delivery.Reliable);
        }

        Assert.Equal(0, InboxCount(host));
    }

    [Fact]
    public void InboxIsBoundedBeforeReceiveRuns()
    {
        LocalNetwork network = new();
        using LocalTransport host = new(network, "host");
        using LocalTransport client = new(network, "client");
        host.MaxInboxBytes = 4096;
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected, what: "connect");
        for (int i = 0; i < 20 && connection.IsConnected; i++)
        {
            connection.Send(new byte[1000], Delivery.Reliable);
        }

        Assert.Equal(ConnectionState.Disconnected, connection.State);
        Assert.InRange(InboxCount(host), 0, 5);
    }

    [Fact]
    public void TwoListenersCannotShareAnAddress()
    {
        LocalNetwork network = new();
        new LocalTransport(network, "a").Listen();
        Assert.Throws<InvalidOperationException>(() => new LocalTransport(network, "a").Listen());
    }

    [Fact]
    public void ShutdownFreesTheAddress()
    {
        LocalNetwork network = new();
        LocalTransport first = new(network, "a");
        first.Listen();
        first.Shutdown();

        new LocalTransport(network, "a").Listen();
    }

    [Fact]
    public void SeparateNetworksDontSeeEachOther()
    {
        new LocalTransport(new LocalNetwork(), "host").Listen();

        LocalTransport client = new(new LocalNetwork());
        Pump pump = new(client);
        Connection connection = client.Connect("host");
        pump.Step();

        Assert.Equal(ConnectionState.Disconnected, connection.State);
        Assert.Equal(DisconnectReason.Unreachable, Assert.Single(pump.EventsOf(client)).Reason);
    }

    [Fact]
    public void ClientAddressIsItsOwnLocalAddress()
    {
        LocalNetwork network = new();
        LocalTransport host = new(network, "host");
        host.Listen();
        LocalTransport client = new(network, "player-one");
        Pump pump = new(host, client);

        client.Connect("host");
        pump.Until(() => host.Connections.Count == 1);

        Assert.Equal("player-one", Assert.Single(host.Connections).Address);
    }
}
