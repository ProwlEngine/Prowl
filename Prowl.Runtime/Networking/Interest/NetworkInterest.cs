// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Host: which networked objects each client is near enough to receive, under <see cref="InterestMode.Distance" />.
///     An object's visibility rules come first, and distance decides only for <see cref="NetworkVisibility.Default" />
///     ones a client doesn't own or have shown to it. A client with no viewpoint receives none of those.
/// </summary>
public sealed class NetworkInterest
{
    private readonly NetworkSystem _system;

    internal NetworkInterest(NetworkSystem system) => _system = system;

    /// <summary>The client's viewpoints and options. One that left gets a detached copy that changes nothing.</summary>
    public PeerInterest For(NetPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return _system.InterestOf(peer);
    }

    /// <summary>Checks every client against every object now, rather than at their next interest check.</summary>
    public void Refresh()
    {
        foreach (NetPeer peer in _system.RemotePeers())
        {
            _system.RefreshInterest(_system.InterestOf(peer));
        }
    }

    /// <summary>Checks <paramref name="peer" /> against every object now.</summary>
    public void Refresh(NetPeer peer) => _system.RefreshInterest(For(peer));

    /// <summary>How many networked objects <paramref name="peer" /> has.</summary>
    public int VisibleCount(NetPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return _system.VisibleCount(peer);
    }

    /// <summary>Adds the networked objects <paramref name="peer" /> has to <paramref name="results" />.</summary>
    public void GetVisible(NetPeer peer, List<GameObject> results)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(results);
        _system.GetVisible(peer, results);
    }
}
