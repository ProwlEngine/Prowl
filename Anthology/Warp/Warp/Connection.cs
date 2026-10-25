// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp;

/// <summary>
///     Traffic counters for a <see cref="Connection" />, counted at the packet level (including protocol
///     overhead, retransmissions and keepalives).
/// </summary>
public readonly record struct ConnectionStats(
    long PacketsSent,
    long PacketsReceived,
    long BytesSent,
    long BytesReceived);

/// <summary>
///     One end of a link between this <see cref="Transport" /> and a remote peer. Connections are created
///     by the transport, either by <see cref="Transport.Connect" /> or when a remote peer connects to a
///     listening transport, and are never reused once <see cref="ConnectionState.Disconnected" />.
/// </summary>
public abstract class Connection
{
    private long _bytesReceived;
    private long _bytesSent;
    private long _packetsReceived;
    private long _packetsSent;

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

    /// <summary>True if this side initiated the connection with <see cref="Transport.Connect" />.</summary>
    public bool IsOutgoing { get; }

    public ConnectionState State { get; protected internal set; } = ConnectionState.Connecting;

    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>Smoothed round trip time to the remote end. Zero until it has been measured.</summary>
    public abstract TimeSpan RoundTripTime { get; }

    public virtual ConnectionStats Stats => new(_packetsSent, _packetsReceived, _bytesSent, _bytesReceived);

    /// <summary>Free slot for whoever is using the transport, such as a higher level session or player object.</summary>
    public object? UserData { get; set; }

    /// <summary>The largest message, in bytes, that can be sent with the given <paramref name="delivery" />.</summary>
    public abstract int GetMaxMessageSize(Delivery delivery);

    /// <summary>Preferred wire batch size, independent of the largest supported message.</summary>
    public virtual int GetBatchThreshold(Delivery delivery) => Math.Min(1200, GetMaxMessageSize(delivery));

    /// <summary>
    ///     Sends a message to the remote end. Data is copied, so the span can be reused immediately.
    ///     Reliable messages are queued and go out on the transport's next <see cref="Transport.Flush" />;
    ///     depending on the transport, unreliable ones may go out straight away.
    /// </summary>
    /// <param name="data">The message.</param>
    /// <param name="delivery">How the message travels.</param>
    /// <param name="flags">Timing policy. Immediate preserves ordering with earlier queued messages.</param>
    /// <param name="channel">
    ///     Which of the transport's <see cref="TransportCapabilities.Channels" /> a reliable message goes on; messages
    ///     are ordered within a channel only. Unreliable messages have no order to keep, so they always use channel 0.
    /// </param>
    /// <exception cref="InvalidOperationException">The connection is not <see cref="ConnectionState.Connected" />.</exception>
    /// <exception cref="ArgumentException">The message is larger than <see cref="GetMaxMessageSize" />.</exception>
    public void Send(ReadOnlySpan<byte> data, Delivery delivery, SendFlags flags = SendFlags.None, byte channel = 0)
    {
        if ((flags & ~SendFlags.Immediate) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }

        ValidateSend(data, delivery, channel);
        SendCore(data, delivery, channel);
        if ((flags & SendFlags.Immediate) != 0)
        {
            Flush();
        }
    }

    /// <summary>Queues or sends a message that <see cref="Send" /> has already validated.</summary>
    protected abstract void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel);

    /// <summary>Submits queued sends. An externally driven connection may forward this as a queued request.</summary>
    public virtual void Flush() => Transport.Flush();

    /// <summary>
    ///     Closes the connection and tells the remote end. A <see cref="TransportEventType.Disconnected" />
    ///     event with <see cref="DisconnectReason.Requested" /> follows. Does nothing if already disconnected.
    /// </summary>
    public abstract void Disconnect();

    /// <summary>Throws unless the connection can send a message of this size right now.</summary>
    private void ValidateSend(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
    {
        if (State != ConnectionState.Connected)
        {
            throw new InvalidOperationException($"Cannot send on {this}: it is {State}.");
        }

        if (!Enum.IsDefined(delivery))
        {
            throw new ArgumentOutOfRangeException(nameof(delivery));
        }

        if (!Transport.Capabilities.Supports(delivery))
        {
            throw new NotSupportedException($"Transport does not support {delivery} delivery.");
        }

        if (channel >= Transport.Capabilities.Channels || (channel != 0 && delivery != Delivery.Reliable))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                $"Transport has {Transport.Capabilities.Channels} channels, and only reliable messages use any but 0.");
        }

        int max = GetMaxMessageSize(delivery);
        if (data.Length > max)
        {
            throw new ArgumentException(
                $"A {delivery} message can be at most {max} bytes, but this one is {data.Length}.", nameof(data));
        }
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
