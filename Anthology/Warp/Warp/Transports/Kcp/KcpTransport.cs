// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
///     A transport over UDP: <see cref="Delivery.Reliable" /> messages are carried by an embedded KCP
///     implementation, one conversation per channel, and unreliable ones as plain datagrams. One socket serves both
///     incoming and outgoing connections. Addresses are <c>host:port</c>, <c>host</c> (using
///     <see cref="KcpTransportOptions.Port" />), or <c>[ipv6]:port</c>.
/// </summary>
/// <remarks>
///     If the OS supports it, the socket is dual-mode IPv6, so it reaches IPv4 and IPv6 peers alike.
///     Host names resolve asynchronously; completions are applied by the next pump. Disconnect and shutdown cancel pending
///     resolution.
/// </remarks>
public sealed class KcpTransport : Transport
{
    // Windows reports an ICMP "port unreachable" as a ConnectionReset on the next receive, which would
    // otherwise let one vanished client break the host's receive loop. This turns that off.
    private const int SioUdpConnReset = unchecked((int)0x9800000C);

    // How long a cookie stays valid after the host issues it.
    private const uint CookieLifetimeMs = 20_000;

    // Disconnect packets are unreliable, so send a few and hope one lands. The remote end times out otherwise.
    private const int DisconnectRepeats = 3;

    private readonly Dictionary<SocketAddress, KcpConnection> _byAddress = new();

    // Per IP address (one /64 for IPv6): its incoming connections and handshake budget.
    private readonly Dictionary<UInt128, AddressBudget> _byIp = new();

    // Cookies that already opened a session, each kept until it would have expired anyway.
    private readonly HashSet<ulong> _usedCookies = new();
    private readonly Queue<(long ExpiresMs, ulong Cookie)> _cookieExpiry = new();
    private long _nextSweepMs;

    // Handshakes from every address together, and encrypted ones whose key agreement runs on the thread pool.
    private readonly AddressBudget _allHandshakes;
    private readonly List<PendingAgreement> _agreements = new();
    private readonly object _signing = new();

    // Encrypted incoming connections, so an authenticated packet from a new address can find its session.
    private readonly Dictionary<uint, KcpConnection> _bySession = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _cookieKey = RandomNumberGenerator.GetBytes(32);

    private readonly List<DelayedPacket> _delayed = new();

    private readonly byte[] _receiveBuffer = new byte[KcpPacket.MaxDatagramSize];
    private readonly byte[] _openBuffer = new byte[KcpPacket.MaxDatagramSize];
    private readonly byte[] _plainBuffer = new byte[KcpPacket.MaxDatagramSize];
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolver;
    private readonly Dictionary<KcpConnection, Resolution> _resolving = new();
    private readonly List<KcpConnection> _scratch = new();
    private readonly List<KcpConnection> _unreachable = new();
    private int _incomingCount;
    private bool _listening;
    private KcpIoThreads? _io;
    private SocketAddress? _ioAddress;
    private SocketAddress? _receiveAddress;
    private byte[] _sendBuffer = Array.Empty<byte>();
    private Random? _simulationRandom;
    private Socket? _socket;

    public KcpTransport(KcpTransportOptions? options = null)
        : this(options, static (host, cancellation) => Dns.GetHostAddressesAsync(host, cancellation))
    {
    }

    internal KcpTransport(KcpTransportOptions? options, Func<string, CancellationToken, Task<IPAddress[]>> resolver)
    {
        Settings = (options ?? new KcpTransportOptions()).CopyValidated();
        _resolver = resolver;
        _allHandshakes = new AddressBudget(Settings.MaxHandshakesPerSecond, 0);
        if (Settings.Encryption?.TrustedHostKey != null)
        {
            TrustedHostKey = Settings.Encryption.ImportTrustedHostKey();
        }
    }

    /// <summary>The host key a client insists on, imported once.</summary>
    internal ECDsa? TrustedHostKey { get; }

    internal KcpTransportOptions Settings { get; }

    /// <summary>A detached copy of the settings captured when this transport was created.</summary>
    public KcpTransportOptions Options => Settings.CopyValidated();

    public override bool IsListening => _listening;

    public override TransportCapabilities Capabilities =>
        new(true, DeliverySupport.All, TransportExecution.WorkerPreferred, Settings.Channels, Settings.MaxConnections);

