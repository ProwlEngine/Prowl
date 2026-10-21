// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
/// Settings for a <see cref="KcpTransport"/>. Both ends of a connection must agree on
/// <see cref="ProtocolId"/>, <see cref="Mtu"/> and the <see cref="Reliability"/> window sizes.
/// </summary>
public sealed class KcpTransportOptions
{
    /// <summary>
    /// The port <see cref="Transport.Listen"/> binds, and the default port for addresses passed to
    /// <see cref="Transport.Connect"/> that don't name one. Listening on 0 picks a free port; see
    /// <see cref="KcpTransport.LocalPort"/>.
    /// </summary>
    public int Port { get; set; } = 7777;

    /// <summary>
    /// Identifies your game and protocol version. Peers with a different id are ignored, so change it
    /// whenever your messages change incompatibly.
    /// </summary>
    public uint ProtocolId { get; set; } = 0x50524157; // "WARP"

    /// <summary>The most incoming connections accepted at once. Further attempts are rejected with <see cref="DisconnectReason.ServerFull"/>.</summary>
    public int MaxConnections { get; set; } = 64;

    /// <summary>
    /// Largest UDP payload sent, in bytes. 1200 fits through practically every network path without
    /// fragmenting at the IP layer.
    /// </summary>
    public int Mtu { get; set; } = 1200;

    /// <summary>How long to keep trying to reach a host before giving up with <see cref="DisconnectReason.Timeout"/>.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a connection may go without hearing from the remote end before it times out.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often each connection pings the remote end, which keeps it alive and measures round trip time.</summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often handshake packets are resent while connecting.</summary>
    public TimeSpan HandshakeResendInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Tuning for the reliable channel.</summary>
    public KcpReliabilityOptions Reliability { get; set; } = new();

    /// <summary>
    /// When set, outgoing packets are dropped and delayed to imitate a bad network. For testing only;
    /// set it on both ends to affect both directions.
    /// </summary>
    public LinkSimulation? Simulation { get; set; }

    /// <summary>OS send and receive buffer size for the socket, in bytes.</summary>
    public int SocketBufferSize { get; set; } = 1024 * 1024;

    /// <summary>Maximum UDP datagrams processed by one Receive call, including invalid packets.</summary>
    public int MaxDatagramsPerReceive { get; set; } = 1024;

    /// <summary>Conservative memory budget for queued and unacknowledged reliable segments per peer.</summary>
    public long MaxPendingReliableBytes { get; set; } = 32 * 1024 * 1024;

    internal void Validate()
    {
        if (Port < 0 || Port > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 0 and 65535.");
        if (Mtu < KcpPacket.SessionHeaderSize + 64 || Mtu > KcpPacket.MaxDatagramSize)
            throw new ArgumentOutOfRangeException(nameof(Mtu), Mtu, $"MTU must be between {KcpPacket.SessionHeaderSize + 64} and {KcpPacket.MaxDatagramSize}.");
        if (MaxConnections < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConnections), MaxConnections, "MaxConnections cannot be negative.");
        if (MaxDatagramsPerReceive <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxDatagramsPerReceive));
        if (MaxPendingReliableBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxPendingReliableBytes));
        ArgumentNullException.ThrowIfNull(Reliability);
    }
}

/// <summary>
/// Tuning for the KCP protocol that carries <see cref="Delivery.Reliable"/> messages. The defaults are
/// KCP's "turbo" profile, which spends some bandwidth on retransmits to keep latency low, as games want.
/// </summary>
public sealed class KcpReliabilityOptions
{
    /// <summary>Resend sooner, with a lower minimum retransmit timeout.</summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>KCP's internal update interval in milliseconds (10 to 5000).</summary>
    public uint Interval { get; set; } = 10;

    /// <summary>Resend a segment once this many later segments have been acknowledged before it. 0 turns fast resend off.</summary>
    public int FastResend { get; set; } = 2;

    /// <summary>
    /// TCP-style congestion control. It shrinks the window drastically on loss, which hurts latency far
    /// more than games can usually afford, so it is off by default.
    /// </summary>
    public bool CongestionControl { get; set; }

    /// <summary>Send window, in segments.</summary>
    public uint SendWindow { get; set; } = 4096;

    /// <summary>
    /// Receive window, in segments. Also caps the largest reliable message, which can span at most
    /// this many segments (and never more than 255).
    /// </summary>
    public uint ReceiveWindow { get; set; } = 4096;

    /// <summary>How many times one segment may be resent without being acknowledged before the connection is considered dead.</summary>
    public uint MaxRetransmits { get; set; } = 40;
}

/// <summary>
/// Artificial network conditions applied to the packets a <see cref="KcpTransport"/> sends.
/// </summary>
public sealed class LinkSimulation
{
    /// <summary>Chance, from 0 to 1, that a packet is dropped.</summary>
    public double PacketLoss { get; set; }

    /// <summary>Delay added to every packet.</summary>
    public TimeSpan Latency { get; set; }

    /// <summary>Up to this much extra random delay per packet. Packets may arrive out of order as a result.</summary>
    public TimeSpan Jitter { get; set; }

    /// <summary>Seed for the random drops and jitter, for reproducible runs. Null picks one at random.</summary>
    public int? Seed { get; set; }
}
