// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp;

/// <summary>
/// The lifecycle of a <see cref="Connection"/>. A connection only ever moves forward through these.
/// </summary>
public enum ConnectionState : byte
{
    /// <summary>The handshake is still in progress. Messages cannot be sent yet.</summary>
    Connecting,

    /// <summary>The handshake completed and messages can be sent in both directions.</summary>
    Connected,

    /// <summary>The connection is closed and will never be used again.</summary>
    Disconnected,
}
