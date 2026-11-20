// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>The local player's progress towards gameplay, separate from Courier session admission.</summary>
public enum NetworkJoinState
{
    /// <summary>No session.</summary>
    Disconnected,

    /// <summary>Reaching the host.</summary>
    Connecting,

    /// <summary>Connected, waiting for the host to admit this peer.</summary>
    Joining,

    /// <summary>Admitted, waiting to be told which scenes to load.</summary>
    WaitingForScene,

    /// <summary>Loading the host's scenes from this peer's own assets.</summary>
    Loading,

    /// <summary>Scenes loaded, receiving the host's networked objects.</summary>
    Synchronizing,

    /// <summary>Every object has arrived and its spawn callbacks ran. Gameplay may start.</summary>
    Ready
}
