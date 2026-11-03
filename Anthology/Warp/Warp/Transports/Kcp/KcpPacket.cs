// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
///     The first byte of every datagram a <see cref="KcpTransport" /> sends.
/// </summary>
/// <remarks>
///     <para>Wire format, all integers little-endian:</para>
///     <code>
/// Handshake (before a session exists)
///   ConnectRequest     [type][u32 protocolId][u32 nonce][16 zero bytes][settings][4 zero bytes]
///                                                                         padded to 176 bytes
///   Challenge          [type][u32 nonce][u32 issued][16 byte cookie]     25 bytes
///   ChallengeResponse  [type][u32 protocolId][u32 nonce][16 byte cookie][settings][u32 issued][65 byte key]
///                                                                         padded to 176 bytes
///   settings           [u8 wireVersion][u8 channels][u16 mtu][u16 receiveWindow][u8 flags, 1 = encrypted]
///   Accept             [type][u32 nonce][u32 sessionId]                  9 bytes
///                      encrypted: then [65 byte host key][64 byte signature, or zeros][16 byte key confirmation]
///                      [16 byte reset token, or zeros]
///   Reject             [type][u32 nonce][u8 DisconnectReason]            6 bytes
/// 
/// Session (after Accept)
///   [type][u32 sessionId][body]
///   Reliable           body = KCP segments of channel 0
///   ReliableChannel    body = [u8 channel, 1 or more] KCP segments of that channel
///   Unreliable         body = one message
///   UnreliableFramed   body = [u8 flags][u16 sequence][u8 index][u8 count] piece of one message
///                      flags = channel, plus 0x80 if sequenced. Pieces of one message share a sequence.
///   Ping               body = u32 sender time in ms
///   Pong               body = u32 time echoed from the Ping
///   Disconnect         no body
///   PathChallenge      body = 8 random bytes, sent to a new address an encrypted peer's packets came from
///   PathResponse       body = the 8 bytes echoed; only an answer from that address moves the session there
///   Reset              [type][u32 sessionId], plus the session's 16 byte reset token when encrypted. Sent, never
///                      sealed, by a host that has no such session, such as one that restarted.
///   Encrypted, every session packet is [type][u32 sessionId][u64 counter][sealed body][16 byte tag].
/// </code>
///     <para>
///         The handshake is a stateless cookie exchange, so a host allocates nothing for a client until the
///         client proves it can receive at its claimed address. Requests are padded to be larger than any
///         reply, so the host can never be used to amplify traffic towards a spoofed address. The random
///         session id then tags every packet, so packets injected without seeing the handshake are dropped.
///     </para>
///     <para>
///         Each reliable channel has its own KCP conversation, so channels never wait on each other. Channel 0,
///         which carries most traffic, is implied by its packet type rather than spending a byte on it.
///         A host rejects a handshake whose settings differ from its own. The cookie covers the time the host
///         issued it, so a host can tell a restart that began after the current session from a replay of an
///         older handshake.
///     </para>
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
    ReliableChannel = 21,
    UnreliableFramed = 22,
    PathChallenge = 23,
    PathResponse = 24,
    Reset = 25
}

internal static class KcpPacket
{
    /// <summary>Largest payload a single UDP datagram over IPv4 can carry.</summary>
    public const int MaxDatagramSize = 65507;

    /// <summary>Type byte plus session id, in front of every session packet.</summary>
    public const int SessionHeaderSize = 5;

    /// <summary>Session header plus the channel, in front of a reliable packet for a channel other than 0.</summary>
    public const int ChannelHeaderSize = SessionHeaderSize + 1;

    /// <summary>Bumped whenever the datagram format changes, so mismatched builds refuse each other.</summary>
    public const byte WireVersion = 5;

    /// <summary>Where a handshake request carries <see cref="WireVersion" />, then the rest of its settings.</summary>
    public const int HandshakeVersionOffset = 9 + CookieSize;

    public const int HandshakeChannelsOffset = HandshakeVersionOffset + 1;
    public const int HandshakeMtuOffset = HandshakeVersionOffset + 2;
    public const int HandshakeWindowOffset = HandshakeVersionOffset + 4;
    public const int HandshakeFlagsOffset = HandshakeVersionOffset + 6;

    /// <summary>Where a ChallengeResponse echoes the time its cookie was issued.</summary>
    public const int HandshakeIssuedOffset = HandshakeVersionOffset + 7;

    /// <summary>Where an encrypted ChallengeResponse carries the client's public key.</summary>
    public const int HandshakeKeyOffset = HandshakeIssuedOffset + 4;

    public const byte EncryptedFlag = 1;

    public const int CookieSize = 16;

    /// <summary>Size of ConnectRequest and ChallengeResponse, which must exceed every reply.</summary>
    public const int HandshakeRequestSize = 176;

    public const int ChallengeSize = 1 + 4 + 4 + CookieSize;
    public const int AcceptSize = 1 + 4 + 4;
    public const int EncryptedAcceptSize =
        AcceptSize + KcpCipher.PublicKeySize + KcpCipher.SignatureSize + KcpCipher.ConfirmationSize + ResetTokenSize;

    public const int ResetTokenSize = 16;
    public const int ResetSize = SessionHeaderSize;
    public const int EncryptedResetSize = SessionHeaderSize + ResetTokenSize;
    public const int RejectSize = 1 + 4 + 1;
    public const int PingSize = SessionHeaderSize + 4;

    /// <summary>Session header plus flags, sequence, index and count, in front of each UnreliableFramed piece.</summary>
    public const int FramedHeaderSize = SessionHeaderSize + 5;

    /// <summary>The most pieces one unreliable message is split into.</summary>
    public const int MaxUnreliablePieces = 32;

    public const byte SequencedFlag = 0x80;
}
