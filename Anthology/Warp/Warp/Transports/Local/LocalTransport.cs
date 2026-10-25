// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Warp.Transports.Local;

/// <summary>
///     A transport that connects to other transports in the same process, through a shared
///     <see cref="LocalNetwork" />. Nothing touches a socket: messages are handed across in memory,
///     every delivery mode arrives reliably and in order, and the maximum message size is unbounded.
/// </summary>
/// <remarks>
///     Use it to run a host and its clients in one process, such as in the editor or in tests. Like a real
///     transport, nothing is delivered until the receiving side calls <see cref="Receive" />.
/// </remarks>
public sealed class LocalTransport : Transport
{
    private readonly List<Connection> _closing = new();
    private readonly ConcurrentQueue<Packet> _inbox = new();
    private readonly object _inboxGate = new();
    private bool _accepting = true;
    private long _inboxBytes;
    private bool _listening;
    private long _maxInboxBytes = 64 * 1024 * 1024;
    private long _postedPackets;
    private long _receivedPackets;

    /// <param name="network">The network to listen and connect on.</param>
    /// <param name="address">The address <see cref="Listen" /> registers, which others pass to <see cref="Connect" />.</param>
    public LocalTransport(LocalNetwork network, string address = "local")
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(address);
        Network = network;
        Address = address;
    }

    /// <summary>Maximum pooled payload bytes waiting for Receive; overflow disconnects the sender.</summary>
    public long MaxInboxBytes
    {
        get => _maxInboxBytes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxInboxBytes = value;
        }
    }

    public int MaxInboxPackets { get; init; } = 4096;
    public int MaxPacketsPerReceive { get; init; } = 1024;
    public int MaxConnections { get; init; } = 64;

    /// <summary>Channels per delivery mode. Both ends must use the same count.</summary>
    public int Channels { get; init; } = 4;

    public long PendingInboxBytes
    {
        get
        {
            lock (_inboxGate)
            {
                return _inboxBytes;
            }
        }
    }

    public LocalNetwork Network { get; }

    public string Address { get; }

    public override TransportCapabilities Capabilities =>
        new(true, DeliverySupport.All, TransportExecution.InlinePreferred, Channels);

    public override bool IsListening => _listening;

    /// <exception cref="InvalidOperationException">Another transport already listens at <see cref="Address" />.</exception>
    public override void Listen()
    {
        ThrowIfDisposed();
        if (_listening)
        {
            return;
        }

        ValidateOptions();
        lock (_inboxGate)
        {
            _accepting = true;
        }

        if (!Network.TryRegister(Address, this))
        {
            throw new InvalidOperationException(
                $"Another transport is already listening at local address '{Address}'.");
        }

        _listening = true;
    }

    public override Connection Connect(string address)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(address);
        ValidateOptions();
        if (Connections.Count >= MaxConnections)
        {
            throw new InvalidOperationException("Local connection limit reached.");
        }

        lock (_inboxGate)
        {
            _accepting = true;
        }

        LocalConnection connection = new(this, NextConnectionId(), address, true);
        AddConnection(connection);

        LocalTransport? host = Network.Find(address);
        if (host == null)
        {
            // Reported on the next Receive, the same as a real transport discovering it.
            Post(new Packet(PacketKind.Reject, connection, null, null, 0, default, DisconnectReason.Unreachable));
        }
        else
        {
            if (!host.Post(new Packet(PacketKind.ConnectRequest, null, connection, null, 0, default, default)))
            {
                RaiseDisconnected(connection, DisconnectReason.ResourceLimit);
            }
        }

        return connection;
    }

    public override void Receive()
    {
        ThrowIfDisposed();
        for (int processed = 0; processed < MaxPacketsPerReceive && Take(out Packet packet); processed++)
        {
            switch (packet.Kind)
            {
                case PacketKind.ConnectRequest:
                    OnConnectRequest(packet.Sender!);
                    break;

                case PacketKind.Accept:
                    {
                        LocalConnection connection = packet.Target!;
                        if (connection.State == ConnectionState.Connecting)
                        {
                            connection.Peer = packet.Sender;
                            RaiseConnected(connection);
                        }
                        else
                        {
                            // Gave up before the host answered, so close the host's half too.
                            packet.Sender!.Post(new Packet(PacketKind.Disconnect, packet.Sender, null, null, 0, default,
                                default));
                        }

                        break;
                    }

                case PacketKind.Reject:
                    FinishClose(packet.Target!, packet.Reason);
                    break;

                case PacketKind.Data:
                    if (packet.Target!.State == ConnectionState.Connected)
                    {
                        packet.Target.RecordReceived(packet.Length);
                        RaiseDataPooled(packet.Target, packet.Buffer!, packet.Length, packet.Delivery,
                            packet.Channel);
                    }
                    else
                    {
                        ArrayPool<byte>.Shared.Return(packet.Buffer!);
                    }

                    break;

                case PacketKind.Disconnect:
                    FinishClose(packet.Target!, DisconnectReason.RemoteClosed);
                    break;
            }
        }

        // A full inbox must not lose terminal notifications. Apply their per-connection fallback
        // only after all packets preceding them have been consumed, preserving reliable data order.
        _closing.AddRange(Connections);
        foreach (LocalConnection connection in _closing)
        {
            if (connection.PendingClose is { } pending && _receivedPackets >= pending.AfterPacket)
            {
                FinishClose(connection, pending.Reason);
            }
        }

        _closing.Clear();
    }

    public override void Flush() => ThrowIfDisposed();

    // Messages are handed to the peer as soon as they are sent; there is nothing to flush.
    public override void Shutdown()
    {
        if (_listening)
        {
            Network.Unregister(Address, this);
            _listening = false;
        }

        _closing.AddRange(Connections);
        foreach (Connection connection in _closing)
        {
            Close((LocalConnection)connection, DisconnectReason.Requested);
        }

        _closing.Clear();
        List<Packet> abandoned = new();
        lock (_inboxGate)
        {
            _accepting = false;
            while (_inbox.TryDequeue(out Packet packet))
            {
                abandoned.Add(packet);
            }

            _inboxBytes = 0;
            _receivedPackets = _postedPackets;
        }

        foreach (Packet packet in abandoned)
        {
            if (packet.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(packet.Buffer);
            }

            if (packet.Kind == PacketKind.ConnectRequest)
            {
                packet.Sender!.Post(new Packet(PacketKind.Reject, packet.Sender, null, null, 0, default,
                    DisconnectReason.RemoteClosed));
            }
            else if (packet.Kind == PacketKind.Accept)
            {
                packet.Sender!.Post(new Packet(PacketKind.Disconnect, packet.Sender, null, null, 0, default,
                    DisconnectReason.RemoteClosed));
            }
        }
    }

    protected override void OnReceiveQueueOverflow(Connection connection) =>
        Close((LocalConnection)connection, DisconnectReason.ResourceLimit);

    internal void Close(LocalConnection connection, DisconnectReason reason)
    {
        if (connection.State == ConnectionState.Disconnected)
        {
            return;
        }

        connection.Peer?.Post(new Packet(PacketKind.Disconnect, connection.Peer, null, null, 0, default, default));
        FinishClose(connection, reason);
    }

    private void FinishClose(LocalConnection connection, DisconnectReason reason)
    {
        connection.Peer = null;
        RaiseDisconnected(connection, reason);
    }

    internal bool Post(Packet packet)
    {
        lock (_inboxGate)
        {
            int reserve = packet.Kind is PacketKind.Disconnect or PacketKind.Accept or PacketKind.Reject
                ? MaxConnections * 2
                : 0;
            if (!_accepting || _inbox.Count >= (long)MaxInboxPackets + reserve ||
                (packet.Buffer?.Length ?? 0) > MaxInboxBytes - _inboxBytes)
            {
                if (_accepting && packet.Kind is PacketKind.Reject or PacketKind.Disconnect)
                {
                    // One fixed-size notification per connection, even when its owner runs on
                    // another thread. Only that owner's Receive changes connection state/events.
                    packet.Target!.DeferClose(_postedPackets,
                        packet.Kind == PacketKind.Reject ? packet.Reason : DisconnectReason.RemoteClosed);
                    return true;
                }

                if (packet.Buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(packet.Buffer);
                }

                return false;
            }

            _inboxBytes += packet.Buffer?.Length ?? 0;
            _inbox.Enqueue(packet);
            _postedPackets++;
            return true;
        }
    }

    private bool Take(out Packet packet)
    {
        lock (_inboxGate)
        {
            if (!_inbox.TryDequeue(out packet))
            {
                return false;
            }

            _inboxBytes -= packet.Buffer?.Length ?? 0;
            _receivedPackets++;
            return true;
        }
    }

    private void ValidateOptions()
    {
        if (Channels < 1 || Channels > byte.MaxValue + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Channels));
        }

        if (MaxInboxPackets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInboxPackets));
        }

        if (MaxPacketsPerReceive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPacketsPerReceive));
        }

        if (MaxConnections <= 0 || MaxConnections > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConnections));
        }
    }

    private void OnConnectRequest(LocalConnection client)
    {
        LocalTransport clientTransport = (LocalTransport)client.Transport;

        if (client.State != ConnectionState.Connecting)
        {
            return;
        }

        if (!_listening || Connections.Count >= MaxConnections)
        {
            clientTransport.Post(new Packet(PacketKind.Reject, client, null, null, 0, default,
                !_listening ? DisconnectReason.Unreachable : DisconnectReason.ServerFull));
            return;
        }

        LocalConnection connection = new(this, NextConnectionId(), clientTransport.Address, false) { Peer = client };
        AddConnection(connection);

        if (!clientTransport.Post(new Packet(PacketKind.Accept, client, connection, null, 0, default, default)))
        {
            Close(connection, DisconnectReason.ResourceLimit);
            return;
        }

        RaiseConnected(connection);
    }

    internal enum PacketKind : byte
    {
        ConnectRequest,
        Accept,
        Reject,
        Data,
        Disconnect
    }

    /// <summary>A message between two local transports. <see cref="Target" /> is the receiving connection.</summary>
    internal readonly record struct Packet(
        PacketKind Kind,
        LocalConnection? Target,
        LocalConnection? Sender,
        byte[]? Buffer,
        int Length,
        Delivery Delivery,
        DisconnectReason Reason,
        byte Channel = 0);
}

