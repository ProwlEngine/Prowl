// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking.Enums;

/// <summary>The local player's progress towards gameplay, separate from Warp session admission.</summary>
public enum NetworkJoinState
{
    Disconnected,
    Connecting,
    Joining,
    WaitingForScene,
    Loading,
    Synchronizing,
    Ready
}
