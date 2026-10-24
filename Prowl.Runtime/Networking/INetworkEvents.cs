// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Local notifications for a network object's component tree, including disabled components.
///     Initial state and proxy physics are installed before spawn; authority gained follows spawn.
///     Ownership transitions notify authority lost, owner change, then authority gained. Despawn follows authority lost.
/// </summary>
public interface INetworkEvents : ISceneEvent<INetworkEvents>
{
    /// <summary>Initial network state and proxy physics are installed; the scene may not yet be active.</summary>
    void OnNetworkSpawn()
    {
    }

    /// <summary>This component has left the network object's membership.</summary>
    void OnNetworkDespawn()
    {
    }

    /// <summary>This peer now simulates the object, as its owner or as the host of an unowned object.</summary>
    void OnNetworkAuthorityGained()
    {
    }

    /// <summary>This peer no longer has network simulation authority.</summary>
    void OnNetworkAuthorityLost()
    {
    }

    /// <summary>Null means unowned, or a previous owner that has already left the session.</summary>
    void OnNetworkOwnerChanged(NetPeer? previousOwner, NetPeer? owner)
    {
    }
}
