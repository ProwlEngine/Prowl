// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>Options for an RPC.</summary>
[Flags]
public enum RpcFlags : uint
{
    None = 0,

    /// <summary>Send it unreliably: cheaper and lower latency, but it may be lost. Arguments must fit in one packet.</summary>
    Unreliable = 1,

    /// <summary>
    ///     Any client that has the object may call a host RPC. Without this, a client may only
    ///     call it on an object it controls, and calls from other clients are dropped. The host may always call.
    /// </summary>
    AnyCaller = 4,

    /// <summary>A broadcast skips the object's owner.</summary>
    ExcludeOwner = 8
}
