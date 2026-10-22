// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Scene-wide session notifications, including disabled components. Exceptions are logged.</summary>
public interface INetworkListener : ISceneEvent<INetworkListener>
{
    /// <summary>The local scene has its initial network state and is about to load. Gameplay is not ready.</summary>
    void OnNetworkLoading() { }

    /// <summary>The local scene is active and its readiness handshake has completed. Called again after scene changes.</summary>
    void OnNetworkReady() { }

    /// <summary>Host only: a remote peer passed session admission, before its scene snapshot is sent.</summary>
    void OnPeerConnected(NetPeer peer) { }

    /// <summary>Host only: a remote peer activated this scene and acknowledged catch-up. Safe to spawn its player.</summary>
    void OnPeerReady(NetPeer peer) { }

    /// <summary>Host only: an admitted remote peer left, whether or not it ever became ready.</summary>
    void OnPeerDisconnected(NetPeer peer, SessionEndReason reason) { }
}
