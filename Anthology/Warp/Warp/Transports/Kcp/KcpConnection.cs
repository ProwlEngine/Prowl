// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>A <see cref="Connection"/> on a <see cref="KcpTransport"/>.</summary>
public sealed class KcpConnection : Connection
{
    private readonly KcpTransport _transport;
    private readonly KcpTransportOptions _options;
    private readonly byte[] _cookie = new byte[KcpPacket.CookieSize];
    private Protocol.Kcp? _kcp;
    private Protocol.Kcp? _bulkKcp;
    private byte[] _sendScratch = Array.Empty<byte>();
    private bool _hasCookie;
    private long _connectStartMs;
    private long _nextHandshakeMs;
    private long _lastReceiveMs;
    private long _nextPingMs;
    private double _smoothedRttMs;
    private bool _hasRtt;

    internal KcpConnection(KcpTransport transport, int id, string address, bool isOutgoing, SocketAddress? remote, long now)
        : base(transport, id, address, isOutgoing)
    {
        _transport = transport;
        _options = transport.Settings;
        RemoteAddress = remote;
        _connectStartMs = now;
        _nextHandshakeMs = now;
        _lastReceiveMs = now;
    }

    /// <summary>Where packets for this connection go, and come from. Null if the address never resolved.</summary>
    internal SocketAddress? RemoteAddress { get; }

    /// <summary>Picked by the client, echoed through the handshake so both sides know which attempt a packet belongs to.</summary>
    internal uint ClientNonce { get; init; }

    /// <summary>Picked by the host on Accept. Tags every session packet, and doubles as the KCP conversation id.</summary>
    internal uint SessionId { get; set; }

    public override TimeSpan RoundTripTime => TimeSpan.FromMilliseconds(_smoothedRttMs);

    public override int GetMaxMessageSize(Delivery delivery)
    {
        int payload = _options.Mtu - KcpPacket.SessionHeaderSize;
        if (delivery == Delivery.Unreliable)
            return payload;

        // KCP numbers a message's fragments in one byte, and the receiver must be able to hold them all
        // in its window. kcp2k raises any window below its default up to that default.
        int segmentSize = payload - Protocol.Kcp.OVERHEAD;
        int window = (int)Math.Max(delivery == Delivery.Bulk ? _options.BulkReceiveWindow : _options.Reliability.ReceiveWindow, Protocol.Kcp.WND_RCV);
        int fragments = Math.Min(Protocol.Kcp.FRG_MAX, window - 1);
        return segmentSize * fragments;
    }

    public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
    {
        ValidateSend(data, delivery);

        if (delivery == Delivery.Unreliable)
        {
            _transport.SendSession(this, KcpPacketType.Unreliable, data);
            return;
        }

        int segmentSize = _options.Mtu - KcpPacket.SessionHeaderSize - Protocol.Kcp.OVERHEAD;
        long segments = Math.Max(1L, ((long)data.Length + segmentSize - 1) / segmentSize);
        long segmentBytes = Math.Max(_options.Mtu, Protocol.Kcp.MTU_DEF);
        if (((long)_kcp!.WaitSnd + _bulkKcp!.WaitSnd + segments) * segmentBytes > _options.MaxPendingReliableBytes)
        {
            _transport.Close(this, DisconnectReason.ResourceLimit, notifyRemote: true);
            return;
        }

        // kcp2k takes arrays, not spans.
        if (_sendScratch.Length < data.Length)
            _sendScratch = new byte[Math.Max(data.Length, _sendScratch.Length * 2)];
        data.CopyTo(_sendScratch);

        int result = (delivery == Delivery.Bulk ? _bulkKcp! : _kcp!).Send(_sendScratch, 0, data.Length);
        if (result < 0)
            throw new InvalidOperationException($"KCP refused a {data.Length} byte message (error {result}).");
    }

    public override void Disconnect() => _transport.Close(this, DisconnectReason.Requested, notifyRemote: true);