/// <summary>A <see cref="Connection" /> on a <see cref="LocalTransport" />.</summary>
public sealed class LocalConnection : Connection
{
    private DeferredClose? _pendingClose;

    internal LocalConnection(LocalTransport transport, int id, string address, bool isOutgoing)
        : base(transport, id, address, isOutgoing)
    {
    }

    /// <summary>The other end of this connection, on the remote transport. Set once connected.</summary>
    internal LocalConnection? Peer { get; set; }

    internal DeferredClose? PendingClose => Volatile.Read(ref _pendingClose);

    public override TimeSpan RoundTripTime => TimeSpan.Zero;

    internal void DeferClose(long afterPacket, DisconnectReason reason) =>
        Interlocked.CompareExchange(ref _pendingClose, new DeferredClose(afterPacket, reason), null);

    public override int GetMaxMessageSize(Delivery delivery) => int.MaxValue;

    protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
    {
        // Disconnected from the other side, and we haven't received that news yet.
        LocalConnection? peer = Peer;
        if (peer == null)
        {
            return;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);
        if (!peer.Post(new LocalTransport.Packet(LocalTransport.PacketKind.Data, peer, this, buffer, data.Length,
                delivery, default, channel)))
        {
            ((LocalTransport)Transport).Close(this, DisconnectReason.ResourceLimit);
            return;
        }

        RecordPacketSent(data.Length);
    }

    public override void Disconnect() => ((LocalTransport)Transport).Close(this, DisconnectReason.Requested);

    internal bool Post(LocalTransport.Packet packet) => ((LocalTransport)Transport).Post(packet);

    internal void RecordReceived(int bytes) => RecordPacketReceived(bytes);

    internal sealed record DeferredClose(long AfterPacket, DisconnectReason Reason);
}
