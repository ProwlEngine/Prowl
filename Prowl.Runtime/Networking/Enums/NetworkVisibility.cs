// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Which clients a networked object is sent to. Its owner always has it, and the host can override it per peer.</summary>
public enum NetworkVisibility : byte
{
    /// <summary>Every client has it.</summary>
    Default,

    /// <summary>Every client has it, whatever other rules say.</summary>
    Always,

    /// <summary>Only its owner has it.</summary>
    OwnerOnly,

    /// <summary>Only its owner and the peers the host shows it to have it.</summary>
    Manual
}
