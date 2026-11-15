// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>
///     A component in the networked scene that runs once per network tick, on every peer, right after the fixed step
///     that completes the tick and before the tick's state is sent. In execution order, while enabled.
/// </summary>
public interface INetworkTick
{
    /// <summary>The tick that just completed, as <see cref="Networking.Tick" /> reads now.</summary>
    void OnNetworkTick(uint tick);
}
