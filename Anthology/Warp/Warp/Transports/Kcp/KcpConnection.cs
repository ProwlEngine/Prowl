// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>A <see cref="Connection" /> on a <see cref="KcpTransport" />.</summary>
public sealed class KcpConnection : Connection
{
    private readonly long _connectStartMs;
    private readonly byte[] _cookie = new byte[KcpPacket.CookieSize];
    private readonly KcpTransportOptions _options;
    private readonly KcpTransport _transport;
    private uint _cookieIssued;
    private bool _hasCookie;
    private bool _refusedAccept;
    private long _challengeRttMs;
    private long _cookieMs;
    private long _requestSentMs;
    private bool _hasRtt;
    private long _lastReceiveMs;
    private long _nextHandshakeMs;
    private long _nextPingMs;

    // One KCP conversation per channel, created when the channel is first used.
    private Protocol.Kcp?[]? _reliable;

    // Unreliable messages in pieces: what this side sends next, the newest sequenced message delivered per
    // channel (-1 before the first), and a few messages part way through arriving.
    private ushort _piecedOut;
    private ushort[]? _sequencesOut;
    private int[]? _sequencesIn;
    private readonly PartialMessage?[] _partials = new PartialMessage?[4];
    private long _releasedRetransmits;

    // Send rate budget in bytes, refilled over time and spent by every packet sent.
    private double _sendTokens;
    private long _packetsDropped;
    private bool _tokensStarted;
    private long _tokensUpdatedMs;
    private long _tokensCountedBytes;
    private double _smoothedRttMs;

    internal KcpConnection(KcpTransport transport, int id, string address, bool isOutgoing, SocketAddress? remote,
        long now)
        : base(transport, id, address, isOutgoing)
    {
        _transport = transport;
        _options = transport.Settings;
        RemoteAddress = remote;
        _connectStartMs = now;
        _nextHandshakeMs = now;
        _lastReceiveMs = now;
    }

    /// <summary>Every address of the host while more than one is being tried, until one answers.</summary>
    internal SocketAddress[]? Candidates { get; set; }

    /// <summary>The host's Challenge has arrived, so the address it came from is the one to keep.</summary>
    internal bool HasCookie => _hasCookie;

    /// <summary>Where packets for this connection go, and come from. Null if the address never resolved.</summary>
    internal SocketAddress? RemoteAddress { get; set; }

    /// <summary>Host: the client's IP address (its /64 for IPv6), which its connection counts against.</summary>
    internal UInt128 IpKey { get; init; }

    /// <summary>Picked by the client, echoed through the handshake so both sides know which attempt a packet belongs to.</summary>
    internal uint ClientNonce { get; init; }

    /// <summary>When the session started, on the transport's clock. Handshakes issued before it cannot replace it.</summary>
    internal long SessionStartMs { get; private set; }

    /// <summary>Seals and opens session packets once keys are agreed. Null on a plain connection.</summary>
    internal KcpCipher? Cipher { get; set; }

    /// <summary>Client: this attempt's key pair, and its public half as sent in the ChallengeResponse.</summary>
    internal ECDiffieHellman? KeyExchange { get; init; }

    internal byte[]? PublicKey { get; init; }

    /// <summary>Host: the Accept sent for this session, resent as is if the client asks again.</summary>
    internal byte[]? AcceptPacket { get; set; }

    /// <summary>Picked by the host on Accept. Tags every session packet, and doubles as the KCP conversation id.</summary>
    internal uint SessionId { get; set; }

    public override long PendingSendBytes => _reliable == null ? 0 : PendingReliableSegments() * SegmentBytes;

    public override long MaxPendingSendBytes => _options.MaxPendingReliableBytes;

    /// <summary>What one queued reliable segment costs against the send limit.</summary>
    private long SegmentBytes => Math.Max(_options.Mtu, Protocol.Kcp.MTU_DEF);

    /// <summary>The datagram size left for a plain packet once encryption takes its share.</summary>
    private int Mtu => _options.Mtu - (_options.Encryption != null ? KcpCipher.Overhead : 0);

    public override ConnectionStats Stats =>
        base.Stats with { Retransmits = Retransmits(), PacketsDropped = _packetsDropped };

