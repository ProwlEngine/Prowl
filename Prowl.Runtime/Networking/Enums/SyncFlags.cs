// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;


namespace Prowl.Runtime.Networking;

/// <summary>Describes how a <see cref="SyncAttribute" /> property is synchronized.</summary>
[Flags]
public enum SyncFlags : uint
{
    None = 0,

    /// <summary>
    ///     Whoever controls the object changes the value: its owner, or the host while nobody owns it. Without this, as
    ///     with Mirror's default sync direction, only the host changes it, even on an object a client owns.
    /// </summary>
    FromOwner = 1,

    /// <summary>
    ///     Only the host and the object's owner receive the value, as Mirror's owner sync mode, for state such as ammo or
    ///     an inventory other players shouldn't see. Plain values only, not network collections.
    /// </summary>
    ToOwner = 2
}
