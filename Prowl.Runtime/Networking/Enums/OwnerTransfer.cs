// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Who may acquire ownership of a network root. The host commits every transfer.</summary>
public enum OwnerTransfer : byte
{
    /// <summary>Only the host may assign ownership. Current owners can still relinquish it.</summary>
    Fixed,

    /// <summary>Any ready client may take ownership through the host.</summary>
    Takeover
}
