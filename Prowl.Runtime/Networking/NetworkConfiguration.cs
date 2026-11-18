using System;

using Prowl.Courier;
using Prowl.Courier.Transports.Kcp;

namespace Prowl.Runtime.Networking;

/// <summary>Serializable project defaults. Explicit transports and session options can override these per session.</summary>
public sealed class NetworkConfiguration
{
    /// <summary>The game's version. Sessions mix in a hash of every networked member, so builds that differ there can't join either.</summary>
    public uint AppVersion;
    public bool BatchMessages = true;
    public bool CongestionControl;
    public float ConnectTimeoutSeconds = 5;

    /// <summary>Encrypts and authenticates KCP traffic. Hosts and clients must agree.</summary>
    public bool Encryption;

    /// <summary>
    ///     Base64 public key of the host's <see cref="Networking.HostSigningKey" />. When set, encrypted clients refuse
    ///     a host that can't prove it holds that key.
    /// </summary>
    public string TrustedHostKey = "";
    public NetworkExecution Execution = NetworkExecution.Auto;
    public int FastResend = 2;

    /// <summary>The least interpolation delay. The buffer grows past it with the send interval and the measured jitter.</summary>
    public float InterpolationMilliseconds = 100;

    /// <summary>Whether clients receive only the objects near their viewpoints.</summary>
    public InterestMode InterestMode = InterestMode.None;

    internal const float MinInterestRadius = 0.01f;

    /// <summary>How far a viewpoint sees, unless its NetworkObserver or the object's CullDistance says otherwise.</summary>
    public float InterestRadius = 100;

    /// <summary>Ticks between checks of who is near what. Each client is checked on a different tick.</summary>
    public int InterestUpdateTicks = 6;

    /// <summary>How much further than its range an object must go before a client loses it, as a fraction of that range.</summary>
    public float InterestHysteresis = 0.15f;

    /// <summary>Seconds a client keeps an object after it was last in range, before distance takes it away.</summary>
    public float InterestLingerSeconds = 1;

    public float JitterMilliseconds;
    public float JoinTimeoutSeconds = 10;
    public float LatencyMilliseconds;
    public string LocalAddress = "host";
    public int MaxMessageSize = 16 * 1024 * 1024;

    /// <summary>Largest message the host takes from a client. A client sending more is dropped.</summary>
    public int MaxClientMessageSize = 256 * 1024;

    /// <summary>Messages a second the host takes from each client, with a second's worth of burst, or 0 for no limit.</summary>
    public int MaxClientMessagesPerSecond = 1000;
    public int MaxPeers = 64;
    public int MaxRetransmits = 40;

    /// <summary>Most bytes per second each KCP connection sends, or 0 for no limit.</summary>
    public int MaxSendRate;

    /// <summary>Maximum decompressed Echo bytes in one scene snapshot or spawned object.</summary>
    public int MaxSnapshotDecompressedBytes = 64 * 1024 * 1024;

    /// <summary>Maximum Echo value nesting, including the root, before engine objects are created.</summary>
    public int MaxSnapshotDepth = 128;

    /// <summary>Maximum Echo values, including container and scalar values, in one snapshot or spawn.</summary>
    public int MaxSnapshotNodes = 250_000;

    public int Mtu = 1200;
    public bool NoDelay = true;
    public float PacketLossPercent;
    public float PingIntervalSeconds = 1;
    public int Port = 7777;
    public uint ProtocolId = 0x50524157;
    public int ReceiveWindow = 4096;
    public float SceneLoadTimeoutSeconds = 60;
    public int SendWindow = 4096;
    public bool SimulateNetwork;
    public float TickRate = 30;

    internal const float DefaultPositionPrecision = 0.01f;

    /// <summary>Grid size, in world units, that moving positions snap to on the wire. Host and clients must match.</summary>
    public float PositionPrecision = DefaultPositionPrecision;

    /// <summary>Local position change from the last sent pose required to resume transform updates.</summary>
    public float TransformPositionThreshold = 0.001f;

    /// <summary>Rotation change in degrees from the last sent pose required to resume transform updates.</summary>
    public float TransformRotationThresholdDegrees = 0.1f;

    /// <summary>Local scale change from the last sent pose required to resume transform updates.</summary>
    public float TransformScaleThreshold = 0.0001f;
    public float TimeoutSeconds = 10;
    public NetworkTransport Transport = NetworkTransport.Kcp;

    public int UpdateIntervalMilliseconds = 10;