    public override TimeSpan RoundTripTime => TimeSpan.FromMilliseconds(_smoothedRttMs);

    public override int GetMaxMessageSize(Delivery delivery)
    {
        if (delivery != Delivery.Reliable)
        {
            return KcpPacket.MaxUnreliablePieces * PieceSize;
        }

        // KCP numbers a message's fragments in one byte, and the receiver must be able to hold them all
        // in its window. kcp2k raises any window below its default up to that default. Sized for the smaller
        // segments of channels other than 0, so the limit holds on every channel.
        int segmentSize = Mtu - KcpPacket.ChannelHeaderSize - Protocol.Kcp.OVERHEAD;
        int window = (int)Math.Max(_options.Reliability.ReceiveWindow, Protocol.Kcp.WND_RCV);
        int fragments = Math.Min(Protocol.Kcp.FRG_MAX, window - 1);
        return segmentSize * fragments;
    }

    // Unreliable batches stay within one datagram, so one lost packet loses one batch only.
    public override int GetBatchThreshold(Delivery delivery) => delivery switch
    {
        Delivery.Reliable => Math.Min(GetMaxMessageSize(delivery),
            Mtu - KcpPacket.ChannelHeaderSize - Protocol.Kcp.OVERHEAD),
        Delivery.Unreliable => Mtu - KcpPacket.SessionHeaderSize,
        _ => PieceSize
    };

    /// <summary>Largest piece of an unreliable message that one UnreliableFramed packet carries.</summary>
    private int PieceSize => Mtu - KcpPacket.FramedHeaderSize;

    protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
    {
        if (delivery != Delivery.Reliable)
        {
            SendUnreliable(data, delivery, channel);
            return;
        }

        int segmentSize = Mtu - HeaderSize(channel) - Protocol.Kcp.OVERHEAD;
        long segments = Math.Max(1L, ((long)data.Length + segmentSize - 1) / segmentSize);
        if (PendingSendBytes + segments * SegmentBytes > MaxPendingSendBytes)
        {
            _transport.Close(this, DisconnectReason.ResourceLimit, true);
            return;
        }

        // kcp2k takes arrays, not spans. It copies into its segments, so the array goes straight back.
        byte[] scratch = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(scratch);
        int result = Reliable(channel).Send(scratch, 0, data.Length);
        ArrayPool<byte>.Shared.Return(scratch);
        if (result < 0)
        {
            throw new InvalidOperationException($"KCP refused a {data.Length} byte message (error {result}).");
        }
    }

    /// <summary>Sends this connection's queued reliable messages without ticking the rest of the transport.</summary>
    public override void Flush() => FlushReliable(_transport.NowMs);

