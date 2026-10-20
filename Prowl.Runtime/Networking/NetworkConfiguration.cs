using System;

using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;
using Prowl.Warp.Transports.Steam;

namespace Prowl.Runtime.Networking;

public enum NetworkTransport { Kcp, Local, Steam }

/// <summary>Serializable project defaults. Explicit transports and session options can override these per session.</summary>
public sealed class NetworkConfiguration
{
    public NetworkTransport Transport = NetworkTransport.Kcp;
    public int Port = 7777;
    public string LocalAddress = "host";
    public float TickRate = 30;
    public float InterpolationMilliseconds = 100;
    public uint ProtocolId = 0x50524157;
    public uint AppVersion;
    public int MaxPeers = 64;
    public int MaxMessageSize = 16 * 1024 * 1024;
    public float JoinTimeoutSeconds = 10;
    public float ConnectTimeoutSeconds = 5;
    public float TimeoutSeconds = 10;
    public float PingIntervalSeconds = 1;
    public int Mtu = 1200;
    public bool NoDelay = true;
    public int UpdateIntervalMilliseconds = 10;
    public int FastResend = 2;
    public bool CongestionControl;
    public int SendWindow = 4096;
    public int ReceiveWindow = 4096;
    public int MaxRetransmits = 40;
    public bool SimulateNetwork;
    public float PacketLossPercent;
    public float LatencyMilliseconds;
    public float JitterMilliseconds;

    /// <summary>Returns an independent, normalized copy so editing project defaults cannot mutate a running session.</summary>
    public NetworkConfiguration ValidatedCopy()
    {
        var copy = (NetworkConfiguration)MemberwiseClone();
        if (!Enum.IsDefined(copy.Transport)) copy.Transport = NetworkTransport.Kcp;
        copy.Port = Math.Clamp(copy.Port, 0, 65535);
        if (string.IsNullOrWhiteSpace(copy.LocalAddress)) copy.LocalAddress = "host";
        copy.TickRate = Limit(copy.TickRate, 1, 240, 30);
        copy.InterpolationMilliseconds = Limit(copy.InterpolationMilliseconds, 0, 5000, 100);
        copy.MaxPeers = Math.Clamp(copy.MaxPeers, 1, 4096);
        copy.MaxMessageSize = Math.Clamp(copy.MaxMessageSize, 1024, 1024 * 1024 * 1024);
        copy.JoinTimeoutSeconds = Limit(copy.JoinTimeoutSeconds, 0.1f, 300, 10);
        copy.ConnectTimeoutSeconds = Limit(copy.ConnectTimeoutSeconds, 0.1f, 300, 5);
        copy.TimeoutSeconds = Limit(copy.TimeoutSeconds, 0.1f, 300, 10);
        copy.PingIntervalSeconds = Limit(copy.PingIntervalSeconds, 0.05f, 60, 1);
        // Engine transform batches need at least 1000 bytes of unreliable payload.
        copy.Mtu = Math.Clamp(copy.Mtu, 1200, 65507);
        copy.UpdateIntervalMilliseconds = Math.Clamp(copy.UpdateIntervalMilliseconds, 10, 5000);
        copy.FastResend = Math.Clamp(copy.FastResend, 0, 255);
        copy.SendWindow = Math.Clamp(copy.SendWindow, 1, 65535);
        copy.ReceiveWindow = Math.Clamp(copy.ReceiveWindow, 128, 65535);
        copy.MaxRetransmits = Math.Clamp(copy.MaxRetransmits, 1, 1000);
        copy.PacketLossPercent = Limit(copy.PacketLossPercent, 0, 100, 0);
        copy.LatencyMilliseconds = Limit(copy.LatencyMilliseconds, 0, 10000, 0);
        copy.JitterMilliseconds = Limit(copy.JitterMilliseconds, 0, 10000, 0);
        return copy;
    }

    private static float Limit(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public NetSessionOptions CreateSessionOptions()
    {
        NetworkConfiguration c = ValidatedCopy();
        return new() { AppVersion = c.AppVersion, MaxPeers = c.MaxPeers, MaxMessageSize = c.MaxMessageSize,
            JoinTimeout = TimeSpan.FromSeconds(c.JoinTimeoutSeconds) };
    }

    public KcpTransportOptions CreateKcpOptions()
    {
        NetworkConfiguration c = ValidatedCopy();
        return new()
        {
            Port = c.Port, ProtocolId = c.ProtocolId, MaxConnections = c.MaxPeers, Mtu = c.Mtu,
            ConnectTimeout = TimeSpan.FromSeconds(c.ConnectTimeoutSeconds), Timeout = TimeSpan.FromSeconds(c.TimeoutSeconds),
            PingInterval = TimeSpan.FromSeconds(c.PingIntervalSeconds),
            Reliability = new() { NoDelay = c.NoDelay, Interval = (uint)c.UpdateIntervalMilliseconds,
                FastResend = c.FastResend, CongestionControl = c.CongestionControl, SendWindow = (uint)c.SendWindow,
                ReceiveWindow = (uint)c.ReceiveWindow, MaxRetransmits = (uint)c.MaxRetransmits },
            Simulation = c.SimulateNetwork ? new LinkSimulation { PacketLoss = c.PacketLossPercent / 100.0,
                Latency = TimeSpan.FromMilliseconds(c.LatencyMilliseconds), Jitter = TimeSpan.FromMilliseconds(c.JitterMilliseconds) } : null,
        };
    }
}

public static partial class Networking
{
    private static NetworkConfiguration s_configuration = new();
    private static readonly LocalNetwork s_localNetwork = new();

    /// <summary>A copy of the configured project defaults.</summary>
    public static NetworkConfiguration Configuration => s_configuration.ValidatedCopy();

    /// <summary>Applies replication timing now; transport and session options apply to subsequent sessions.</summary>
    public static void Configure(NetworkConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        s_configuration = configuration.ValidatedCopy();
        TickRate = s_configuration.TickRate;
        InterpolationDelay = TimeSpan.FromMilliseconds(s_configuration.InterpolationMilliseconds);
    }

    /// <summary>Creates a transport using project defaults. Local transports share a network within this process only.</summary>
    public static Transport CreateTransport(bool hosting) => s_configuration.Transport switch
    {
        NetworkTransport.Local => new LocalTransport(s_localNetwork, hosting ? s_configuration.LocalAddress : Guid.NewGuid().ToString()),
        NetworkTransport.Steam => new SteamTransport(),
        _ => new KcpTransport(s_configuration.CreateKcpOptions()),
    };

    /// <summary>Hosts using the project's transport and session defaults.</summary>
    public static NetSession Host(string name = "Host")
    {
        BeginSession();
        Transport transport = CreateTransport(hosting: true);
        try { return Host(transport, name); }
        catch { transport.Dispose(); throw; }
    }

    /// <summary>Joins using the project's transport and session defaults. KCP addresses may include a port.</summary>
    public static NetSession Join(string address, string name, ReadOnlySpan<byte> token = default)
    {
        BeginSession();
        Transport transport = CreateTransport(hosting: false);
        try { return Join(transport, address, name, token); }
        catch { transport.Dispose(); throw; }
    }
}