    /// <summary>Sets up the reliable channel once the session id is known.</summary>
    internal void StartSession(long now)
    {
        KcpReliabilityOptions reliability = _options.Reliability;

        Protocol.Kcp kcp = new(SessionId, (buffer, length) => _transport.SendSession(this, KcpPacketType.Reliable, buffer.AsSpan(0, length)));
        kcp.SetNoDelay(reliability.NoDelay ? 1u : 0u, reliability.Interval, reliability.FastResend, !reliability.CongestionControl);
        kcp.SetWindowSize(reliability.SendWindow, reliability.ReceiveWindow);
        kcp.SetMtu((uint)(_options.Mtu - KcpPacket.SessionHeaderSize));
        kcp.dead_link = reliability.MaxRetransmits;
        _kcp = kcp;
        Protocol.Kcp bulk = new(SessionId, (buffer, length) => _transport.SendSession(this, KcpPacketType.BulkReliable, buffer.AsSpan(0, length)));
        bulk.SetNoDelay(reliability.NoDelay ? 1u : 0u, reliability.Interval, reliability.FastResend, !reliability.CongestionControl);
        bulk.SetWindowSize(_options.BulkSendWindow, _options.BulkReceiveWindow);
        bulk.SetMtu((uint)(_options.Mtu - KcpPacket.SessionHeaderSize));
        bulk.dead_link = reliability.MaxRetransmits;
        _bulkKcp = bulk;

        _lastReceiveMs = now;
        _nextPingMs = now;
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
                    _transport.HandleRepeatHandshake(this, packet, now);
                return;

            case KcpPacketType.Challenge:
                if (IsOutgoing && State == ConnectionState.Connecting && length == KcpPacket.ChallengeSize && ReadNonce(packet) == ClientNonce)
                {
                    RecordPacketReceived(length);
                    packet.Slice(5, KcpPacket.CookieSize).CopyTo(_cookie);
                    _hasCookie = true;
                    _transport.SendChallengeResponse(this, _cookie);
                    _nextHandshakeMs = now + (long)_options.HandshakeResendInterval.TotalMilliseconds;
                }
                return;

            case KcpPacketType.Accept:
                if (IsOutgoing && State == ConnectionState.Connecting && length == KcpPacket.AcceptSize && ReadNonce(packet) == ClientNonce)
                {
                    RecordPacketReceived(length);
                    SessionId = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
                    StartSession(now);
                    _transport.OnConnected(this);
                }
                return;

            case KcpPacketType.Reject:
                if (IsOutgoing && State == ConnectionState.Connecting && length == KcpPacket.RejectSize && ReadNonce(packet) == ClientNonce)
                {
                    RecordPacketReceived(length);
                    DisconnectReason reason = (DisconnectReason)packet[5] == DisconnectReason.ServerFull ? DisconnectReason.ServerFull : DisconnectReason.Rejected;
                    _transport.Close(this, reason, notifyRemote: false);
                }
                return;
        }

        // Everything else belongs to an established session.
        if (State != ConnectionState.Connected || length < KcpPacket.SessionHeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != SessionId)
            return;

        RecordPacketReceived(length);
        _lastReceiveMs = now;
        ReadOnlySpan<byte> body = packet[KcpPacket.SessionHeaderSize..];

        switch (type)
        {
            case KcpPacketType.Reliable:
                if (_kcp!.Input(buffer, KcpPacket.SessionHeaderSize, body.Length) >= 0)
                    DrainReliable(_kcp, Delivery.Reliable);
                break;

            case KcpPacketType.BulkReliable:
                if (_bulkKcp!.Input(buffer, KcpPacket.SessionHeaderSize, body.Length) >= 0)
                    DrainReliable(_bulkKcp, Delivery.Bulk);
                break;

            case KcpPacketType.Unreliable:
                _transport.OnData(this, body, Delivery.Unreliable);
                break;

            case KcpPacketType.Ping:
                if (body.Length >= 4)
                    _transport.SendSession(this, KcpPacketType.Pong, body[..4]);
                break;

            case KcpPacketType.Pong:
                if (body.Length >= 4)
                    AddRttSample((uint)now - BinaryPrimitives.ReadUInt32LittleEndian(body));
                break;

            case KcpPacketType.Disconnect:
                _transport.Close(this, DisconnectReason.RemoteClosed, notifyRemote: false);
                break;
        }
    }

    /// <summary>Handshake resends, keepalives, the reliable channel's timers and timeouts.</summary>
    internal void Tick(long now)
    {
        if (State == ConnectionState.Connecting)
        {
            // Incoming connections are connected as soon as they exist; only outgoing ones wait here.
            if (now - _connectStartMs >= (long)_options.ConnectTimeout.TotalMilliseconds)
            {
                _transport.Close(this, DisconnectReason.Timeout, notifyRemote: false);
                return;
            }

            if (now >= _nextHandshakeMs)
            {
                if (_hasCookie)
                    _transport.SendChallengeResponse(this, _cookie);
                else
                    _transport.SendConnectRequest(this);

                _nextHandshakeMs = now + (long)_options.HandshakeResendInterval.TotalMilliseconds;
            }
            return;
        }

        if (now - _lastReceiveMs >= (long)_options.Timeout.TotalMilliseconds)
        {
            _transport.Close(this, DisconnectReason.Timeout, notifyRemote: false);
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

        // kcp2k flags the link as dead once a segment has been resent dead_link times without an ack.
        if (_kcp!.state == -1 || _bulkKcp!.state == -1)
            _transport.Close(this, DisconnectReason.Timeout, notifyRemote: false);
    }

    /// <summary>Sends whatever the reliable channel has due: new messages, acks and retransmits.</summary>
    internal void FlushReliable(long now)
    {
        if (_kcp == null)
            return;

        // Update only flushes once per KCP interval. Flushing on every call as well sends new messages
        // and acks on this tick instead of up to an interval later; retransmits are still timer driven.
        _kcp.Update((uint)now);
        _kcp.Flush();
        _bulkKcp!.Update((uint)now);
        _bulkKcp.Flush();
    }

    internal void ReleaseBuffers()
    {
        _kcp = null;
        _bulkKcp = null;
        _sendScratch = Array.Empty<byte>();
    }

    internal void RecordSent(int bytes) => RecordPacketSent(bytes);

    private void DrainReliable(Protocol.Kcp channel, Delivery delivery)
    {
        int size;
        while (IsConnected && (size = channel.PeekSize()) >= 0)
        {
            byte[] message = ArrayPool<byte>.Shared.Rent(size);
            int received = channel.Receive(message, size);
            if (received < 0)
            {
                ArrayPool<byte>.Shared.Return(message);
                break;
            }

            _transport.OnDataPooled(this, message, received, delivery);
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

    private static uint ReadNonce(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]);
}
