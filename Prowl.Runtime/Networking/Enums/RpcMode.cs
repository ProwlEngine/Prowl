// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking.Enums;

/// <summary>Who runs an RPC.</summary>
public enum RpcMode : byte
{
    /// <summary>Everyone, the caller included.</summary>
    Broadcast,

    /// <summary>The host only.</summary>
    Host,

    /// <summary>Whoever controls the object: its owner, or the host when it has none.</summary>
    Owner
}