    /// <summary>Returns an independent, normalized copy so editing project defaults cannot mutate a running session.</summary>
    public NetworkConfiguration ValidatedCopy()
    {
        NetworkConfiguration copy = (NetworkConfiguration)MemberwiseClone();
        if (!Enum.IsDefined(copy.Transport))
        {
            copy.Transport = NetworkTransport.Kcp;
        }

        if (!Enum.IsDefined(copy.Execution))
        {
            copy.Execution = NetworkExecution.Auto;
        }

        if (!Enum.IsDefined(copy.InterestMode))
        {
            copy.InterestMode = InterestMode.None;
        }

        copy.InterestRadius = Limit(copy.InterestRadius, MinInterestRadius, float.MaxValue, 100);
        copy.InterestUpdateTicks = Math.Clamp(copy.InterestUpdateTicks, 1, 600);
        copy.InterestHysteresis = Limit(copy.InterestHysteresis, 0, 10, 0.15f);
        copy.InterestLingerSeconds = Limit(copy.InterestLingerSeconds, 0, 600, 1);
        copy.Port = Math.Clamp(copy.Port, 0, 65535);
        if (string.IsNullOrWhiteSpace(copy.LocalAddress))
        {
            copy.LocalAddress = "host";
        }

        copy.TickRate = Limit(copy.TickRate, 1, 240, 30);
        copy.InterpolationMilliseconds = Limit(copy.InterpolationMilliseconds, 0, 5000, 100);
        copy.PositionPrecision = float.IsFinite(copy.PositionPrecision) && copy.PositionPrecision > 0
            ? Math.Clamp(copy.PositionPrecision, 1e-6f, 1000)
            : DefaultPositionPrecision;

        copy.TransformPositionThreshold = Limit(copy.TransformPositionThreshold, 0, float.MaxValue, 0.001f);
        copy.TransformRotationThresholdDegrees = Limit(copy.TransformRotationThresholdDegrees, 0, 180, 0.1f);
        copy.TransformScaleThreshold = Limit(copy.TransformScaleThreshold, 0, float.MaxValue, 0.0001f);
        copy.MaxPeers = Math.Clamp(copy.MaxPeers, 1, 4096);
        copy.MaxMessageSize = Math.Clamp(copy.MaxMessageSize, 1024, 1024 * 1024 * 1024);
        copy.MaxClientMessageSize = Math.Clamp(copy.MaxClientMessageSize, 1024, copy.MaxMessageSize);
        copy.MaxClientMessagesPerSecond = Math.Max(0, copy.MaxClientMessagesPerSecond);
        copy.MaxSnapshotDecompressedBytes = Math.Clamp(copy.MaxSnapshotDecompressedBytes, 1, 1024 * 1024 * 1024);
        copy.MaxSnapshotDepth = Math.Clamp(copy.MaxSnapshotDepth, 1, 256);
        copy.MaxSnapshotNodes = Math.Clamp(copy.MaxSnapshotNodes, 1, 1_000_000);
        copy.SceneLoadTimeoutSeconds = Limit(copy.SceneLoadTimeoutSeconds, 0.1f, 600, 60);
        copy.JoinTimeoutSeconds = Limit(copy.JoinTimeoutSeconds, 0.1f, 300, 10);
        copy.ConnectTimeoutSeconds = Limit(copy.ConnectTimeoutSeconds, 0.1f, 300, 5);
        copy.TimeoutSeconds = Limit(copy.TimeoutSeconds, 0.1f, 300, 10);
        copy.PingIntervalSeconds = Limit(copy.PingIntervalSeconds, 0.05f, 60, 1);
        // Keep the conservative default IP packet size; custom transports may use smaller payloads.
        copy.Mtu = Math.Clamp(copy.Mtu, 1200, 65507);
        copy.MaxSendRate = copy.MaxSendRate <= 0 ? 0 : Math.Max(copy.MaxSendRate, copy.Mtu);
        copy.TrustedHostKey ??= "";
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

    internal bool UsesWorker(Transport transport)
    {
        TransportCapabilities capabilities = transport.Capabilities;
        if (!capabilities.IsAvailable)
        {
            throw new NotSupportedException("The selected transport is unavailable on this platform.");
        }

        if (!capabilities.Supports(Delivery.Reliable))
        {
            throw new NotSupportedException("Sessions require reliable delivery.");
        }

        if (Execution == NetworkExecution.DedicatedThread &&
            capabilities.Execution == TransportExecution.CreatingThreadRequired)
        {
            throw new NotSupportedException("The selected transport must run on its creating thread.");
        }

        return Execution == NetworkExecution.DedicatedThread ||
               (Execution == NetworkExecution.Auto &&
                capabilities.Execution == TransportExecution.WorkerPreferred);
    }

    private static float Limit(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public NetSessionOptions CreateSessionOptions()
    {
        NetworkConfiguration c = ValidatedCopy();
        return new NetSessionOptions
        {
            AppVersion = c.AppVersion,
            BatchMessages = c.BatchMessages,
            MaxPeers = c.MaxPeers,
            MaxMessageSize = c.MaxMessageSize,
            JoinTimeout = TimeSpan.FromSeconds(c.JoinTimeoutSeconds)
        };
    }

    public KcpTransportOptions CreateKcpOptions()
    {
        NetworkConfiguration c = ValidatedCopy();
        return new KcpTransportOptions
        {
            Port = c.Port,
            ProtocolId = c.ProtocolId,
            MaxConnections = c.MaxPeers,
            Mtu = c.Mtu,
            ConnectTimeout = TimeSpan.FromSeconds(c.ConnectTimeoutSeconds),
            Timeout = TimeSpan.FromSeconds(c.TimeoutSeconds),
            PingInterval = TimeSpan.FromSeconds(c.PingIntervalSeconds),
            MaxSendRate = c.MaxSendRate,
            Encryption = c.Encryption
                ? new KcpEncryption
                {
                    TrustedHostKey = string.IsNullOrWhiteSpace(c.TrustedHostKey)
                        ? null
                        : Convert.FromBase64String(c.TrustedHostKey)
                }
                : null,
            Reliability =
                new KcpReliabilityOptions
                {
                    NoDelay = c.NoDelay,
                    Interval = (uint)c.UpdateIntervalMilliseconds,
                    FastResend = c.FastResend,
                    CongestionControl = c.CongestionControl,
                    SendWindow = (uint)c.SendWindow,
                    ReceiveWindow = (uint)c.ReceiveWindow,
                    MaxRetransmits = (uint)c.MaxRetransmits
                },
            Simulation = c.SimulateNetwork
                ? new LinkSimulation
                {
                    PacketLoss = c.PacketLossPercent / 100.0,
                    Latency = TimeSpan.FromMilliseconds(c.LatencyMilliseconds),
                    Jitter = TimeSpan.FromMilliseconds(c.JitterMilliseconds)
                }
                : null
        };
    }
}
