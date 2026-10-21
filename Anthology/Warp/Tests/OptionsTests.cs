using System;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Warp.Tests;

public class OptionsTests
{
    [Fact]
    public void SessionOptionsAreValidatedAndIsolated()
    {
        LocalNetwork network = new();
        using LocalTransport transport = new(network, "host");
        Assert.Throws<ArgumentOutOfRangeException>(() => NetSession.Host(transport, options: new() { MaxMessageSize = -1 }));
        Assert.False(transport.IsListening);
        NetSessionOptions options = new() { MaxPeers = 3 };
        using NetSession host = NetSession.Host(transport, options: options);
        options.MaxPeers = 1;
        var snapshot = host.Options;
        snapshot.MaxPeers = 2;
        Assert.Equal(3, host.Options.MaxPeers);
    }

    [Fact]
    public void ReadingSessionOptionsDoesNotAllocate()
    {
        using LocalTransport transport = new(new LocalNetwork(), "host");
        using NetSession host = NetSession.Host(transport);
        int total = 0;
        for (int i = 0; i < 100; i++) total += host.Options.MaxPeers;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) total += host.Options.MaxPeers;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1100 * 64, total);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void KcpOptionsAndNestedSettingsAreIsolated()
    {
        KcpTransportOptions options = new() { Port = 0, Simulation = new() { PacketLoss = 0.1 } };
        using KcpTransport transport = new(options);
        options.Mtu = 500;
        options.Reliability.ReceiveWindow = 1;
        options.Simulation.PacketLoss = 1;
        transport.Options.Mtu = 600;
        Assert.Equal(1200, transport.Options.Mtu);
        Assert.Equal(4096u, transport.Options.Reliability.ReceiveWindow);
        Assert.Equal(0.1, transport.Options.Simulation!.PacketLoss);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void KcpRejectsInvalidNestedSettings(int invalid)
    {
        KcpTransportOptions options = new();
        switch (invalid)
        {
            case 0: options.Timeout = TimeSpan.Zero; break;
            case 1: options.Reliability.ReceiveWindow = uint.MaxValue; break;
            case 2: options.Reliability.MaxRetransmits = 0; break;
            case 3: options.Simulation = new() { PacketLoss = double.NaN }; break;
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new KcpTransport(options));
    }
}
