// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Who receives a <see cref="NetworkBehaviour" />'s state.</summary>
public enum SyncMode : byte
{
    /// <summary>Every client that has the object.</summary>
    Observers,

    /// <summary>Only the object's owner, for state such as ammo or an inventory other players shouldn't see.</summary>
    Owner
}
