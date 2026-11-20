using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public class TickCounter : Component, INetworkTick
{
    [SerializeIgnore] public readonly List<uint> Ticks = new();
    [SerializeIgnore] public readonly List<bool> StartedFirst = new();
    private bool _started;

    public override void Start() => _started = true;

    public void OnNetworkTick(uint tick)
    {
        Ticks.Add(tick);
        StartedFirst.Add(_started);
    }
}

public partial class NetworkingTests
{
    private static void WorldFrame(SceneInstance world, params FakePeer[] others)
    {
        Networking.Networking.Receive();
        world.Tick(1f / 60f);
        Networking.Networking.Flush();
        foreach (FakePeer other in others)
            other.Session.Update();
    }

    private static (uint Tick, double Time, float Rate) ReadTime(byte[] data)
    {
        NetReader reader = new(data);
        return (reader.ReadVarUInt32(), reader.ReadDouble(), reader.ReadSingle());
    }

    private static void SendTime(FakePeer host, uint tick, double time, float rate)
    {
        foreach (NetPeer peer in host.Session.Peers.Where(p => !p.IsLocal))
        {
            using NetWriter message = new();
            message.WriteVarUInt32(tick);
            message.WriteDouble(time);
            message.WriteSingle(rate);
            host.Session.SendRaw(peer, FakePeer.Time, message.WrittenSpan, Delivery.Unreliable);
        }
    }

