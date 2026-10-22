// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp;

/// <summary>
///     The kind of a <see cref="TransportEvent" />.
/// </summary>
public enum TransportEventType : byte
{
    /// <summary>A connection finished its handshake. Raised for both incoming and outgoing connections.</summary>
    Connected,

    /// <summary>A connection closed. Raised exactly once per connection, whoever closed it.</summary>
    Disconnected,

    /// <summary>A message arrived.</summary>
    Data
}

/// <summary>
///     Something that happened on a <see cref="Transport" />, returned by <see cref="Transport.Poll" />.
/// </summary>
public readonly struct TransportEvent
{
    public TransportEventType Type { get; }

    /// <summary>The connection this event is about.</summary>
    public Connection Connection { get; }

    /// <summary>
    ///     The message for a <see cref="TransportEventType.Data" /> event. The memory belongs to the transport
    ///     and is only valid until the next call to <see cref="Transport.Poll" />; copy it to keep it.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>How the message for a <see cref="TransportEventType.Data" /> event was sent.</summary>
    public Delivery Delivery { get; }

    /// <summary>Why the connection closed, for a <see cref="TransportEventType.Disconnected" /> event.</summary>
    public DisconnectReason Reason { get; }

    public TransportEvent(TransportEventType type, Connection connection, ReadOnlyMemory<byte> data = default,
        Delivery delivery = default, DisconnectReason reason = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (!Enum.IsDefined(delivery))
        {
            throw new ArgumentOutOfRangeException(nameof(delivery));
        }

        Type = type;
        Connection = connection;
        Data = data;
        Delivery = delivery;
        Reason = reason;
    }

    public override string ToString() => Type switch
    {
        TransportEventType.Data => $"Data({Data.Length} bytes, {Delivery}) from {Connection}",
        TransportEventType.Disconnected => $"Disconnected({Reason}) {Connection}",
        _ => $"{Type} {Connection}"
    };
}
