// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Who writes a <see cref="NetworkBehaviour" />'s state: its [Sync] values, collections and custom state.</summary>
public enum SyncDirection : byte
{
    /// <summary>The host writes it and clients receive it.</summary>
    ServerToClient,

    /// <summary>The owning client writes it, and the host passes it on to the others. Nobody writes it while nobody owns the object.</summary>
    ClientToServer
}
