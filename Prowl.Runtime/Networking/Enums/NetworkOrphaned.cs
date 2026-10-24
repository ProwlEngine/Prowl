// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking.Enums;

/// <summary>What happens to a networked object when the peer that owns it leaves.</summary>
public enum NetworkOrphaned : byte
{
    /// <summary>Destroy it everywhere.</summary>
    Destroy,

    /// <summary>Keep it, with the host in control.</summary>
    ClearOwner
}
