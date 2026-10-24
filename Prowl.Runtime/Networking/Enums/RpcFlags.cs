// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking.Enums;

/// <summary>Options for an RPC.</summary>
[Flags]
public enum RpcFlags : uint
{
    None = 0,

    /// <summary>Send it unreliably: cheaper and lower latency, but it may be lost. Arguments must fit in one packet.</summary>
    Unreliable = 1,

    /// <summary>Only the host may call it. Calls from clients are dropped.</summary>
    HostOnly = 2,

    /// <summary>Only whoever controls the object may call it. Calls from anyone else are dropped.</summary>
    OwnerOnly = 4
}
