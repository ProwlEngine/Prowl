// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp;

/// <summary>
/// Traffic counters for a <see cref="Connection"/>, counted at the packet level (including protocol
/// overhead, retransmissions and keepalives).
/// </summary>
public readonly record struct ConnectionStats(long PacketsSent, long PacketsReceived, long BytesSent, long BytesReceived);

/// <summary>
/// One end of a link between this <see cref="Transport"/> and a remote peer. Connections are created
/// by the transport, either by <see cref="Transport.Connect"/> or when a remote peer connects to a
/// listening transport, and are never reused once <see cref="ConnectionState.Disconnected"/>.
/// </summary>
public abstract class Connection
{
    private long _packetsSent;
    private long _packetsReceived;
    private long _bytesSent;
    private long _bytesReceived;

    protected Connection(Transport transport, int id, string address, bool isOutgoing)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(address);
        Transport = transport;
        Id = id;
        Address = address;
        IsOutgoing = isOutgoing;
    }

    /// <summary>The transport that owns this connection.</summary>
    public Transport Transport { get; }

    /// <summary>Identifies this connection within its transport. Never reused by that transport.</summary>
    public int Id { get; }

    /// <summary>The remote address, in the transport's own format (for UDP, <c>host:port</c>).</summary>
    public string Address { get; }

    /// <summary>True if this side initiated the connection with <see cref="Transport.Connect"/>.</summary>
    public bool IsOutgoing { get; }

    public ConnectionState State { get; internal set; } = ConnectionState.Connecting;

    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>Smoothed round trip time to the remote end. Zero until it has been measured.</summary>
    public abstract TimeSpan RoundTripTime { get; }

    public ConnectionStats Stats => new(_packetsSent, _packetsReceived, _bytesSent, _bytesReceived);

    /// <summary>Free slot for whoever is using the transport, such as a higher level session or player object.</summary>
    public object? UserData { get; set; }

    /// <summary>The largest message, in bytes, that can be sent with the given <paramref name="delivery"/>.</summary>
    public abstract int GetMaxMessageSize(Delivery delivery);

    /// <summary>
    /// Sends a message to the remote end. Data is copied, so the span can be reused immediately.
    /// Reliable messages are queued and go out on the transport's next <see cref="Transport.Flush"/>;
    /// depending on the transport, unreliable ones may go out straight away.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection is not <see cref="ConnectionState.Connected"/>.</exception>
    /// <exception cref="ArgumentException">The message is larger than <see cref="GetMaxMessageSize"/>.</exception>
    public abstract void Send(ReadOnlySpan<byte> data, Delivery delivery);

    /// <summary>
    /// Closes the connection and tells the remote end. A <see cref="TransportEventType.Disconnected"/>
    /// event with <see cref="DisconnectReason.Requested"/> follows. Does nothing if already disconnected.
    /// </summary>
    public abstract void Disconnect();

    /// <summary>Throws unless the connection can send a message of this size right now.</summary>
    protected void ValidateSend(ReadOnlySpan<byte> data, Delivery delivery)
    {
        if (State != ConnectionState.Connected)
            throw new InvalidOperationException($"Cannot send on {this}: it is {State}.");

        if (!Enum.IsDefined(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
        int max = GetMaxMessageSize(delivery);
        if (data.Length > max)
            throw new ArgumentException($"A {delivery} message can be at most {max} bytes, but this one is {data.Length}.", nameof(data));
    }

    protected void RecordPacketSent(int bytes)
    {
        _packetsSent++;
        _bytesSent += bytes;
    }

    protected void RecordPacketReceived(int bytes)
    {
        _packetsReceived++;
        _bytesReceived += bytes;
    }

    public override string ToString() => $"#{Id} {Address} ({State})";
}
