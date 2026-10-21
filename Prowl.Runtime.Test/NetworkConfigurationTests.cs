using Prowl.Runtime.Networking;
using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkConfigurationTests : RuntimeTestBase
{
    [Fact]
    public void DefaultUdpTransportUsesNetworkingWorker()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        try
        {
            Networking.Networking.Configure(new() { Port = 0 });
            using Transport transport = Networking.Networking.CreateTransport(true);
            Assert.IsType<Prowl.Warp.Transports.Batching.BatchingTransport>(transport);
            using NetSession session = Networking.Networking.Host(transport);
            Assert.IsType<NetworkingThread>(session.Transport);
            Assert.NotEqual(0, session.Transport.LocalPort);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void ConfigurationNormalizesInvalidValuesAndCreatesIndependentOptions()
    {
        var config = new NetworkConfiguration { TickRate = float.NaN, InterpolationMilliseconds = -1,
            Port = 90000, MaxPeers = -5, Mtu = 100, PacketLossPercent = 150, LatencyMilliseconds = float.PositiveInfinity,
            SimulateNetwork = true, UpdateIntervalMilliseconds = -1 };
        NetworkConfiguration normalized = config.ValidatedCopy();
        Assert.Equal(30, normalized.TickRate);
        Assert.Equal(0, normalized.InterpolationMilliseconds);
        Assert.Equal(65535, normalized.Port);
        Assert.Equal(1, normalized.MaxPeers);
        Assert.Equal(1200, normalized.Mtu);
        Assert.Equal(100, normalized.PacketLossPercent);
        Assert.Equal(0, normalized.LatencyMilliseconds);
        KcpTransportOptions first = config.CreateKcpOptions();
        KcpTransportOptions second = config.CreateKcpOptions();
        Assert.Equal(1, first.Simulation!.PacketLoss);
        Assert.Equal(10u, first.Reliability.Interval);
        first.Reliability.SendWindow = 1;
        Assert.Equal(4096u, second.Reliability.SendWindow);
    }

    [Fact]
    public void ConfiguredHostUsesDefaultsAndDoesNotChangeExistingSessionOptions()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        try
        {
            var config = new NetworkConfiguration { Transport = NetworkTransport.Local, LocalAddress = "configured",
                TickRate = 20, InterpolationMilliseconds = 75, MaxPeers = 3, AppVersion = 17 };
            Networking.Networking.Configure(config);
            config.MaxPeers = 99;
            NetSession session = Networking.Networking.Host();
            Assert.IsType<LocalTransport>(session.Transport);
            Assert.Equal(3, session.Options.MaxPeers);
            Assert.Equal(17u, session.Options.AppVersion);
            Assert.Equal(20, Networking.Networking.TickRate);
            Assert.Equal(75, Networking.Networking.InterpolationDelay.TotalMilliseconds);
            NetworkConfiguration changed = Networking.Networking.Configuration;
            changed.MaxPeers = 8;
            Networking.Networking.Configure(changed);
            Assert.Equal(3, session.Options.MaxPeers);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void ConfiguredClientUsesDefaultDestinationPortAndMatchingProtocolAndVersion()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        var config = new NetworkConfiguration { Port = 0, ProtocolId = 1234, AppVersion = 12,
            BatchMessages = false, UseNetworkThread = false };
        using var transport = new KcpTransport(config.CreateKcpOptions());
        using NetSession host = NetSession.Host(transport, options: config.CreateSessionOptions());
        config.Port = transport.LocalPort;
        try
        {
            Networking.Networking.Configure(config);
            NetSession client = Networking.Networking.Join("127.0.0.1", "configured-client");
            for (int i = 0; i < 1000 && !Networking.Networking.IsActive; i++)
            {
                host.Update();
                Networking.Networking.Receive();
                Networking.Networking.Flush();
                Thread.Sleep(1);
            }
            Assert.True(Networking.Networking.IsActive);
            Assert.Equal(12u, client.Options.AppVersion);
            Assert.NotEqual(transport.LocalPort, ((KcpTransport)client.Transport).LocalPort);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConfiguredUdpModesCanJoinAndExchangeMessages(bool batching, bool threaded)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        try
        {
            var config = new NetworkConfiguration { Port = 0, BatchMessages = batching, UseNetworkThread = threaded };
            Networking.Networking.Configure(config);
            using Transport transport = Networking.Networking.CreateTransport(true);
            using NetSession host = NetSession.Host(transport);
            config.Port = transport.LocalPort;
            Networking.Networking.Configure(config);
            NetSession client = Networking.Networking.Join("127.0.0.1", "client");
            bool received = false;
            host.OnRaw(20, (NetPeer peer, ref NetReader reader, Delivery delivery) => received = reader.ReadByte() == 42);
            for (int i = 0; i < 2000 && client.State != SessionState.Active; i++)
            {
                host.Update(); Networking.Networking.Receive(); Networking.Networking.Flush(); Thread.Sleep(1);
            }
            Assert.Equal(SessionState.Active, client.State);
            client.SendRaw(client.HostPeer!, 20, new byte[] { 42 }, flags: SendFlags.Immediate);
            for (int i = 0; i < 2000 && !received; i++)
            {
                host.Update(); Networking.Networking.Receive(); Networking.Networking.Flush(); Thread.Sleep(1);
            }
            Assert.True(received);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }
}
