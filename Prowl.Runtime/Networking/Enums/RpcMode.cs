// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Who runs an RPC.</summary>
public enum RpcMode : byte
{
    /// <summary>Everyone who has the object, the host included. Only the host calls it on an object. A static one may come from a client.</summary>
    Broadcast,

    /// <summary>The host only. The one kind a client calls.</summary>
    Host,

    /// <summary>Whoever controls the object: its owner, or the host when it has none. Only the host calls it.</summary>
    Owner,

    /// <summary>One peer the host names. Only the host calls it.</summary>
    Target
}