    /// <summary>Sends small plain messages as they are, and everything else as numbered pieces.</summary>
    private void SendUnreliable(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
    {
        // Over the send rate cap, unreliable messages are dropped whole rather than queued.
        if (_options.MaxSendRate > 0)
        {
            RefillSendTokens(_transport.NowMs);
            if (_sendTokens < 0)
            {
                _packetsDropped++;
                return;
            }
        }

        if (delivery == Delivery.Unreliable && data.Length <= Mtu - KcpPacket.SessionHeaderSize)
        {
            _transport.SendSession(this, KcpPacketType.Unreliable, data);
            return;
        }

        bool sequenced = delivery == Delivery.UnreliableSequenced;
        ushort sequence = sequenced ? _sequencesOut![channel]++ : _piecedOut++;
        int count = Math.Max(1, (data.Length + PieceSize - 1) / PieceSize);
        Span<byte> header = stackalloc byte[KcpPacket.FramedHeaderSize - KcpPacket.SessionHeaderSize];
        header[0] = (byte)(channel | (sequenced ? KcpPacket.SequencedFlag : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(header[1..], sequence);
        header[4] = (byte)count;
        for (int i = 0; i < count; i++)
        {
            header[3] = (byte)i;
            int offset = i * PieceSize;
            _transport.SendSession(this, KcpPacketType.UnreliableFramed, header,
                data.Slice(offset, Math.Min(PieceSize, data.Length - offset)));
        }
    }

    public override void Disconnect() => _transport.Close(this, DisconnectReason.Requested, true);

    /// <summary>Sets up the channels once the session id is known.</summary>
    internal void StartSession(long now)
    {
        _reliable = new Protocol.Kcp?[_options.Channels];
        Reliable(0);
        _sequencesOut = new ushort[_options.Channels];
        _sequencesIn = new int[_options.Channels];
        Array.Fill(_sequencesIn, -1);

        SessionStartMs = now;
        _lastReceiveMs = now;
        _nextPingMs = now;
    }

    /// <summary>The KCP conversation carrying a reliable channel, created on first use.</summary>
    private Protocol.Kcp Reliable(byte channel) => _reliable![channel] ??= CreateReliable(channel);

    private Protocol.Kcp CreateReliable(byte channel)
    {
        KcpReliabilityOptions reliability = _options.Reliability;
        byte[] prefix = { channel };

        Protocol.Kcp kcp = new(SessionId, channel == 0
            ? (buffer, length) => _transport.SendSession(this, KcpPacketType.Reliable, buffer.AsSpan(0, length))
            : (buffer, length) =>
                _transport.SendSession(this, KcpPacketType.ReliableChannel, prefix, buffer.AsSpan(0, length)));
        kcp.SetNoDelay(reliability.NoDelay ? 1u : 0u, reliability.Interval, reliability.FastResend,
            !reliability.CongestionControl);
        kcp.SetWindowSize(reliability.SendWindow, reliability.ReceiveWindow);
        kcp.SetMtu((uint)(Mtu - HeaderSize(channel)));
        kcp.dead_link = reliability.MaxRetransmits;
        kcp.SetMaxRetransmitTimeout((int)reliability.MaxRetransmitTimeout);
        return kcp;
    }

    /// <summary>Header bytes in front of a reliable channel's KCP segments: channel 0's is implied by its packet type.</summary>
    private static int HeaderSize(byte channel) =>
        channel == 0 ? KcpPacket.SessionHeaderSize : KcpPacket.ChannelHeaderSize;

    private long Retransmits()
    {
        if (_reliable == null)
        {
            return _releasedRetransmits;
        }

        long total = 0;
        foreach (Protocol.Kcp? kcp in _reliable)
        {
            total += kcp?.retransmits ?? 0;
        }

        return total;
    }

    /// <summary>Queued and unacknowledged reliable segments, across every channel.</summary>
    private long PendingReliableSegments()
    {
        long total = 0;
        foreach (Protocol.Kcp? kcp in _reliable!)
        {
            total += kcp?.WaitSnd ?? 0;
        }

        return total;
    }

    internal void HandlePacket(byte[] buffer, int length, long now)
    {
        ReadOnlySpan<byte> packet = buffer.AsSpan(0, length);
        KcpPacketType type = (KcpPacketType)packet[0];

        switch (type)
        {
            case KcpPacketType.ConnectRequest:
            case KcpPacketType.ChallengeResponse:
                if (!IsOutgoing)
                {
                    _transport.HandleRepeatHandshake(this, packet, now);
                }

                return;

            case KcpPacketType.Challenge:
                if (IsOutgoing && State == ConnectionState.Connecting && length == KcpPacket.ChallengeSize &&
                    ReadNonce(packet) == ClientNonce)
                {
                    RecordPacketReceived(length);
                    _cookieIssued = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
                    packet.Slice(9, KcpPacket.CookieSize).CopyTo(_cookie);
                    _hasCookie = true;
                    _cookieMs = now;
                    _challengeRttMs = now - _requestSentMs;
                    _transport.SendChallengeResponse(this, _cookie, _cookieIssued);
                    _nextHandshakeMs = now + (long)_options.HandshakeResendInterval.TotalMilliseconds;
                }

                return;

            case KcpPacketType.Accept:
                if (IsOutgoing && State == ConnectionState.Connecting && ReadNonce(packet) == ClientNonce &&
                    length == (KeyExchange != null ? KcpPacket.EncryptedAcceptSize : KcpPacket.AcceptSize))
                {
                    RecordPacketReceived(length);
                    SessionId = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);

                    // A forged Accept is ignored, so it can't cut off the genuine one still on its way.
                    if (KeyExchange != null && !AgreeKeys(packet))
                    {
                        SessionId = 0;
                        _refusedAccept = true;
                        return;
                    }

                    StartSession(now);
                    _transport.OnConnected(this);
                }

                return;

            case KcpPacketType.Reject:
                if (IsOutgoing && State == ConnectionState.Connecting && length == KcpPacket.RejectSize &&
                    ReadNonce(packet) == ClientNonce)
                {
                    RecordPacketReceived(length);
                    DisconnectReason reason = (DisconnectReason)packet[5] switch
                    {
                        DisconnectReason.ServerFull => DisconnectReason.ServerFull,
                        DisconnectReason.ProtocolError => DisconnectReason.ProtocolError,
                        _ => DisconnectReason.Rejected
                    };
                    _transport.Close(this, reason, false);
                }

                return;
        }

        // Everything else belongs to an established session.
        if (Authenticate(ref buffer, ref length, false))
        {
            Process(buffer, length, now);
        }
    }

    /// <summary>
    ///     Checks that a packet belongs to this session and, when encrypted, decrypts it. On success
    ///     <paramref name="buffer" /> and <paramref name="length" /> describe the plain packet. With
    ///     <paramref name="newestOnly" />, only a packet newer than any seen passes, as a move to a new address requires.
    /// </summary>
    internal bool Authenticate(ref byte[] buffer, ref int length, bool newestOnly)
    {
        bool inSession = State == ConnectionState.Connected || (State == ConnectionState.Connecting && !IsOutgoing);
        if (!inSession || length < KcpPacket.SessionHeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(1)) != SessionId)
        {
            return false;
        }

        return Cipher == null || _transport.Open(this, buffer.AsSpan(0, length), newestOnly, out buffer, out length);
    }

    /// <summary>Follows the peer to a new address.</summary>
    internal void MoveTo(SocketAddress remote, string address)
    {
        RemoteAddress = remote;
        Address = address;
    }

    /// <summary>
    ///     Handles a session packet that <see cref="Authenticate" /> accepted. <paramref name="from" /> is where it
    ///     came from when that isn't the connection's address, which is where a path challenge is answered.
    /// </summary>
    internal void Process(byte[] buffer, int length, long now, SocketAddress? from = null)
    {
        ReadOnlySpan<byte> packet = buffer.AsSpan(0, length);
        KcpPacketType type = (KcpPacketType)packet[0];
        if (State == ConnectionState.Connecting)
        {
            _transport.OnConnected(this);
        }

        RecordPacketReceived(length);
        _lastReceiveMs = now;
        ReadOnlySpan<byte> body = packet[KcpPacket.SessionHeaderSize..];

        switch (type)
        {
            case KcpPacketType.Reliable:
                InputReliable(0, buffer, KcpPacket.SessionHeaderSize, length);
                break;

            // Channel 0 always goes as Reliable, so a channel byte of 0 isn't valid here.
            case KcpPacketType.ReliableChannel:
                if (body.Length >= 1 && body[0] != 0 && body[0] < _options.Channels)
                {
                    InputReliable(body[0], buffer, KcpPacket.ChannelHeaderSize, length);
                }

                break;

            case KcpPacketType.Unreliable:
                _transport.OnData(this, body, Delivery.Unreliable, 0);
                break;

            case KcpPacketType.UnreliableFramed:
                ReceivePiece(body, now);
                break;

            case KcpPacketType.Ping:
                if (body.Length >= 4)
                {
                    _transport.SendSession(this, KcpPacketType.Pong, body[..4]);
                }

                break;

            case KcpPacketType.Pong:
                if (body.Length >= 4)
                {
                    // Ignores echoes older than the timeout, which only a forged Pong can carry.
                    uint sample = (uint)now - BinaryPrimitives.ReadUInt32LittleEndian(body);
                    if (sample <= _options.Timeout.TotalMilliseconds)
                    {
                        AddRttSample(sample);
                    }
                }

                break;

            case KcpPacketType.Disconnect:
                _transport.Close(this, DisconnectReason.RemoteClosed, false);
                break;

            case KcpPacketType.PathChallenge:
                if (body.Length == PathTokenSize)
                {
                    _transport.SendSession(this, KcpPacketType.PathResponse, ReadOnlySpan<byte>.Empty, body, from);
                }

                break;
        }
    }

    private const int PathTokenSize = 8;
    private SocketAddress? _pathCandidate;
    private readonly byte[] _pathToken = new byte[PathTokenSize];
    private long _pathChallengedMs;

    /// <summary>
    ///     Asks <paramref name="candidate" /> to echo a fresh token. At most one challenge per 100 ms, whatever the
    ///     address, so spoofed sources can't turn the host into a reflector.
    /// </summary>
    internal void ChallengePath(SocketAddress candidate, long now)
    {
        if (State != ConnectionState.Connected || (_pathCandidate != null && now - _pathChallengedMs < 100))
        {
            return;
        }

        _pathCandidate = _transport.CloneAddress(candidate);
        RandomNumberGenerator.Fill(_pathToken);
        _pathChallengedMs = now;
        _transport.SendSession(this, KcpPacketType.PathChallenge, ReadOnlySpan<byte>.Empty, _pathToken, _pathCandidate);
    }

    /// <summary>Whether a PathResponse from <paramref name="from" /> echoes the challenge sent there.</summary>
    internal bool ConfirmsPath(ReadOnlySpan<byte> body, SocketAddress from)
    {
        if (State != ConnectionState.Connected || _pathCandidate == null || !from.Equals(_pathCandidate) ||
            !CryptographicOperations.FixedTimeEquals(body, _pathToken))
        {
            return false;
        }

        _pathCandidate = null;
        return true;
    }

    /// <summary>Handshake resends, keepalives, the reliable channel's timers and timeouts.</summary>
    internal void Tick(long now)
    {
        if (State == ConnectionState.Connecting)
        {
            if (now - _connectStartMs >= (long)_options.ConnectTimeout.TotalMilliseconds)
            {
                _transport.Close(this,
                    _refusedAccept ? DisconnectReason.AuthenticationFailed : DisconnectReason.Timeout, false);
                return;
            }

            // An incoming connection waits for the client's first session packet, which proves it has the Accept.
            if (!IsOutgoing)
            {
                return;
            }

            if (now >= _nextHandshakeMs)
            {
                // A cookie that gets no Accept may be spoiled or from a host that restarted, so ask for a new one.
                long interval = (long)_options.HandshakeResendInterval.TotalMilliseconds;
                if (_hasCookie && now - _cookieMs > Math.Max(4 * interval, 3 * _challengeRttMs))
                {
                    _hasCookie = false;
                }

                if (_hasCookie)
                {
                    _transport.SendChallengeResponse(this, _cookie, _cookieIssued);
                }
                else
                {
                    _transport.SendConnectRequest(this);
                    _requestSentMs = now;
                }

                _nextHandshakeMs = now + (long)_options.HandshakeResendInterval.TotalMilliseconds;
            }

            return;
        }

        if (now - _lastReceiveMs >= (long)_options.Timeout.TotalMilliseconds)
        {
            _transport.Close(this, DisconnectReason.Timeout, false);
            return;
        }

        if (now >= _nextPingMs)
        {
            Span<byte> time = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(time, (uint)now);
            _transport.SendSession(this, KcpPacketType.Ping, time);
            _nextPingMs = now + (long)_options.PingInterval.TotalMilliseconds;
        }

        FlushReliable(now);

        foreach (PartialMessage? partial in _partials)
        {
            if (partial is { Buffer: not null } && now - partial.StartedMs > PartialLifetimeMs)
            {
                partial.Release();
            }
        }

        // kcp2k flags the link as dead once a segment has been resent dead_link times without an ack.
        foreach (Protocol.Kcp? kcp in _reliable!)
        {
            if (kcp?.state == -1)
            {
                _transport.Close(this, DisconnectReason.Timeout, false);
                return;
            }
        }
    }

    /// <summary>Sends whatever the reliable channels have due: new messages, acks and retransmits.</summary>
    internal void FlushReliable(long now)
    {
        if (_reliable == null)
        {
            return;
        }

        // Update only flushes once per KCP interval. Flushing on every call as well sends new messages
        // and acks on this tick instead of up to an interval later; retransmits are still timer driven.
        if (_options.MaxSendRate == 0)
        {
            foreach (Protocol.Kcp? kcp in _reliable)
            {
                kcp?.Update((uint)now);
                kcp?.Flush();
            }

            return;
        }

        // Channels in order, so lower channels spend the budget first.
        RefillSendTokens(now);
        foreach (Protocol.Kcp? kcp in _reliable)
        {
            if (kcp == null)
            {
                continue;
            }

            kcp.send_budget = _sendTokens > 0 ? (int)Math.Ceiling(_sendTokens / SegmentBytes) : 0;
            kcp.Update((uint)now);
            kcp.Flush();
            RefillSendTokens(now);
        }
    }

    /// <summary>Adds the budget earned since the last call and spends what has been sent since.</summary>
    private void RefillSendTokens(long now)
    {
        if (!_tokensStarted)
        {
            _tokensStarted = true;
            _tokensUpdatedMs = now;
            _sendTokens = _options.MaxSendRate / 10.0;
        }

        double burst = Math.Max(_options.MaxSendRate / 10.0, 2.0 * _options.Mtu);
        _sendTokens = Math.Min(burst, _sendTokens + _options.MaxSendRate * (now - _tokensUpdatedMs) / 1000.0);
        _tokensUpdatedMs = now;

        long sent = base.Stats.BytesSent;
        _sendTokens = Math.Max(-burst, _sendTokens - (sent - _tokensCountedBytes));
        _tokensCountedBytes = sent;
    }

    internal void ReleaseBuffers()
    {
        Cipher?.Dispose();
        Cipher = null;
        KeyExchange?.Dispose();

        foreach (PartialMessage? partial in _partials)
        {
            partial?.Release();
        }

        _releasedRetransmits = Retransmits();
        _reliable = null;
    }

    internal void RecordSent(int bytes) => RecordPacketSent(bytes);

    internal void RecordDropped() => _packetsDropped++;

    private void InputReliable(byte channel, byte[] buffer, int offset, int length)
    {
        Protocol.Kcp kcp = Reliable(channel);
        if (kcp.Input(buffer, offset, length - offset) >= 0)
        {
            DrainReliable(kcp, channel);
        }
    }

    private void DrainReliable(Protocol.Kcp kcp, byte channel)
    {
        int size;
        while (IsConnected && (size = kcp.PeekSize()) >= 0)
        {
            byte[] message = ArrayPool<byte>.Shared.Rent(size);
            int received = kcp.Receive(message, size);
            if (received < 0)
            {
                ArrayPool<byte>.Shared.Return(message);
                break;
            }

            _transport.OnDataPooled(this, message, received, Delivery.Reliable, channel);
        }
    }

    private void AddRttSample(uint sampleMs)
    {
        // Same smoothing as TCP's SRTT (RFC 6298).
        if (!_hasRtt)
        {
            _smoothedRttMs = sampleMs;
            _hasRtt = true;
        }
        else
        {
            _smoothedRttMs += (sampleMs - _smoothedRttMs) * 0.125;
        }
    }

    /// <summary>Client: checks the host's signature if a host key is trusted, then derives this session's keys.</summary>
    private bool AgreeKeys(ReadOnlySpan<byte> accept)
    {
        ReadOnlySpan<byte> hostPublic = accept.Slice(KcpPacket.AcceptSize, KcpCipher.PublicKeySize);
        ReadOnlySpan<byte> signature = accept.Slice(KcpPacket.AcceptSize + KcpCipher.PublicKeySize,
            KcpCipher.SignatureSize);
        ReadOnlySpan<byte> confirmation = accept[^KcpCipher.ConfirmationSize..];
        if (_transport.TrustedHostKey is { } trusted && !trusted.VerifyData(
                KcpCipher.Transcript(PublicKey, hostPublic, ClientNonce, SessionId, _options.ProtocolId), signature,
                HashAlgorithmName.SHA256))
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[KcpCipher.ConfirmationSize];
        KcpCipher? cipher = KcpCipher.Create(KeyExchange!, PublicKey, hostPublic, ClientNonce, SessionId, true, expected);
        if (cipher == null || !CryptographicOperations.FixedTimeEquals(expected, confirmation))
        {
            cipher?.Dispose();
            return false;
        }

        Cipher = cipher;

        KeyExchange!.Dispose();
        return true;
    }

    private const long PartialLifetimeMs = 1000;

    /// <summary>Takes one piece of an unreliable message, delivering the message once every piece is in.</summary>
    private void ReceivePiece(ReadOnlySpan<byte> body, long now)
    {
        if (body.Length <= KcpPacket.FramedHeaderSize - KcpPacket.SessionHeaderSize)
        {
            return;
        }

        bool sequenced = (body[0] & KcpPacket.SequencedFlag) != 0;
        byte channel = (byte)(body[0] & ~KcpPacket.SequencedFlag);
        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]);
        int index = body[3], count = body[4];
        ReadOnlySpan<byte> piece = body[5..];
        bool last = index == count - 1;
        if (channel >= _options.Channels || (!sequenced && channel != 0) || count == 0 ||
            count > KcpPacket.MaxUnreliablePieces || index >= count || piece.Length > PieceSize ||
            (!last && piece.Length != PieceSize) || (sequenced && !IsNewer(channel, sequence)))
        {
            return;
        }

