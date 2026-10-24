// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Networking.Objects;

namespace Prowl.Runtime.Networking.Enums;

/// <summary>How a <see cref="GameObject" /> takes part in a networked session. See <see cref="Networking" />.</summary>
public enum NetworkMode : byte
{
    /// <summary>
    ///     Sent to clients as part of the scene when they join, and when the host changes scene, but not
    ///     kept in sync afterwards. Right for level geometry, props and anything else that does not change,
    ///     and the default, so a scene needs no setup to be shared.
    /// </summary>
    Snapshot,

    /// <summary>
    ///     Never leaves the process it exists in. Clients don't receive it at all, and anything below it is
    ///     excluded too. For host-only logic, local effects and editor helpers.
    /// </summary>
    Never,

    /// <summary>
    ///     A networked object, kept in sync for its whole life: its transform and [Sync] properties follow
    ///     whoever controls it (its owner, or the host), and destroying it on the host destroys it everywhere.
    ///     Objects placed in a scene this way belong to the host; <see cref="NetworkAccessor.Spawn" />
    ///     creates new ones at runtime. Everything below it is part of it.
    /// </summary>
    Object
}
