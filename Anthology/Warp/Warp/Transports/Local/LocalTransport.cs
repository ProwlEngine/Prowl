// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Warp.Transports.Local;

/// <summary>
/// A transport that connects to other transports in the same process, through a shared
/// <see cref="LocalNetwork"/>. Nothing touches a socket: messages are handed across in memory,
/// both deliveries arrive reliably and in order, and the maximum message size is unbounded.
/// </summary>
/// <remarks>
/// Use it to run a host and its clients in one process, such as in the editor or in tests. Like a real
/// transport, nothing is delivered until the receiving side calls <see cref="Receive"/>.
/// </remarks>
public sealed class LocalTransport : Transport
{
    private readonly ConcurrentQueue<Packet> _inbox = new();
    private readonly List<Connection> _closing = new();
    private bool _listening;

    /// <param name="network">The network to listen and connect on.</param>
    /// <param name="address">The address <see cref="Listen"/> registers, which others pass to <see cref="Connect"/>.</param>
    public LocalTransport(LocalNetwork network, string address = "local")
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(address);
        Network = network;
        Address = address;
    }

    public LocalNetwork Network { get; }

    public string Address { get; }

    public override bool IsListening => _listening;

    /// <exception cref="InvalidOperationException">Another transport already listens at <see cref="Address"/>.</exception>
    public override void Listen()
    {
        if (_listening)
            return;

        if (!Network.TryRegister(Address, this))
            throw new InvalidOperationException($"Another transport is already listening at local address '{Address}'.");

        _listening = true;
    }

    public override Connection Connect(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        LocalConnection connection = new(this, NextConnectionId(), address, isOutgoing: true);
        AddConnection(connection);

        LocalTransport? host = Network.Find(address);
        if (host == null)
        {
            // Reported on the next Receive, the same as a real transport discovering it.
            _inbox.Enqueue(new Packet(PacketKind.Reject, connection, null, null, 0, default, DisconnectReason.Unreachable));
        }
        else
        {
            host._inbox.Enqueue(new Packet(PacketKind.ConnectRequest, null, connection, null, 0, default, default));
        }

        return connection;
    }

    public override void Receive()
    {
        while (_inbox.TryDequeue(out Packet packet))
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
                        packet.Sender!.Post(new Packet(PacketKind.Disconnect, packet.Sender, null, null, 0, default, default));
                    }
                    break;
                }

                case PacketKind.Reject:
                    RaiseDisconnected(packet.Target!, packet.Reason);
                    break;

                case PacketKind.Data:
                    if (packet.Target!.State == ConnectionState.Connected)
                    {
                        packet.Target.RecordReceived(packet.Length);
                        RaiseDataPooled(packet.Target, packet.Buffer!, packet.Length, packet.Delivery);
                    }
                    else
                        ArrayPool<byte>.Shared.Return(packet.Buffer!);
                    break;

                case PacketKind.Disconnect:
                    RaiseDisconnected(packet.Target!, DisconnectReason.RemoteClosed);
                    break;
            }
        }
    }

    public override void Flush()
    {
        // Messages are handed to the peer as soon as they are sent; there is nothing to flush.
    }

    public override void Shutdown()
    {
        if (_listening)
        {
            Network.Unregister(Address, this);
            _listening = false;
        }

        _closing.AddRange(Connections);
        foreach (Connection connection in _closing)
            Close((LocalConnection)connection, DisconnectReason.Requested);
        _closing.Clear();
    }

    protected override void OnReceiveQueueOverflow(Connection connection) =>
        Close((LocalConnection)connection, DisconnectReason.ResourceLimit);

    internal void Close(LocalConnection connection, DisconnectReason reason)
    {
        if (connection.State == ConnectionState.Disconnected)
            return;

        connection.Peer?.Post(new Packet(PacketKind.Disconnect, connection.Peer, null, null, 0, default, default));
        connection.Peer = null;
        RaiseDisconnected(connection, reason);
    }

    internal void Post(Packet packet) => _inbox.Enqueue(packet);

    private void OnConnectRequest(LocalConnection client)
    {
        LocalTransport clientTransport = (LocalTransport)client.Transport;

        if (!_listening)
        {
            clientTransport.Post(new Packet(PacketKind.Reject, client, null, null, 0, default, DisconnectReason.Unreachable));
            return;
        }

        LocalConnection connection = new(this, NextConnectionId(), clientTransport.Address, isOutgoing: false)
        {
            Peer = client,
        };
        AddConnection(connection);
        RaiseConnected(connection);

        clientTransport.Post(new Packet(PacketKind.Accept, client, connection, null, 0, default, default));
    }

    internal enum PacketKind : byte
    {
        ConnectRequest,
        Accept,
        Reject,
        Data,
        Disconnect,
    }

    /// <summary>A message between two local transports. <see cref="Target"/> is the receiving connection.</summary>
    internal readonly record struct Packet(
        PacketKind Kind,
        LocalConnection? Target,
        LocalConnection? Sender,
        byte[]? Buffer,
        int Length,
        Delivery Delivery,
        DisconnectReason Reason);
}

/// <summary>A <see cref="Connection"/> on a <see cref="LocalTransport"/>.</summary>
public sealed class LocalConnection : Connection
{
    internal LocalConnection(LocalTransport transport, int id, string address, bool isOutgoing)
        : base(transport, id, address, isOutgoing)
    {
    }

    /// <summary>The other end of this connection, on the remote transport. Set once connected.</summary>
    internal LocalConnection? Peer { get; set; }

    public override TimeSpan RoundTripTime => TimeSpan.Zero;

    public override int GetMaxMessageSize(Delivery delivery) => int.MaxValue;

    public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
    {
        ValidateSend(data, delivery);

        // Disconnected from the other side, and we haven't received that news yet.
        LocalConnection? peer = Peer;
        if (peer == null)
            return;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);
        peer.Post(new LocalTransport.Packet(LocalTransport.PacketKind.Data, peer, this, buffer, data.Length, delivery, default));

        RecordPacketSent(data.Length);
    }

    public override void Disconnect() => ((LocalTransport)Transport).Close(this, DisconnectReason.Requested);

    internal void Post(LocalTransport.Packet packet) => ((LocalTransport)Transport).Post(packet);

    internal void RecordReceived(int bytes) => RecordPacketReceived(bytes);
}
