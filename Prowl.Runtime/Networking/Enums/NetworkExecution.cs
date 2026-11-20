// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Where transport I/O runs. Session callbacks always run on the caller.</summary>
public enum NetworkExecution
{
    /// <summary>What the transport prefers: a thread of its own for sockets, the game thread for an in-process one.</summary>
    Auto,

    /// <summary>On the game thread, pumped once a frame.</summary>
    Inline,

    /// <summary>On a thread of its own, so the connection is kept alive while the game thread is busy.</summary>
    DedicatedThread
}
