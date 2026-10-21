// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
/// The first byte of every datagram a <see cref="KcpTransport"/> sends.
/// </summary>
/// <remarks>
/// <para>Wire format, all integers little-endian:</para>
/// <code>
/// Handshake (before a session exists)
///   ConnectRequest     [type][u32 protocolId][u32 nonce]                 padded to 64 bytes
///   Challenge          [type][u32 nonce][16 byte cookie]                 21 bytes
///   ChallengeResponse  [type][u32 protocolId][u32 nonce][16 byte cookie] padded to 64 bytes
///   Accept             [type][u32 nonce][u32 sessionId]                  9 bytes
///   Reject             [type][u32 nonce][u8 DisconnectReason]            6 bytes
///
/// Session (after Accept)
///   [type][u32 sessionId][body]
///   Reliable           body = KCP segments
///   Unreliable         body = one message
///   Ping               body = u32 sender time in ms
///   Pong               body = u32 time echoed from the Ping
///   Disconnect         no body
/// </code>
/// <para>
/// The handshake is a stateless cookie exchange, so a host allocates nothing for a client until the
/// client proves it can receive at its claimed address. Requests are padded to be larger than any
/// reply, so the host can never be used to amplify traffic towards a spoofed address. The random
/// session id then tags every packet, so packets injected without seeing the handshake are dropped.
/// </para>
/// </remarks>
internal enum KcpPacketType : byte
{
    ConnectRequest = 1,
    Challenge = 2,
    ChallengeResponse = 3,
    Accept = 4,
    Reject = 5,

    Reliable = 16,
    Unreliable = 17,
    Ping = 18,
    Pong = 19,
    Disconnect = 20,
    BulkReliable = 21,
}

internal static class KcpPacket
{
    /// <summary>Largest payload a single UDP datagram over IPv4 can carry.</summary>
    public const int MaxDatagramSize = 65507;

    /// <summary>Type byte plus session id, in front of every session packet.</summary>
    public const int SessionHeaderSize = 5;

    public const int CookieSize = 16;

    /// <summary>Size of ConnectRequest and ChallengeResponse, which must exceed every reply.</summary>
    public const int HandshakeRequestSize = 64;

    public const int ChallengeSize = 1 + 4 + CookieSize;
    public const int AcceptSize = 1 + 4 + 4;
    public const int RejectSize = 1 + 4 + 1;
    public const int PingSize = SessionHeaderSize + 4;
}
