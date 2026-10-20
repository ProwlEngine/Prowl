// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Warp.Tests;

/// <summary>Local-only behaviour. The shared transport behaviour is in <see cref="LocalTransportContractTests"/>.</summary>
public class LocalTransportTests
{
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