        Delivery delivery = sequenced ? Delivery.UnreliableSequenced : Delivery.Unreliable;
        if (count == 1)
        {
            Deliver(piece, delivery, channel, sequence);
            return;
        }

        PartialMessage partial = FindPartial(sequenced, channel, sequence, count, now);
        uint bit = 1u << index;
        if (partial.Count != count || (partial.Received & bit) != 0)
        {
            return;
        }

        piece.CopyTo(partial.Buffer.AsSpan(index * PieceSize));
        partial.Received |= bit;
        if (last)
        {
            partial.LastLength = piece.Length;
        }

        if (partial.Received == (count == 32 ? uint.MaxValue : (1u << count) - 1))
        {
            int length = (count - 1) * PieceSize + partial.LastLength;
            if (!sequenced || IsNewer(channel, sequence))
            {
                Deliver(partial.Buffer.AsSpan(0, length), delivery, channel, sequence);
            }

            partial.Release();
        }
    }

    private void Deliver(ReadOnlySpan<byte> message, Delivery delivery, byte channel, ushort sequence)
    {
        if (delivery == Delivery.UnreliableSequenced)
        {
            _sequencesIn![channel] = sequence;
        }

        _transport.OnData(this, message, delivery, channel);
    }

    private bool IsNewer(byte channel, ushort sequence) =>
        _sequencesIn![channel] < 0 || (short)(sequence - (ushort)_sequencesIn[channel]) > 0;

    /// <summary>The partial message with this key, or a fresh one in a free slot or in place of the oldest.</summary>
    private PartialMessage FindPartial(bool sequenced, byte channel, ushort sequence, int count, long now)
    {
        PartialMessage? victim = null;
        for (int i = 0; i < _partials.Length; i++)
        {
            PartialMessage partial = _partials[i] ??= new PartialMessage();
            if (partial.Buffer != null && partial.Sequenced == sequenced && partial.Channel == channel &&
                partial.Sequence == sequence)
            {
                return partial;
            }

            if (victim == null ||
                (victim.Buffer != null && (partial.Buffer == null || partial.StartedMs < victim.StartedMs)))
            {
                victim = partial;
            }
        }

        victim!.Release();
        victim.Start(sequenced, channel, sequence, count, count * PieceSize, now);
        return victim;
    }

    private sealed class PartialMessage
    {
        public byte[]? Buffer;
        public byte Channel;
        public int Count;
        public int LastLength;
        public uint Received;
        public bool Sequenced;
        public ushort Sequence;
        public long StartedMs;

        public void Start(bool sequenced, byte channel, ushort sequence, int count, int size, long now)
        {
            Buffer = ArrayPool<byte>.Shared.Rent(size);
            Sequenced = sequenced;
            Channel = channel;
            Sequence = sequence;
            Count = count;
            Received = 0;
            LastLength = 0;
            StartedMs = now;
        }

        public void Release()
        {
            if (Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(Buffer);
                Buffer = null;
            }
        }
    }

    private static uint ReadNonce(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]);
}
