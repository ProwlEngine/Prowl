// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp;

/// <summary>
/// Why a <see cref="Connection"/> was closed. Reported once, on its
/// <see cref="TransportEventType.Disconnected"/> event.
/// </summary>
public enum DisconnectReason : byte
{
    /// <summary>This side closed the connection, through <see cref="Connection.Disconnect"/> or <see cref="Transport.Shutdown"/>.</summary>
    Requested,

    /// <summary>The remote end closed the connection.</summary>
    RemoteClosed,

    /// <summary>
    /// Nothing was heard from the remote end for too long, or the handshake never completed. A
    /// connection that times out while still <see cref="ConnectionState.Connecting"/> never reached the host.
    /// </summary>
    Timeout,

    /// <summary>The address could not be resolved, or nothing is listening at it.</summary>
    Unreachable,

    /// <summary>The host refused the connection.</summary>
    Rejected,

    /// <summary>The host refused the connection because it has no free slots.</summary>
    ServerFull,

    /// <summary>The peer exceeded a local traffic or queue budget.</summary>
    ResourceLimit,

    /// <summary>The secure transport could not authenticate its peer.</summary>
    AuthenticationFailed,
}
