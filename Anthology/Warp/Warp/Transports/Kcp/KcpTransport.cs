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

    // How often the cookie key rotates. A cookie is accepted for one to two of these.
    private const long CookieWindowMs = 10_000;

    // Disconnect packets are unreliable, so send a few and hope one lands. The remote end times out otherwise.
    private const int DisconnectRepeats = 3;

    private readonly Dictionary<SocketAddress, KcpConnection> _byAddress = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _cookieKey = RandomNumberGenerator.GetBytes(32);

    private readonly List<DelayedPacket> _delayed = new();

    private readonly byte[] _receiveBuffer = new byte[KcpPacket.MaxDatagramSize];
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolver;
    private readonly Dictionary<KcpConnection, Resolution> _resolving = new();
    private readonly List<KcpConnection> _scratch = new();
    private readonly List<KcpConnection> _unreachable = new();
    private int _incomingCount;
    private bool _listening;
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
    }

    internal KcpTransportOptions Settings { get; }

    /// <summary>A detached copy of the settings captured when this transport was created.</summary>
    public KcpTransportOptions Options => Settings.CopyValidated();

    public override bool IsListening => _listening;

    public override TransportCapabilities Capabilities =>
        new(true, DeliverySupport.All, TransportExecution.WorkerPreferred, Settings.Channels);

    /// <summary>The port the socket is bound to, or 0 if it isn't bound yet.</summary>
    public override int LocalPort => (_socket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    internal long NowMs => _clock.ElapsedMilliseconds;

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
        if (remote != null && _byAddress.ContainsKey(remote))
        {
            cancellation.Dispose();
            throw new InvalidOperationException($"Already connected to {address}.");
        }

        KcpConnection connection = new(this, NextConnectionId(), address, true, remote, NowMs)
        {
            ClientNonce = RandomUInt32()
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

        _socket?.Dispose();
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

        if (!_listening)
        {
            return;
        }

        ReadOnlySpan<byte> packet = _receiveBuffer.AsSpan(0, length);
        switch ((KcpPacketType)packet[0])
        {
            case KcpPacketType.ConnectRequest:
                HandleConnectRequest(from, packet, out _);
                break;

            case KcpPacketType.ChallengeResponse:
                HandleChallengeResponse(from, packet, now);
                break;
        }
    }

    private bool HandleConnectRequest(SocketAddress from, ReadOnlySpan<byte> packet, out uint nonce)
    {
        nonce = 0;
        if (packet.Length != KcpPacket.HandshakeRequestSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Settings.ProtocolId)
        {
            return false;
        }

        nonce = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
        if (!MatchesVersion(packet))
        {
            SendReject(from, nonce, DisconnectReason.ProtocolError);
            return false;
        }

        Span<byte> reply = stackalloc byte[KcpPacket.ChallengeSize];
        reply[0] = (byte)KcpPacketType.Challenge;
        BinaryPrimitives.WriteUInt32LittleEndian(reply[1..], nonce);
        ComputeCookie(from, nonce, NowMs / CookieWindowMs, reply[5..]);
        SendRaw(null, from, reply);
        return true;
    }

    private void HandleChallengeResponse(SocketAddress from, ReadOnlySpan<byte> packet, long now)
    {
        if (!TryReadChallengeResponse(from, packet, out uint nonce))
        {
            return;
        }

        if (_incomingCount >= Settings.MaxConnections)
        {
            SendReject(from, nonce, DisconnectReason.ServerFull);
            return;
        }

        SocketAddress remote = Clone(from);
        KcpConnection connection = new(this, NextConnectionId(), Format(remote), false, remote, now)
        {
            ClientNonce = nonce, SessionId = RandomUInt32()
        };
        connection.StartSession(now);

        AddConnection(connection);
        _byAddress.Add(remote, connection);
        _incomingCount++;

        SendAccept(connection);
        RaiseConnected(connection);
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
                if (HandleConnectRequest(remote, packet, out uint requestNonce) && requestNonce != connection.ClientNonce)
                {
                    connection.RestartNonce = requestNonce;
                }

                break;

            case KcpPacketType.ChallengeResponse:
                if (!TryReadChallengeResponse(remote, packet, out uint nonce))
                {
                    return;
                }

                if (nonce == connection.ClientNonce)
                {
                    SendAccept(connection);
                    return;
                }

                // Only the attempt that asked to restart since this session began may replace it. Anything
                // else is a delayed or replayed response from an attempt that is already over.
                if (nonce != connection.RestartNonce)
                {
                    return;
                }

                // A new attempt from the same address, so the old session is gone on the client's end.
                Close(connection, DisconnectReason.RemoteClosed, false);
                HandleChallengeResponse(remote, packet, now);
                break;
        }
    }

    private bool TryReadChallengeResponse(SocketAddress from, ReadOnlySpan<byte> packet, out uint nonce)
    {
        nonce = 0;
        if (packet.Length != KcpPacket.HandshakeRequestSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Settings.ProtocolId || !MatchesVersion(packet))
        {
            return false;
        }

        nonce = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
        ReadOnlySpan<byte> cookie = packet.Slice(9, KcpPacket.CookieSize);

        // Accept cookies from this key window and the one before, so a handshake that straddles a
        // rotation still succeeds.
        long window = NowMs / CookieWindowMs;
        Span<byte> expected = stackalloc byte[KcpPacket.CookieSize];
        ComputeCookie(from, nonce, window, expected);
        if (CryptographicOperations.FixedTimeEquals(cookie, expected))
        {
            return true;
        }

        ComputeCookie(from, nonce, window - 1, expected);
        return CryptographicOperations.FixedTimeEquals(cookie, expected);
    }

    /// <summary>Whether a handshake request comes from a build with this wire format and channel count.</summary>
    private bool MatchesVersion(ReadOnlySpan<byte> packet) =>
        packet[KcpPacket.HandshakeVersionOffset] == KcpPacket.WireVersion &&
        packet[KcpPacket.HandshakeVersionOffset + 1] == Settings.Channels;

    private void WriteVersion(Span<byte> packet)
    {
        packet[KcpPacket.HandshakeVersionOffset] = KcpPacket.WireVersion;
        packet[KcpPacket.HandshakeVersionOffset + 1] = (byte)Settings.Channels;
    }

    private void SendReject(SocketAddress to, uint nonce, DisconnectReason reason)
    {
        Span<byte> reject = stackalloc byte[KcpPacket.RejectSize];
        reject[0] = (byte)KcpPacketType.Reject;
        BinaryPrimitives.WriteUInt32LittleEndian(reject[1..], nonce);
        reject[5] = (byte)reason;
        SendRaw(null, to, reject);
    }

    private void ComputeCookie(SocketAddress address, uint nonce, long window, Span<byte> destination)
    {
        Span<byte> input = stackalloc byte[address.Size + 12];
        address.Buffer.Span[..address.Size].CopyTo(input);
        BinaryPrimitives.WriteUInt32LittleEndian(input[address.Size..], nonce);
        BinaryPrimitives.WriteInt64LittleEndian(input[(address.Size + 4)..], window);

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
        WriteVersion(packet);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    internal void SendChallengeResponse(KcpConnection connection, ReadOnlySpan<byte> cookie)
    {
        Span<byte> packet = stackalloc byte[KcpPacket.HandshakeRequestSize];
        packet.Clear();
        packet[0] = (byte)KcpPacketType.ChallengeResponse;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], Settings.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.ClientNonce);
        cookie.CopyTo(packet[9..]);
        WriteVersion(packet);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    private void SendAccept(KcpConnection connection)
    {
        Span<byte> packet = stackalloc byte[KcpPacket.AcceptSize];
        packet[0] = (byte)KcpPacketType.Accept;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], connection.ClientNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.SessionId);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    /// <summary>Sends <c>[type][sessionId][body]</c> to the connection's remote end.</summary>
    internal void SendSession(KcpConnection connection, KcpPacketType type, ReadOnlySpan<byte> body) =>
        SendSession(connection, type, ReadOnlySpan<byte>.Empty, body);

    /// <summary>Sends <c>[type][sessionId][prefix][body]</c>, the prefix being a data packet's channel and sequence.</summary>
    internal void SendSession(KcpConnection connection, KcpPacketType type, ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> body)
    {
        int length = KcpPacket.SessionHeaderSize + prefix.Length + body.Length;
        if (_sendBuffer.Length < length)
        {
            _sendBuffer = new byte[Math.Max(length, Settings.Mtu)];
        }

        Span<byte> packet = _sendBuffer.AsSpan(0, length);
        packet[0] = (byte)type;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], connection.SessionId);
        prefix.CopyTo(packet[KcpPacket.SessionHeaderSize..]);
        body.CopyTo(packet[(KcpPacket.SessionHeaderSize + prefix.Length)..]);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    private void SendRaw(KcpConnection? connection, SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        if (_socket == null)
        {
            return;
        }

        connection?.RecordSent(datagram.Length);

        if (Settings.Simulation is { } simulation && Simulate(simulation, to, datagram))
        {
            return;
        }

        SendNow(to, datagram);
    }

    private void SendNow(SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        try
        {
            _socket?.SendTo(datagram, SocketFlags.None, to);
        }
        catch (SocketException)
        {
            // UDP makes no promises; a failed send is just a lost packet.
        }
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
        if (connection.RemoteAddress != null && _byAddress.Remove(connection.RemoteAddress) && !connection.IsOutgoing)
        {
            _incomingCount--;
        }

        RaiseDisconnected(connection, reason);
        connection.ReleaseBuffers();
    }

    protected override void OnReceiveQueueOverflow(Connection connection) =>
        Close((KcpConnection)connection, DisconnectReason.ResourceLimit, true);

    internal void OnConnected(KcpConnection connection) => RaiseConnected(connection);

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
        Socket socket = CreateDualModeSocket() ??
                        new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            IPAddress any = socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
            socket.Bind(new IPEndPoint(any, port));
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
        _receiveAddress =
            new SocketAddress(socket.AddressFamily, SocketAddress.GetMaximumAddressSize(socket.AddressFamily));
        _sendBuffer = new byte[Settings.Mtu];
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

    private readonly record struct DelayedPacket(SocketAddress To, byte[] Buffer, int Length, long DueMs);
}