    /// <summary>The port the socket is bound to, or 0 if it isn't bound yet.</summary>
    public override int LocalPort => (_socket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    internal long NowMs => _clock.ElapsedMilliseconds;

    /// <summary>Datagrams the receive thread has read that Receive has not handled yet.</summary>
    internal int QueuedDatagrams => _io?.Count ?? 0;

    /// <exception cref="InvalidOperationException">The socket is already bound to a random port for outgoing connections.</exception>
    /// <exception cref="SocketException">The port is in use.</exception>
    public override void Listen()
    {
        ThrowIfDisposed();
        if (_listening)
        {
            return;
        }

        if (_socket != null)
        {
            throw new InvalidOperationException(
                "The socket is already bound for outgoing connections. Call Listen before Connect.");
        }

        Settings.Validate();
        OpenSocket(Settings.Port);
        _listening = true;
    }

    public override Connection Connect(string address)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(address);
        Settings.Validate();

        if (_socket == null)
        {
            OpenSocket(0);
        }

        CancellationTokenSource cancellation = new();
        ValueTask<SocketAddress?> resolution = TryResolveAsync(address, Settings.Port, _socket!.AddressFamily,
            cancellation.Token, _resolver);
        SocketAddress? remote = resolution.IsCompletedSuccessfully ? resolution.Result : null;
        // One socket can hold only one connection per remote address.
        if (remote != null && _byAddress.ContainsKey(remote))
        {
            remote = null;
        }

        ECDiffieHellman? exchange = null;
        byte[]? publicKey = null;
        if (Settings.Encryption != null)
        {
            exchange = KcpCipher.CreateKeyExchange(out publicKey);
        }

        KcpConnection connection = new(this, NextConnectionId(), address, true, remote, NowMs)
        {
            ClientNonce = RandomUInt32(), KeyExchange = exchange, PublicKey = publicKey
        };
        AddConnection(connection);
        if (!resolution.IsCompletedSuccessfully)
        {
            _resolving.Add(connection, new Resolution(resolution.AsTask(), cancellation, NowMs));
        }
        else
        {
            cancellation.Dispose();
            if (remote == null)
            {
                _unreachable.Add(connection);
            }
            else
            {
                _byAddress.Add(remote, connection);
            }
        }

        return connection;
    }

    public override void Receive()
    {
        ThrowIfDisposed();
        ProcessResolutions();
        CompleteAgreements(NowMs);
        foreach (KcpConnection connection in _unreachable)
        {
            RaiseDisconnected(connection, DisconnectReason.Unreachable);
        }

        _unreachable.Clear();

        if (_socket == null)
        {
            return;
        }

        long now = NowMs;
        if (_io != null)
        {
            for (int processed = 0;
                 processed < Settings.MaxDatagramsPerReceive && _io.TryTake(_receiveBuffer, ref _ioAddress, out int length);
                 processed++)
            {
                HandleDatagram(_ioAddress!, length, now);
            }

            return;
        }

        for (int processed = 0; processed < Settings.MaxDatagramsPerReceive; processed++)
        {
            int length;
            try
            {
                if (_socket.Available == 0)
                {
                    break;
                }

                length = _socket.ReceiveFrom(_receiveBuffer, SocketFlags.None, _receiveAddress!);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                break;
            }
            catch (SocketException)
            {
                // A reset or an oversized datagram. Either way that one packet is lost; keep reading.
                continue;
            }

            if (length == 0 || length > Settings.Mtu)
            {
                continue;
            }

            HandleDatagram(_receiveAddress!, length, now);
        }
    }

    public override void Flush()
    {
        ThrowIfDisposed();
        ProcessResolutions();
        long now = NowMs;
        CompleteAgreements(now);

        _scratch.AddRange(_byAddress.Values);
        foreach (KcpConnection connection in _scratch)
        {
            if (connection.State != ConnectionState.Disconnected)
            {
                connection.Tick(now);
            }
        }

        _scratch.Clear();

        SendDelayed(now, false);
        if (now >= _nextSweepMs)
        {
            SweepHandshakeState(now);
            _nextSweepMs = now + 1000;
        }
    }

    /// <summary>Forgets expired cookies and addresses that have no connections and a full handshake budget.</summary>
    private void SweepHandshakeState(long now)
    {
        while (_cookieExpiry.TryPeek(out (long ExpiresMs, ulong Cookie) oldest) && oldest.ExpiresMs <= now)
        {
            _cookieExpiry.Dequeue();
            _usedCookies.Remove(oldest.Cookie);
        }

        List<UInt128>? idle = null;
        foreach ((UInt128 ip, AddressBudget budget) in _byIp)
        {
            if (budget.Connections == 0 && budget.IsFull(now, Settings.MaxHandshakesPerAddressPerSecond))
            {
                (idle ??= new List<UInt128>()).Add(ip);
            }
        }

        if (idle != null)
        {
            foreach (UInt128 ip in idle)
            {
                _byIp.Remove(ip);
            }
        }
    }

    private void ProcessResolutions()
    {
        _scratch.AddRange(_resolving.Keys);
        foreach (KcpConnection connection in _scratch)
        {
            Resolution pending = _resolving[connection];
            if (NowMs - pending.Started >= Settings.ConnectTimeout.TotalMilliseconds)
            {
                Close(connection, DisconnectReason.Timeout, false);
                continue;
            }

            if (!pending.Task.IsCompleted)
            {
                continue;
            }

            _resolving.Remove(connection);
            pending.Cancellation.Dispose();
            SocketAddress? remote = pending.Task.GetAwaiter().GetResult();
            if (remote == null || _byAddress.ContainsKey(remote))
            {
                Close(connection, DisconnectReason.Unreachable, false);
            }
            else
            {
                connection.RemoteAddress = remote;
                _byAddress.Add(remote, connection);
            }
        }

        _scratch.Clear();
    }

