using Prowl.Wicked;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked.Tests;

public class TimeSyncTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public TimeSyncTests()
    {
        _serverTransport = new InMemoryServerTransport();
        _clientTransport = new InMemoryClientTransport(_serverTransport);

        Server.Transport = _serverTransport;
        Client.Transport = _clientTransport;
    }

    public void Dispose()
    {
        if (Server.Active) Server.Stop();
        Server.Reset();
        Client.Reset();
    }

    private void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            Server.Tick();
            Client.Tick();
        }
    }

    [Fact]
    public void ServerTimeIncreasesAfterStart()
    {
        Server.Start(0);
        Tick();
        Thread.Sleep(10);

        Assert.True(Server.Time > 0);
    }

    [Fact]
    public void PongUpdatesRoundTripTime()
    {
        Server.Start(0);
        Client.PingInterval = 0f;
        Client.Connect("localhost", 0);
        Tick(); // client sends ping, server receives
        Tick(); // server sends pong, client receives

        Assert.True(Client.RoundTripTime > 0);
    }

    [Fact]
    public void ServerTimeIsNonZeroAfterPong()
    {
        Server.Start(0);
        Client.PingInterval = 0f;
        Client.Connect("localhost", 0);
        Tick();
        Tick();

        Assert.True(Client.ServerTime > 0);
    }

    [Fact]
    public void ServerTimeApproximatesServerClock()
    {
        Server.Start(0);
        Client.PingInterval = 0f;
        Client.Connect("localhost", 0);
        Tick();
        Tick();

        // With InMemoryTransport (zero network latency), the two clocks should
        // be within a few milliseconds of each other.
        double diff = Math.Abs(Client.ServerTime - Server.Time);
        Assert.True(diff < 0.05, $"Client.ServerTime ({Client.ServerTime:F6}) and Server.Time ({Server.Time:F6}) differ by {diff:F6}");
    }

    [Fact]
    public void StandardDeviationStartsAtZero()
    {
        Server.Start(0);
        Client.PingInterval = 0f;
        Client.Connect("localhost", 0);
        Tick();
        Tick();

        Assert.Equal(0.0, Client.StandardDeviation);
    }

    [Fact]
    public void MultiplePingsUpdateRtt()
    {
        Server.Start(0);
        Client.PingInterval = 0.001f;
        Client.Connect("localhost", 0);

        // Capture RTT after first exchange
        Tick();
        Tick();
        double firstRtt = Client.RoundTripTime;
        Assert.True(firstRtt > 0);

        // Run many more exchanges — EMA should stay stable and small
        for (int i = 0; i < 20; i++)
            Tick();

        Assert.True(Client.RoundTripTime > 0);
        // With InMemoryTransport, RTT should be sub-millisecond range
        Assert.True(Client.RoundTripTime < 0.1, $"RTT unexpectedly large: {Client.RoundTripTime}");
        // Standard deviation should be non-negative (may still be ~0 with uniform in-memory latency)
        Assert.True(Client.StandardDeviation >= 0);
    }

    [Fact]
    public void ClientResetClearsTimingState()
    {
        Server.Start(0);
        Client.PingInterval = 0f;
        Client.Connect("localhost", 0);
        Tick();
        Tick();

        Assert.True(Client.RoundTripTime > 0);

        Client.Reset();

        Assert.Equal(0.0, Client.ServerTime);
        Assert.Equal(0.0, Client.RoundTripTime);
        Assert.Equal(0.0, Client.StandardDeviation);
        Assert.Equal(2f, Client.PingInterval);
        Assert.Equal(10, Client.PingWindowSize);
    }

    [Fact]
    public void ServerResetClearsConnectionTimeout()
    {
        Server.ConnectionTimeout = 1f;
        Server.Reset();

        Assert.Equal(6f, Server.ConnectionTimeout);
    }

    [Fact]
    public void ServerTimeIsZeroBeforeFirstPong()
    {
        Server.Start(0);
        Client.PingInterval = 1000f;
        Client.Connect("localhost", 0);

        // Only process the connection — no ping/pong exchange yet
        Server.Tick();

        Assert.Equal(0.0, Client.ServerTime);
        Assert.Equal(0.0, Client.RoundTripTime);
    }

    [Fact]
    public void ServerDisconnectsSilentClient()
    {
        Server.ConnectionTimeout = 0.1f;
        Server.Start(0);
        Client.PingInterval = 1000f; // effectively disable pings
        Client.Connect("localhost", 0);

        // Process connection + drain the initial ping (_pingTimer starts at PingInterval)
        Server.Tick(); // processes client connection
        Client.Tick(); // gets AssignClientId, sends initial ping
        Server.Tick(); // processes the ping → LastReceivedTime updated

        Assert.Single(Server.Clients);

        // Wait past timeout — no more client ticks so no more data
        Thread.Sleep(150);
        Server.Tick(); // timeout check fires → Transport.Disconnect() queued
        Server.Tick(); // processes the queued disconnect

        Assert.Empty(Server.Clients);
    }

    [Fact]
    public void ActiveClientIsNotDisconnected()
    {
        Server.ConnectionTimeout = 0.5f;
        Server.Start(0);
        Client.PingInterval = 0.05f;
        Client.Connect("localhost", 0);

        for (int i = 0; i < 30; i++)
        {
            Tick();
            Thread.Sleep(20);
        }

        Assert.Single(Server.Clients);
        Assert.True(Client.IsConnected);
    }

    [Fact]
    public void ConnectionTimeoutDisabledWhenZero()
    {
        Server.ConnectionTimeout = 0f;
        Server.Start(0);
        Client.PingInterval = 1000f; // effectively disable pings
        Client.Connect("localhost", 0);

        // Process connection + drain initial ping
        Server.Tick();
        Client.Tick();
        Server.Tick();

        Assert.Single(Server.Clients);

        // Wait well past what would be a timeout if it were enabled (e.g. 0.1s)
        // With ConnectionTimeout=0, the check is skipped entirely.
        Thread.Sleep(150);
        Server.Tick(); // would detect timeout if guard was broken
        Server.Tick(); // would process disconnect if guard was broken

        Assert.Single(Server.Clients);
    }
}
