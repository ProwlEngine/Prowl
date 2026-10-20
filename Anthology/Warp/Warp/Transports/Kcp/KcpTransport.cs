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

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
/// A transport over UDP: <see cref="Delivery.Reliable"/> messages are carried by an embedded KCP
/// implementation, <see cref="Delivery.Unreliable"/> ones as plain datagrams. One socket serves both
/// incoming and outgoing connections. Addresses are <c>host:port</c>, <c>host</c> (using
/// <see cref="KcpTransportOptions.Port"/>), or <c>[ipv6]:port</c>.
/// </summary>
/// <remarks>
/// If the OS supports it, the socket is dual-mode IPv6, so it reaches IPv4 and IPv6 peers alike.
/// Host names passed to <see cref="Connect"/> are resolved synchronously.
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
    private readonly List<KcpConnection> _scratch = new();
    private readonly List<KcpConnection> _unreachable = new();
    private readonly List<DelayedPacket> _delayed = new();
    private readonly byte[] _receiveBuffer = new byte[KcpPacket.MaxDatagramSize];
    private readonly byte[] _cookieKey = RandomNumberGenerator.GetBytes(32);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private byte[] _sendBuffer = Array.Empty<byte>();
    private Socket? _socket;
    private SocketAddress? _receiveAddress;
    private Random? _simulationRandom;
    private bool _listening;
    private int _incomingCount;

    public KcpTransport(KcpTransportOptions? options = null)
    {
        Options = options ?? new KcpTransportOptions();
        Options.Validate();
    }

    /// <summary>The settings this transport uses. Changes apply to sockets and connections created afterwards.</summary>
    public KcpTransportOptions Options { get; }

    public override bool IsListening => _listening;

    /// <summary>The port the socket is bound to, or 0 if it isn't bound yet.</summary>
    public int LocalPort => (_socket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    internal long NowMs => _clock.ElapsedMilliseconds;

    /// <exception cref="InvalidOperationException">The socket is already bound to a random port for outgoing connections.</exception>
    /// <exception cref="SocketException">The port is in use.</exception>
    public override void Listen()
    {
        if (_listening)
            return;

        if (_socket != null)
            throw new InvalidOperationException("The socket is already bound for outgoing connections. Call Listen before Connect.");

        Options.Validate();
        OpenSocket(Options.Port);
        _listening = true;
    }

    public override Connection Connect(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Options.Validate();

        if (_socket == null)
            OpenSocket(0);

        SocketAddress? remote = TryResolve(address, Options.Port, _socket!.AddressFamily);
        if (remote == null)
        {
            // Reported on the next Receive, like every other way a connection attempt can fail.
            KcpConnection unreachable = new(this, NextConnectionId(), address, isOutgoing: true, remote: null, NowMs);
            AddConnection(unreachable);
            _unreachable.Add(unreachable);
            return unreachable;
        }

        if (_byAddress.ContainsKey(remote))
            throw new InvalidOperationException($"Already connected to {address}.");

        KcpConnection connection = new(this, NextConnectionId(), address, isOutgoing: true, remote, NowMs)
        {
            ClientNonce = RandomUInt32(),
        };
        AddConnection(connection);
        _byAddress.Add(remote, connection);
        return connection;
    }

    public override void Receive()
    {
        foreach (KcpConnection connection in _unreachable)
            RaiseDisconnected(connection, DisconnectReason.Unreachable);
        _unreachable.Clear();

        if (_socket == null)
            return;

        long now = NowMs;
        while (true)
        {
            int length;
            try
            {
                if (_socket.Available == 0)
                    break;

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

            if (length == 0 || length > Options.Mtu)
                continue;

            HandleDatagram(_receiveAddress!, length, now);
        }
    }

    public override void Flush()
    {
        long now = NowMs;

        _scratch.AddRange(_byAddress.Values);
        foreach (KcpConnection connection in _scratch)
        {
            if (connection.State != ConnectionState.Disconnected)
                connection.Tick(now);
        }
        _scratch.Clear();

        SendDelayed(now, all: false);
    }

    public override void Shutdown()
    {
        foreach (KcpConnection connection in _unreachable)
            RaiseDisconnected(connection, DisconnectReason.Unreachable);
        _unreachable.Clear();

        _scratch.AddRange(_byAddress.Values);
        foreach (KcpConnection connection in _scratch)
            Close(connection, DisconnectReason.Requested, notifyRemote: true);
        _scratch.Clear();

        // Simulated latency would otherwise swallow the disconnect packets just queued.
        SendDelayed(NowMs, all: true);

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
            return;

        ReadOnlySpan<byte> packet = _receiveBuffer.AsSpan(0, length);
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

    private void HandleConnectRequest(SocketAddress from, ReadOnlySpan<byte> packet)
    {
        if (packet.Length != KcpPacket.HandshakeRequestSize || BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Options.ProtocolId)
            return;

        uint nonce = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);

        Span<byte> reply = stackalloc byte[KcpPacket.ChallengeSize];
        reply[0] = (byte)KcpPacketType.Challenge;
        BinaryPrimitives.WriteUInt32LittleEndian(reply[1..], nonce);
        ComputeCookie(from, nonce, NowMs / CookieWindowMs, reply[5..]);
        SendRaw(null, from, reply);
    }

    private void HandleChallengeResponse(SocketAddress from, ReadOnlySpan<byte> packet, long now)
    {
        if (!TryReadChallengeResponse(from, packet, out uint nonce))
            return;

        if (_incomingCount >= Options.MaxConnections)
        {
            Span<byte> reject = stackalloc byte[KcpPacket.RejectSize];
            reject[0] = (byte)KcpPacketType.Reject;
            BinaryPrimitives.WriteUInt32LittleEndian(reject[1..], nonce);
            reject[5] = (byte)DisconnectReason.ServerFull;
            SendRaw(null, from, reject);
            return;
        }

        SocketAddress remote = Clone(from);
        KcpConnection connection = new(this, NextConnectionId(), Format(remote), isOutgoing: false, remote, now)
        {
            ClientNonce = nonce,
            SessionId = RandomUInt32(),
        };
        connection.StartSession(now);

        AddConnection(connection);
        _byAddress.Add(remote, connection);
        _incomingCount++;

        SendAccept(connection);
        RaiseConnected(connection);
    }

    /// <summary>
    /// A handshake packet from the address of an existing incoming connection: either the client never
    /// got our Accept, or it restarted and is connecting again from the same address.
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
                if (!TryReadChallengeResponse(remote, packet, out uint nonce))
                    return;

                if (nonce == connection.ClientNonce)
                {
                    SendAccept(connection);
                    return;
                }

                // A new attempt from the same address, so the old session is gone on the client's end.
                Close(connection, DisconnectReason.RemoteClosed, notifyRemote: false);
                HandleChallengeResponse(remote, packet, now);
                break;
        }
    }

    private bool TryReadChallengeResponse(SocketAddress from, ReadOnlySpan<byte> packet, out uint nonce)
    {
        nonce = 0;
        if (packet.Length != KcpPacket.HandshakeRequestSize || BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Options.ProtocolId)
            return false;

        nonce = BinaryPrimitives.ReadUInt32LittleEndian(packet[5..]);
        ReadOnlySpan<byte> cookie = packet.Slice(9, KcpPacket.CookieSize);

        // Accept cookies from this key window and the one before, so a handshake that straddles a
        // rotation still succeeds.
        long window = NowMs / CookieWindowMs;
        Span<byte> expected = stackalloc byte[KcpPacket.CookieSize];
        ComputeCookie(from, nonce, window, expected);
        if (CryptographicOperations.FixedTimeEquals(cookie, expected))
            return true;

        ComputeCookie(from, nonce, window - 1, expected);
        return CryptographicOperations.FixedTimeEquals(cookie, expected);
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
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], Options.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.ClientNonce);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    internal void SendChallengeResponse(KcpConnection connection, ReadOnlySpan<byte> cookie)
    {
        Span<byte> packet = stackalloc byte[KcpPacket.HandshakeRequestSize];
        packet.Clear();
        packet[0] = (byte)KcpPacketType.ChallengeResponse;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], Options.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], connection.ClientNonce);
        cookie.CopyTo(packet[9..]);
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
    internal void SendSession(KcpConnection connection, KcpPacketType type, ReadOnlySpan<byte> body)
    {
        int length = KcpPacket.SessionHeaderSize + body.Length;
        if (_sendBuffer.Length < length)
            _sendBuffer = new byte[Math.Max(length, Options.Mtu)];

        Span<byte> packet = _sendBuffer.AsSpan(0, length);
        packet[0] = (byte)type;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], connection.SessionId);
        body.CopyTo(packet[KcpPacket.SessionHeaderSize..]);
        SendRaw(connection, connection.RemoteAddress!, packet);
    }

    private void SendRaw(KcpConnection? connection, SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        if (_socket == null)
            return;

        connection?.RecordSent(datagram.Length);

        if (Options.Simulation is { } simulation && Simulate(simulation, to, datagram))
            return;

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
            return;

        if (notifyRemote && connection.State == ConnectionState.Connected)
        {
            // Last chance for queued reliable messages to go out, then say goodbye.
            connection.FlushReliable(NowMs);
            for (int i = 0; i < DisconnectRepeats; i++)
                SendSession(connection, KcpPacketType.Disconnect, ReadOnlySpan<byte>.Empty);
        }

        if (connection.RemoteAddress != null && _byAddress.Remove(connection.RemoteAddress) && !connection.IsOutgoing)
            _incomingCount--;

        RaiseDisconnected(connection, reason);
    }

    internal void OnConnected(KcpConnection connection) => RaiseConnected(connection);

    internal void OnData(KcpConnection connection, ReadOnlySpan<byte> data, Delivery delivery) => RaiseData(connection, data, delivery);

    internal void OnDataPooled(KcpConnection connection, byte[] rentedBuffer, int length, Delivery delivery) =>
        RaiseDataPooled(connection, rentedBuffer, length, delivery);

    // --- Link simulation ------------------------------------------------------------------------

    /// <summary>Drops or delays the datagram. Returns false if it should be sent right away.</summary>
    private bool Simulate(LinkSimulation simulation, SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        Random random = _simulationRandom ??= simulation.Seed is int seed ? new Random(seed) : new Random();

        if (simulation.PacketLoss > 0 && random.NextDouble() < simulation.PacketLoss)
            return true;

        double delayMs = simulation.Latency.TotalMilliseconds + random.NextDouble() * simulation.Jitter.TotalMilliseconds;
        if (delayMs <= 0)
            return false;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(datagram.Length);
        datagram.CopyTo(buffer);
        _delayed.Add(new DelayedPacket(Clone(to), buffer, datagram.Length, NowMs + (long)delayMs));
        return true;
    }

    private void SendDelayed(long now, bool all)
    {
        if (_delayed.Count == 0)
            return;

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

    private readonly record struct DelayedPacket(SocketAddress To, byte[] Buffer, int Length, long DueMs);

    // --- Sockets and addresses ------------------------------------------------------------------

    private void OpenSocket(int port)
    {
        Socket socket = CreateDualModeSocket() ?? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            IPAddress any = socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
            socket.Bind(new IPEndPoint(any, port));
            socket.Blocking = false;
            socket.ReceiveBufferSize = Options.SocketBufferSize;
            socket.SendBufferSize = Options.SocketBufferSize;

            if (OperatingSystem.IsWindows())
                socket.IOControl(SioUdpConnReset, new byte[4], null);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _socket = socket;
        _receiveAddress = new SocketAddress(socket.AddressFamily, SocketAddress.GetMaximumAddressSize(socket.AddressFamily));
        _sendBuffer = new byte[Options.Mtu];
    }

    private static Socket? CreateDualModeSocket()
    {
        if (!Socket.OSSupportsIPv6)
            return null;

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
    internal static SocketAddress? TryResolve(string address, int defaultPort, AddressFamily socketFamily)
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
                    return null;
                host = address[..colon];
            }

            ip = ResolveHost(host, socketFamily);
            if (ip == null)
                return null;
        }

        if (port == 0)
            port = defaultPort;

        if (socketFamily == AddressFamily.InterNetworkV6)
            ip = ip.MapToIPv6();
        else if (ip.AddressFamily != AddressFamily.InterNetwork)
            return null;

        return new IPEndPoint(ip, port).Serialize();
    }

    private static IPAddress? ResolveHost(string host, AddressFamily socketFamily)
    {
        IPAddress[] candidates;
        try
        {
            candidates = Dns.GetHostAddresses(host);
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            return null;
        }

        // Prefer IPv4: it works from both kinds of socket and is what most hosts listen on.
        IPAddress? fallback = null;
        foreach (IPAddress candidate in candidates)
        {
            if (candidate.AddressFamily == AddressFamily.InterNetwork)
                return candidate;
            if (candidate.AddressFamily == AddressFamily.InterNetworkV6 && socketFamily == AddressFamily.InterNetworkV6)
                fallback ??= candidate;
        }
        return fallback;
    }

    private static string Format(SocketAddress address)
    {
        IPEndPoint endpoint = (IPEndPoint)new IPEndPoint(IPAddress.IPv6Any, 0).Create(address);
        if (endpoint.Address.IsIPv4MappedToIPv6)
            endpoint = new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port);
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
        }
        while (value == 0);
        return value;
    }
}