    private static FakePeer JoinFakeHost(SceneInstance level)
    {
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(level, snapshot);
        byte[] payload = snapshot.WrittenSpan.ToArray();
        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, payload);
        Networking.Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        return host;
    }

    [Fact]
    public void TheHostTicksOnceForEachTickRateWorthOfFixedSteps()
    {
        float fixedDelta = Time.FixedDeltaTime;
        Time.FixedDeltaTime = 1f / 60f;
        TickCounter counter = null!;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject clock = CreateGameObject("Clock");
            counter = clock.AddComponent<TickCounter>();
            world.Add(clock);
        });
        try
        {
            Assert.Equal(2.0 / 60.0, Networking.Networking.TickInterval, 6);
            for (int i = 0; i < 4; i++)
                WorldFrame(session.World, session.Alice, session.Bob);
            uint start = Networking.Networking.Tick;
            counter.Ticks.Clear();

            for (int i = 0; i < 8; i++)
                WorldFrame(session.World, session.Alice, session.Bob);

            Assert.Equal(start + 4, Networking.Networking.Tick);
            Assert.Equal([start + 1, start + 2, start + 3, start + 4], counter.Ticks);
            Assert.Equal(Networking.Networking.Tick * Networking.Networking.TickInterval, Networking.Networking.ServerTime,
                Networking.Networking.TickInterval);
        }
        finally
        {
            Time.FixedDeltaTime = fixedDelta;
        }
    }

    [Fact]
    public void APausedWorldHoldsTheTickAndTellsClientsTimeStopped()
    {
        TickCounter counter = null!;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject clock = CreateGameObject("Clock");
            counter = clock.AddComponent<TickCounter>();
            world.Add(clock);
        });
        WorldFrame(session.World, session.Alice, session.Bob);
        session.World.Paused = true;
        WorldFrame(session.World, session.Alice, session.Bob);
        uint start = Networking.Networking.Tick;
        counter.Ticks.Clear();
        int times = session.Alice.TimesReceived.Count;

        for (int i = 0; i < 10; i++)
        {
            WorldFrame(session.World, session.Alice, session.Bob);
            Thread.Sleep(5);
        }

        Assert.Equal(start, Networking.Networking.Tick);
        Assert.Empty(counter.Ticks);
        Assert.True(session.Alice.TimesReceived.Count > times);
        Assert.Equal(0f, ReadTime(session.Alice.TimesReceived[^1]).Rate);
    }

    [Fact]
    public void APausedWorldNobodyTicksHoldsTheTickToo()
    {
        TickCounter counter = null!;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject clock = CreateGameObject("Clock");
            counter = clock.AddComponent<TickCounter>();
            world.Add(clock);
        });
        session.World.Paused = true;
        uint start = Networking.Networking.Tick;
        counter.Ticks.Clear();
        PumpFor(200, session.Alice, session.Bob);

        Assert.Equal(start, Networking.Networking.Tick);
        Assert.Empty(counter.Ticks);
    }

    [Fact]
    public void AComponentStartsBeforeItsFirstNetworkTick()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject clock = CreateGameObject("Clock");
        TickCounter counter = clock.AddComponent<TickCounter>();
        session.World.Add(clock);
        Pump(() => counter.Ticks.Count > 0, session.Alice, session.Bob);

        Assert.True(counter.StartedFirst[0]);
    }

    [Fact]
    public void AWorldNobodyTicksStillTicksOnTheWallClock()
    {
        TickCounter counter = null!;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject clock = CreateGameObject("Clock");
            counter = clock.AddComponent<TickCounter>();
            world.Add(clock);
        });
        uint start = Networking.Networking.Tick;
        counter.Ticks.Clear();
        PumpFor(300, session.Alice, session.Bob);

        Assert.InRange(Networking.Networking.Tick, start + 3, start + 40);
        Assert.Equal(Enumerable.Range((int)start + 1, counter.Ticks.Count).Select(t => (uint)t), counter.Ticks);
    }

    [Fact]
    public void TheHostSendsClientsItsTickAndTime()
    {
        using VisibilitySession session = VisibilityHost();
        Pump(() => session.Alice.TimesReceived.Count >= 3, session.Alice, session.Bob);

        (uint tick, double time, float rate) = ReadTime(session.Alice.TimesReceived[^1]);
        Assert.Equal(1f, rate);
        Assert.True(tick > 0);
        Assert.True(tick <= Networking.Networking.Tick);
        Assert.InRange(time, tick * Networking.Networking.TickInterval - 1e-6, Networking.Networking.ServerTime + 1e-6);
    }

    [Fact]
    public void AClientEstimatesTheHostsTickAndTime()
    {
        FakePeer host = JoinFakeHost(CreateScene());
        try
        {
            double interval = Networking.Networking.TickInterval;
            uint hostTick = 1000;
            void Frame()
            {
                SendTime(host, hostTick, hostTick * interval, 1);
                hostTick++;
                Thread.Sleep((int)(interval * 1000));
            }

            Pump(() =>
            {
                Frame();
                return Networking.Networking.Tick >= 1005;
            }, host);

            Assert.InRange(Networking.Networking.ServerTime, 1000 * interval, hostTick * interval + 0.5);
            Assert.InRange(Networking.Networking.Tick, 1000u, hostTick + 15);
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }

    [Fact]
    public void AClientWaitsForAHostThatStopped()
    {
        FakePeer host = JoinFakeHost(CreateScene());
        try
        {
            double interval = Networking.Networking.TickInterval;
            uint hostTick = 1000;
            Pump(() =>
            {
                SendTime(host, hostTick, hostTick * interval, 1);
                hostTick++;
                Thread.Sleep((int)(interval * 1000));
                return Networking.Networking.Tick >= 1005;
            }, host);

            hostTick--;
            for (int i = 0; i < 40; i++)
            {
                SendTime(host, hostTick, hostTick * interval, 0);
                PumpFor((int)(interval * 1000), host);
            }

            Assert.InRange(Networking.Networking.Tick, 1005u, hostTick + 2);
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }

    [Fact]
    public void TheClockTracksAHostRunningAtAnotherRate()
    {
        NetworkClock clock = new();
        double previous = 0, worst = 0;
        for (int sample = 0; sample < 600; sample++)
        {
            double wall = sample / 30.0;
            clock.Observe((uint)sample, wall * 0.5, 0.5, wall + 0.025, 0.05);
            for (int frame = 1; frame <= 4; frame++)
            {
                double now = wall + 0.025 + frame / 120.0;
                double estimate = clock.ServerTime(now);
                Assert.True(estimate >= previous);
                previous = estimate;
                worst = Math.Max(worst, Math.Abs(estimate - (now - 0.025) * 0.5));
            }
        }

        Assert.True(worst < 0.02, $"Off by {worst}");

        NetworkClock stopped = new();
        stopped.Observe(10, 5, 0, now: 0, roundTrip: 0.2);
        Assert.Equal(5, stopped.ServerTime(100));
    }

    [Fact]
    public void TheClockFollowsTheHostAndOnlyJumpsBackWhenItStalls()
    {
        NetworkClock clock = new();
        Assert.Equal(0, clock.ServerTime(5));

        clock.Observe(100, 10, 1, now: 50, roundTrip: 0.1);
        Assert.Equal(10.05, clock.ServerTime(50), 6);
        Assert.Equal(11.05, clock.ServerTime(51), 6);
        Assert.Equal(131u, clock.Tick(51, 1 / 30.0));

        clock.Observe(130, 11.0, 1, now: 51.08, roundTrip: 0.1);
        double late = clock.ServerTime(51.08);
        Assert.InRange(late, 11.12, 11.14);
        Assert.True(clock.ServerTime(51.09) >= late);

        clock.Observe(131, 11.0333, 1, now: 60, roundTrip: 0.1);
        Assert.Equal(11.0833, clock.ServerTime(60), 3);
    }
}