    public override void Shutdown()
    {
        foreach (PendingAgreement pending in _agreements)
        {
            ReleaseAddressSlot(pending.IpKey);
            pending.Task.ContinueWith(static task => task.Result.Cipher?.Dispose(),
                TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        _agreements.Clear();

        _scratch.AddRange(_resolving.Keys);
        foreach (KcpConnection connection in _scratch)
        {
            Close(connection, DisconnectReason.Requested, false);
        }

        _scratch.Clear();
        foreach (KcpConnection connection in _unreachable)
        {
            RaiseDisconnected(connection, DisconnectReason.Unreachable);
        }

        _unreachable.Clear();

        _scratch.AddRange(_byAddress.Values);
        foreach (KcpConnection connection in _scratch)
        {
            Close(connection, DisconnectReason.Requested, true);
        }

        _scratch.Clear();

        // Simulated latency would otherwise swallow the disconnect packets just queued.
        SendDelayed(NowMs, true);

        _io?.StopSending();
        _socket?.Dispose();
        _io?.Dispose();
        _io = null;
        _ioAddress = null;
        _socket = null;
        _receiveAddress = null;
        _listening = false;
        _incomingCount = 0;
    }

    // --- Incoming datagrams -----------------------------------------------------------------

    private void HandleDatagram(SocketAddress from, int length, long now)
    {
        if (_byAddress.TryGetValue(from, out KcpConnection? connection))
        {
            connection.HandlePacket(_receiveBuffer, length, now);
            return;
        }

        ReadOnlySpan<byte> packet = _receiveBuffer.AsSpan(0, length);
        if (packet[0] >= (byte)KcpPacketType.Reliable)
        {
            FollowToNewAddress(from, length, now);
            return;
        }

        if (!_listening)
        {
            return;
        }

        switch ((KcpPacketType)packet[0])
        {
            case KcpPacketType.ConnectRequest:
                HandleConnectRequest(from, packet);
                break;

            case KcpPacketType.ChallengeResponse:
                HandleChallengeResponse(from, packet, now);
                break;
        }
    }

    /// <summary>
    ///     Handles a genuine packet of an encrypted session from an address the session isn't at. The session only
    ///     moves once that address echoes a path challenge, so replaying a packet from elsewhere can't redirect it.
    /// </summary>
    private void FollowToNewAddress(SocketAddress from, int length, long now)
    {
        if (length < KcpPacket.SessionHeaderSize || !_bySession.TryGetValue(
                BinaryPrimitives.ReadUInt32LittleEndian(_receiveBuffer.AsSpan(1)), out KcpConnection? connection))
        {
            return;
        }

        bool response = _receiveBuffer[0] == (byte)KcpPacketType.PathResponse;
        byte[] buffer = _receiveBuffer;
        if (!connection.Authenticate(ref buffer, ref length, !response))
        {
            return;
        }

        if (!response)
        {
            connection.Process(buffer, length, now, from);
            connection.ChallengePath(from, now);
        }
        else if (connection.ConfirmsPath(buffer.AsSpan(KcpPacket.SessionHeaderSize, length - KcpPacket.SessionHeaderSize),
                     from))
        {
            SocketAddress moved = Clone(from);
            _byAddress.Remove(connection.RemoteAddress!);
            _byAddress.Add(moved, connection);
            connection.MoveTo(moved, Format(moved));
        }
    }

    internal SocketAddress CloneAddress(SocketAddress address) => Clone(address);

    private void HandleConnectRequest(SocketAddress from, ReadOnlySpan<byte> packet)
    {
        if (packet.Length != KcpPacket.HandshakeRequestSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Settings.ProtocolId)
        {
            return;
        }

        uint nonce = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
        if (!MatchesSettings(packet))
        {
            SendReject(from, nonce, DisconnectReason.ProtocolError);
            return;
        }

        uint issued = (uint)NowMs;
        Span<byte> reply = stackalloc byte[KcpPacket.ChallengeSize];
        reply[0] = (byte)KcpPacketType.Challenge;
        BinaryPrimitives.WriteUInt32LittleEndian(reply[1..], nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(reply[5..], issued);
        ComputeCookie(from, nonce, issued, reply[9..]);
        SendRaw(null, from, reply);
    }

    private void HandleChallengeResponse(SocketAddress from, ReadOnlySpan<byte> packet, long now)
    {
        if (!TryReadChallengeResponse(from, packet, out uint nonce, out _))
        {
            return;
        }

        ulong cookie = BinaryPrimitives.ReadUInt64LittleEndian(packet[9..]);
        if (_usedCookies.Contains(cookie))
        {
            return;
        }

        if (_incomingCount + _agreements.Count >= Settings.MaxConnections)
        {
            SendReject(from, nonce, DisconnectReason.ServerFull);
            return;
        }

        UInt128 ip = IpKey(from);
        if (!_byIp.TryGetValue(ip, out AddressBudget? budget))
        {
            budget = new AddressBudget(Settings.MaxHandshakesPerAddressPerSecond, now);
            _byIp.Add(ip, budget);
        }

        if (budget.Connections >= Settings.MaxConnectionsPerAddress)
        {
            SendReject(from, nonce, DisconnectReason.Rejected);
            return;
        }

        if (!budget.TrySpend(now, Settings.MaxHandshakesPerAddressPerSecond) ||
            !_allHandshakes.TrySpend(now, Settings.MaxHandshakesPerSecond))
        {
            return;
        }

        ReadOnlySpan<byte> clientPublic = packet.Slice(KcpPacket.HandshakeKeyOffset, KcpCipher.PublicKeySize);
        if (Settings.Encryption != null && !KcpCipher.IsValidPublicKey(clientPublic))
        {
            return;
        }

        _usedCookies.Add(cookie);
        _cookieExpiry.Enqueue((now + CookieLifetimeMs, cookie));

        uint sessionId;
        do
        {
            sessionId = RandomUInt32();
        } while (_bySession.ContainsKey(sessionId) || _agreements.Exists(a => a.SessionId == sessionId));

        budget.Connections++;
        if (Settings.Encryption == null)
        {
            Accept(Clone(from), nonce, sessionId, ip, null, null, now);
            return;
        }

        // Key agreement and signing take a couple of milliseconds, so they run on the thread pool. The connection
        // appears on a later Receive or Flush.
        byte[] key = clientPublic.ToArray();
        _agreements.Add(new PendingAgreement(Task.Run(() => AgreeKeys(key, nonce, sessionId)), Clone(from), nonce,
            sessionId, ip));
    }

    /// <summary>Creates connections for key agreements that have finished.</summary>
    private void CompleteAgreements(long now)
    {
        for (int i = _agreements.Count - 1; i >= 0; i--)
        {
            PendingAgreement pending = _agreements[i];
            if (!pending.Task.IsCompleted)
            {
                continue;
            }

            _agreements.RemoveAt(i);
            (KcpCipher? cipher, byte[]? accept) = pending.Task.IsCompletedSuccessfully ? pending.Task.Result : default;
            if (cipher == null || _byAddress.ContainsKey(pending.From))
            {
                cipher?.Dispose();
                ReleaseAddressSlot(pending.IpKey);
                continue;
            }

            Accept(pending.From, pending.Nonce, pending.SessionId, pending.IpKey, cipher, accept, now);
        }
    }

    private void ReleaseAddressSlot(UInt128 ip)
    {
        if (_byIp.TryGetValue(ip, out AddressBudget? budget))
        {
            budget.Connections--;
        }
    }

    /// <summary>Opens an incoming connection whose handshake is complete, with its address slot already taken.</summary>
    private void Accept(SocketAddress remote, uint nonce, uint sessionId, UInt128 ip, KcpCipher? cipher,
        byte[]? accept, long now)
    {
        KcpConnection connection = new(this, NextConnectionId(), Format(remote), false, remote, now)
        {
            ClientNonce = nonce, SessionId = sessionId, Cipher = cipher, AcceptPacket = accept, IpKey = ip
        };
        connection.StartSession(now);

        AddConnection(connection);
        _byAddress.Add(remote, connection);
        if (cipher != null)
        {
            _bySession.Add(sessionId, connection);
        }

        _incomingCount++;

        SendAccept(connection);
        RaiseConnected(connection);
    }

    /// <summary>
    ///     Host, on the thread pool: answers the client's public key with its own, signed if it has a key, and derives
    ///     the session keys. Both are null if the key agreement fails.
    /// </summary>
    private (KcpCipher? Cipher, byte[]? Accept) AgreeKeys(byte[] clientPublic, uint nonce, uint sessionId)
    {
        using ECDiffieHellman exchange = KcpCipher.CreateKeyExchange(out byte[] hostPublic);
        byte[] accept = new byte[KcpPacket.EncryptedAcceptSize];
        KcpCipher? cipher = KcpCipher.Create(exchange, clientPublic, hostPublic, nonce, sessionId, false,
            accept.AsSpan(KcpPacket.EncryptedAcceptSize - KcpCipher.ConfirmationSize));
        if (cipher == null)
        {
            return default;
        }

        accept[0] = (byte)KcpPacketType.Accept;
        BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(1), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(5), sessionId);
        hostPublic.CopyTo(accept, KcpPacket.AcceptSize);
        if (Settings.Encryption!.SigningKey is { } signer)
        {
            byte[] transcript = KcpCipher.Transcript(clientPublic, hostPublic, nonce, sessionId, Settings.ProtocolId);
            lock (_signing)
            {
                signer.SignData(transcript, accept.AsSpan(KcpPacket.AcceptSize + KcpCipher.PublicKeySize),
                    HashAlgorithmName.SHA256);
            }
        }

        return (cipher, accept);
    }

    /// <summary>
    ///     A handshake packet from the address of an existing incoming connection: either the client never
    ///     got our Accept, or it restarted and is connecting again from the same address.
    /// </summary>
    internal void HandleRepeatHandshake(KcpConnection connection, ReadOnlySpan<byte> packet, long now)
    {
        SocketAddress remote = connection.RemoteAddress!;

        switch ((KcpPacketType)packet[0])
        {
            case KcpPacketType.ConnectRequest:
                HandleConnectRequest(remote, packet);
                break;

            case KcpPacketType.ChallengeResponse:
                if (!TryReadChallengeResponse(remote, packet, out uint nonce, out uint issued))
                {
                    return;
                }

                if (nonce == connection.ClientNonce)
                {
                    SendAccept(connection);
                    return;
                }

                // Only an attempt that began after this session started may replace it. An older cookie is a
                // delayed or replayed handshake from an attempt that is already over.
                if (!IssuedSince(issued, connection.SessionStartMs, now))
                {
                    return;
                }

                // A new attempt from the same address, so the old session is gone on the client's end.
                Close(connection, DisconnectReason.RemoteClosed, false);
                HandleChallengeResponse(remote, packet, now);
                break;
        }
    }

    /// <summary>Whether a cookie issued at <paramref name="issued" />, a truncated clock reading, dates from <paramref name="sinceMs" /> or later.</summary>
    internal static bool IssuedSince(uint issued, long sinceMs, long nowMs) => nowMs - ((uint)nowMs - issued) >= sinceMs;

    private bool TryReadChallengeResponse(SocketAddress from, ReadOnlySpan<byte> packet, out uint nonce,
        out uint issued)
    {
        nonce = 0;
        issued = 0;
        if (packet.Length != KcpPacket.HandshakeRequestSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Settings.ProtocolId || !MatchesSettings(packet))
        {
            return false;
        }

        nonce = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
        issued = BinaryPrimitives.ReadUInt32LittleEndian(packet[KcpPacket.HandshakeIssuedOffset..]);
        if ((uint)NowMs - issued >= CookieLifetimeMs)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[KcpPacket.CookieSize];
        ComputeCookie(from, nonce, issued, expected);
        return CryptographicOperations.FixedTimeEquals(packet.Slice(9, KcpPacket.CookieSize), expected);
    }

    /// <summary>Whether a handshake request comes from a build with the same wire format and connection settings.</summary>
    private bool MatchesSettings(ReadOnlySpan<byte> packet) =>
        packet[KcpPacket.HandshakeVersionOffset] == KcpPacket.WireVersion &&
        packet[KcpPacket.HandshakeChannelsOffset] == Settings.Channels &&
        BinaryPrimitives.ReadUInt16LittleEndian(packet[KcpPacket.HandshakeMtuOffset..]) == Settings.Mtu &&
        BinaryPrimitives.ReadUInt16LittleEndian(packet[KcpPacket.HandshakeWindowOffset..]) ==
        Settings.Reliability.ReceiveWindow &&
        packet[KcpPacket.HandshakeFlagsOffset] == Flags;

    private byte Flags => Settings.Encryption != null ? KcpPacket.EncryptedFlag : (byte)0;

    private void WriteSettings(Span<byte> packet)
    {
        packet[KcpPacket.HandshakeVersionOffset] = KcpPacket.WireVersion;
        packet[KcpPacket.HandshakeChannelsOffset] = (byte)Settings.Channels;
        BinaryPrimitives.WriteUInt16LittleEndian(packet[KcpPacket.HandshakeMtuOffset..], (ushort)Settings.Mtu);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[KcpPacket.HandshakeWindowOffset..],
            (ushort)Settings.Reliability.ReceiveWindow);
        packet[KcpPacket.HandshakeFlagsOffset] = Flags;
    }

    private void SendReject(SocketAddress to, uint nonce, DisconnectReason reason)
    {
        Span<byte> reject = stackalloc byte[KcpPacket.RejectSize];
        reject[0] = (byte)KcpPacketType.Reject;
        BinaryPrimitives.WriteUInt32LittleEndian(reject[1..], nonce);
        reject[5] = (byte)reason;
        SendRaw(null, to, reject);
    }

    private void ComputeCookie(SocketAddress address, uint nonce, uint issued, Span<byte> destination)
    {
        Span<byte> input = stackalloc byte[address.Size + 8];
        address.Buffer.Span[..address.Size].CopyTo(input);
        BinaryPrimitives.WriteUInt32LittleEndian(input[address.Size..], nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(input[(address.Size + 4)..], issued);

        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(_cookieKey, input, hash);
        hash[..KcpPacket.CookieSize].CopyTo(destination);
    }

    // --- Outgoing datagrams -----------------------------------------------------------------

    internal void SendConnectRequest(KcpConnection connection)
    {
        Span<byte> packet = stackalloc byte[KcpPacket.HandshakeRequestSize];
        packet.Clear();
        packet[0] = (byte)KcpPacketType.ConnectRequest;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], Settings.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.ClientNonce);
        WriteSettings(packet);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    internal void SendChallengeResponse(KcpConnection connection, ReadOnlySpan<byte> cookie, uint issued)
    {
        Span<byte> packet = stackalloc byte[KcpPacket.HandshakeRequestSize];
        packet.Clear();
        packet[0] = (byte)KcpPacketType.ChallengeResponse;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], Settings.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.ClientNonce);
        cookie.CopyTo(packet[9..]);
        WriteSettings(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[KcpPacket.HandshakeIssuedOffset..], issued);
        connection.PublicKey?.CopyTo(packet[KcpPacket.HandshakeKeyOffset..]);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    private void SendAccept(KcpConnection connection)
    {
        if (connection.AcceptPacket != null)
        {
            SendRaw(connection, connection.RemoteAddress!, connection.AcceptPacket);
            return;
        }

        Span<byte> packet = stackalloc byte[KcpPacket.AcceptSize];
        packet[0] = (byte)KcpPacketType.Accept;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], connection.ClientNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.SessionId);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    /// <summary>Sends <c>[type][sessionId][body]</c> to the connection's remote end.</summary>
    internal void SendSession(KcpConnection connection, KcpPacketType type, ReadOnlySpan<byte> body) =>
        SendSession(connection, type, ReadOnlySpan<byte>.Empty, body);

    /// <summary>
    ///     Sends <c>[type][sessionId][prefix][body]</c>, the prefix being a data packet's channel and sequence, to
    ///     <paramref name="to" /> if given, or else the connection's own address.
    /// </summary>
    internal void SendSession(KcpConnection connection, KcpPacketType type, ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> body, SocketAddress? to = null)
    {
        // An encrypted session without its keys, such as one already closed, sends nothing rather than plaintext.
        if (Settings.Encryption != null && connection.Cipher == null)
        {
            return;
        }

        int length = KcpPacket.SessionHeaderSize + prefix.Length + body.Length;
        int sealedLength = length + (connection.Cipher != null ? KcpCipher.Overhead : 0);
        if (_sendBuffer.Length < sealedLength)
        {
            _sendBuffer = new byte[Math.Max(sealedLength, Settings.Mtu)];
        }

        Span<byte> packet = _sendBuffer.AsSpan(0, length);
        packet[0] = (byte)type;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], connection.SessionId);
        if (connection.Cipher is { } cipher)
        {
            Span<byte> plain = _plainBuffer.AsSpan(0, prefix.Length + body.Length);
            prefix.CopyTo(plain);
            body.CopyTo(plain[prefix.Length..]);
            int written = cipher.Seal(packet[..KcpPacket.SessionHeaderSize], plain, _sendBuffer);
            SendRaw(connection, to ?? connection.RemoteAddress!, _sendBuffer.AsSpan(0, written));
            return;
        }

        prefix.CopyTo(packet[KcpPacket.SessionHeaderSize..]);
        body.CopyTo(packet[(KcpPacket.SessionHeaderSize + prefix.Length)..]);
        SendRaw(connection, to ?? connection.RemoteAddress!, packet);
    }

    private void SendRaw(KcpConnection? connection, SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        if (_socket == null)
        {
            return;
        }

        if (Settings.Simulation is { } simulation && Simulate(simulation, to, datagram))
        {
            connection?.RecordSent(datagram.Length);
            return;
        }

        if (SendNow(to, datagram))
        {
            connection?.RecordSent(datagram.Length);
        }
        else
        {
            connection?.RecordDropped();
        }
    }

    /// <summary>Hands the datagram to the socket, or to the send thread. False only if the send queue is full.</summary>
    private bool SendNow(SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        if (_io != null)
        {
            return _io.Send(to, datagram);
        }

        try
        {
            _socket?.SendTo(datagram, SocketFlags.None, to);
        }
        catch (SocketException)
        {
            // UDP makes no promises; a failed send is just a lost packet.
        }

        return true;
    }

    /// <summary>Decrypts a sealed session packet into <c>[type][sessionId][body]</c> in a scratch buffer.</summary>
    internal bool Open(KcpConnection connection, ReadOnlySpan<byte> packet, bool newestOnly, out byte[] plain,
        out int length)
    {
        plain = _openBuffer;
        packet[..KcpPacket.SessionHeaderSize].CopyTo(plain);
        bool opened = connection.Cipher!.Open(packet, KcpPacket.SessionHeaderSize,
            plain.AsSpan(KcpPacket.SessionHeaderSize), out int body, newestOnly);
        length = KcpPacket.SessionHeaderSize + body;
        return opened;
    }

    // --- Connection lifecycle -----------------------------------------------------------------

    internal void Close(KcpConnection connection, DisconnectReason reason, bool notifyRemote)
    {
        if (connection.State == ConnectionState.Disconnected)
        {
            return;
        }

        if (notifyRemote && connection.State == ConnectionState.Connected)
        {
            // Last chance for queued reliable messages to go out, then say goodbye.
            if (reason != DisconnectReason.ResourceLimit)
            {
                connection.FlushReliable(NowMs);
            }

            for (int i = 0; i < DisconnectRepeats; i++)
            {
                SendSession(connection, KcpPacketType.Disconnect, ReadOnlySpan<byte>.Empty);
            }
        }

        if (_resolving.Remove(connection, out Resolution resolution))
        {
            resolution.Cancellation.Cancel();
            resolution.Cancellation.Dispose();
        }

        _unreachable.Remove(connection);
        if (_bySession.TryGetValue(connection.SessionId, out KcpConnection? listed) && listed == connection)
        {
            _bySession.Remove(connection.SessionId);
        }

        if (connection.RemoteAddress != null && _byAddress.Remove(connection.RemoteAddress) && !connection.IsOutgoing)
        {
            _incomingCount--;
            if (_byIp.TryGetValue(connection.IpKey, out AddressBudget? budget))
            {
                budget.Connections--;
            }
        }

        RaiseDisconnected(connection, reason);
        connection.ReleaseBuffers();
    }

    protected override void DisposeCore() => TrustedHostKey?.Dispose();

    protected override void OnReceiveQueueOverflow(Connection connection) =>
        Close((KcpConnection)connection, DisconnectReason.ResourceLimit, true);

    internal void OnConnected(KcpConnection connection)
    {
        if (connection.Cipher != null)
        {
            _bySession.TryAdd(connection.SessionId, connection);
        }

        RaiseConnected(connection);
    }

    internal void OnData(KcpConnection connection, ReadOnlySpan<byte> data, Delivery delivery, byte channel) =>
        RaiseData(connection, data, delivery, channel);

    internal void OnDataPooled(KcpConnection connection, byte[] rentedBuffer, int length, Delivery delivery,
        byte channel) =>
        RaiseDataPooled(connection, rentedBuffer, length, delivery, channel);

    // --- Link simulation ------------------------------------------------------------------------

    /// <summary>Drops or delays the datagram. Returns false if it should be sent right away.</summary>
    private bool Simulate(LinkSimulation simulation, SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        Random random = _simulationRandom ??= simulation.Seed is int seed ? new Random(seed) : new Random();

        if (simulation.PacketLoss > 0 && random.NextDouble() < simulation.PacketLoss)
        {
            return true;
        }

        double delayMs = simulation.Latency.TotalMilliseconds +
                         random.NextDouble() * simulation.Jitter.TotalMilliseconds;
        if (delayMs <= 0)
        {
            return false;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(datagram.Length);
        datagram.CopyTo(buffer);
        _delayed.Add(new DelayedPacket(Clone(to), buffer, datagram.Length, NowMs + (long)delayMs));
        return true;
    }

    private void SendDelayed(long now, bool all)
    {
        if (_delayed.Count == 0)
        {
            return;
        }

        // Keep the waiting packets in order, so pure latency without jitter never reorders anything.
        int kept = 0;
        for (int i = 0; i < _delayed.Count; i++)
        {
            DelayedPacket packet = _delayed[i];
            if (all || packet.DueMs <= now)
            {
                SendNow(packet.To, packet.Buffer.AsSpan(0, packet.Length));
                ArrayPool<byte>.Shared.Return(packet.Buffer);
            }
            else
            {
                _delayed[kept++] = packet;
            }
        }

        _delayed.RemoveRange(kept, _delayed.Count - kept);
    }

    // --- Sockets and addresses ------------------------------------------------------------------

    private void OpenSocket(int port)
    {
        IPAddress? bind = Settings.BindAddress;
        Socket socket = bind != null
            ? new Socket(bind.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            : CreateDualModeSocket() ?? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            IPAddress any = socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
            socket.Bind(new IPEndPoint(bind ?? any, port));
            socket.Blocking = false;
            socket.ReceiveBufferSize = Settings.SocketBufferSize;
            socket.SendBufferSize = Settings.SocketBufferSize;

            if (OperatingSystem.IsWindows())
            {
                socket.IOControl(SioUdpConnReset, new byte[4], null);
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _socket = socket;
        if (Settings.UseIoThreads)
        {
            _io = new KcpIoThreads(socket, Settings.Mtu, Settings.MaxIoQueueBytes);
        }

        _receiveAddress =
            new SocketAddress(socket.AddressFamily, SocketAddress.GetMaximumAddressSize(socket.AddressFamily));
        _sendBuffer = new byte[Settings.Mtu + KcpCipher.Overhead];
    }

    private static Socket? CreateDualModeSocket()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return null;
        }

        Socket socket = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.DualMode = true;
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>Resolves an address to the form the socket sends to and receives from, or null if it can't.</summary>
    internal static async ValueTask<SocketAddress?> TryResolveAsync(string address, int defaultPort,
        AddressFamily socketFamily,
        CancellationToken cancellation, Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        IPAddress? ip;
        int port;

        if (IPEndPoint.TryParse(address, out IPEndPoint? literal))
        {
            ip = literal.Address;
            port = literal.Port;
        }
        else
        {
            // A host name, optionally with a port. Names can't contain ':', so a single one is the separator.
            string host = address;
            port = 0;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon)
            {
                if (!int.TryParse(address.AsSpan(colon + 1), out port) || port < 0 || port > 65535)
                {
                    return null;
                }

                host = address[..colon];
            }

            ip = await ResolveHostAsync(host, socketFamily, cancellation, resolver).ConfigureAwait(false);
            if (ip == null)
            {
                return null;
            }
        }

        if (port == 0)
        {
            port = defaultPort;
        }

        if (socketFamily == AddressFamily.InterNetworkV6)
        {
            ip = ip.MapToIPv6();
        }
        else if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        return new IPEndPoint(ip, port).Serialize();
    }

    private static async ValueTask<IPAddress?> ResolveHostAsync(string host, AddressFamily socketFamily,
        CancellationToken cancellation, Func<string, CancellationToken, Task<IPAddress[]>>? resolver)
    {
        IPAddress[] candidates;
        try
        {
            candidates =
                await (resolver == null ? Dns.GetHostAddressesAsync(host, cancellation) : resolver(host, cancellation))
                    .ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or ArgumentException or OperationCanceledException)
        {
            return null;
        }

        // Prefer IPv4: it works from both kinds of socket and is what most hosts listen on.
        IPAddress? fallback = null;
        foreach (IPAddress candidate in candidates)
        {
            if (candidate.AddressFamily == AddressFamily.InterNetwork)
            {
                return candidate;
            }

            if (candidate.AddressFamily == AddressFamily.InterNetworkV6 && socketFamily == AddressFamily.InterNetworkV6)
            {
                fallback ??= candidate;
            }
        }

        return fallback;
    }

    internal static string Format(SocketAddress address)
    {
        IPAddress any = address.Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
        IPEndPoint endpoint = (IPEndPoint)new IPEndPoint(any, 0).Create(address);
        if (endpoint.Address.IsIPv4MappedToIPv6)
        {
            endpoint = new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port);
        }

        return endpoint.ToString();
    }

    private static SocketAddress Clone(SocketAddress address)
    {
        SocketAddress copy = new(address.Family, address.Size);
        address.Buffer.Span[..address.Size].CopyTo(copy.Buffer.Span);
        return copy;
    }

    /// <summary>Groups addresses the way one machine or one home network would hold them: an IPv4 address, or an IPv6 /64.</summary>
    internal static UInt128 IpKey(SocketAddress address)
    {
        ReadOnlySpan<byte> bytes = address.Buffer.Span;
        if (address.Family == AddressFamily.InterNetwork)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        }

        // sockaddr_in6: family, port, flow info, then the 16 byte address.
        ReadOnlySpan<byte> ip = bytes.Slice(8, 16);
        bool mappedIPv4 = ip[..10].IndexOfAnyExcept((byte)0) < 0 && ip[10] == 0xFF && ip[11] == 0xFF;
        UInt128 key = BinaryPrimitives.ReadUInt128BigEndian(ip);
        return mappedIPv4 ? key : key >> 64 << 64;
    }

    private static uint RandomUInt32()
    {
        Span<byte> bytes = stackalloc byte[4];
        uint value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        } while (value == 0);

        return value;
    }

    private readonly record struct Resolution(
        Task<SocketAddress?> Task,
        CancellationTokenSource Cancellation,
        long Started);

    private sealed class AddressBudget(int perSecond, long now)
    {
        private double _tokens = perSecond;
        private long _updatedMs = now;
        public int Connections;

        public bool TrySpend(long now, int perSecond)
        {
            Refill(now, perSecond);
            if (_tokens < 1)
            {
                return false;
            }

            _tokens--;
            return true;
        }

        public bool IsFull(long now, int perSecond)
        {
            Refill(now, perSecond);
            return _tokens >= perSecond;
        }

        private void Refill(long now, int perSecond)
        {
            _tokens = Math.Min(perSecond, _tokens + perSecond * (now - _updatedMs) / 1000.0);
            _updatedMs = now;
        }
    }

    private readonly record struct PendingAgreement(
        Task<(KcpCipher? Cipher, byte[]? Accept)> Task,
        SocketAddress From,
        uint Nonce,
        uint SessionId,
        UInt128 IpKey);

    private readonly record struct DelayedPacket(SocketAddress To, byte[] Buffer, int Length, long DueMs);
}
