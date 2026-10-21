// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp;

/// <summary>
/// How a message travels to the remote end of a <see cref="Connection"/>.
/// </summary>
public enum Delivery : byte
{
    /// <summary>
    /// Guaranteed and in order. Large messages are split across packets and reassembled before
    /// they are delivered, so the receiver always sees whole messages.
    /// </summary>
    Reliable = 0,

    /// <summary>
    /// Fire and forget. The message may be lost, duplicated or arrive out of order, and it must fit
    /// in a single packet (see <see cref="Connection.GetMaxMessageSize"/>). Cheapest and lowest latency.
    /// </summary>
    Unreliable = 1,

    /// <summary>Reliable, ordered bulk transfers on an independent channel. No ordering with Reliable.</summary>
    Bulk = 2,
}
