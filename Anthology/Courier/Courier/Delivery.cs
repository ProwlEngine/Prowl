// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Courier;

/// <summary>
///     How a message travels to the remote end of a <see cref="Connection" />.
/// </summary>
public enum Delivery : byte
{
    /// <summary>
    ///     Guaranteed and in order. Large messages are split across packets and reassembled before
    ///     they are delivered, so the receiver always sees whole messages.
    /// </summary>
    Reliable = 0,

    /// <summary>
    ///     Fire and forget. The message may be lost, duplicated or arrive out of order. Messages larger than
    ///     one packet are split and are lost whole if any piece is lost, so keep them small where you can
    ///     (see <see cref="Connection.GetMaxMessageSize" />). Cheapest and lowest latency.
    /// </summary>
    Unreliable = 1,

    /// <summary>
    ///     Like <see cref="Unreliable" />, but a message older than one already delivered on its channel is
    ///     dropped, so the receiver only ever moves forward. Suits state that the next message replaces,
    ///     such as positions. Each channel keeps its own sequence.
    /// </summary>
    UnreliableSequenced = 2
}
