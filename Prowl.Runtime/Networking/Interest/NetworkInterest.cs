// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Host: decides which clients receive each networked object. Set one as <see cref="Networking.Interest" />. With
///     none, every client receives every object. An object's owner always receives it, and its
///     <see cref="NetworkVisibility" /> can force it shown to everyone or hidden from all but its owner.
/// </summary>
public abstract class NetworkInterest
{
    /// <summary>Whether <paramref name="peer" /> receives <paramref name="go" />. Asked for every object as a client joins or loads a scene.</summary>
    public abstract bool OnCheckObserver(GameObject go, NetPeer peer);

    /// <summary>Adds the clients that receive <paramref name="go" /> to <paramref name="observers" />. Asked as it spawns and on each rebuild.</summary>
    public abstract void OnRebuildObservers(GameObject go, HashSet<NetPeer> observers);

    /// <summary>A networked object was spawned, before its observers are first worked out.</summary>
    public virtual void OnSpawned(GameObject go)
    {
    }

    /// <summary>A networked object was despawned or destroyed.</summary>
    public virtual void OnDestroyed(GameObject go)
    {
    }

    /// <summary>A session starts or ends, or this became the interest: forget everything kept so far.</summary>
    public virtual void ResetState()
    {
    }

    /// <summary>Runs once a network tick on the host, for rebuilding observers over time.</summary>
    public virtual void Update()
    {
    }

    /// <summary>Works out again who receives <paramref name="go" />, sending or removing it where that changed.</summary>
    protected static void Rebuild(GameObject go) => Networking.System?.RebuildObservers(go);

    /// <summary>Works out again who receives every networked object.</summary>
    protected static void RebuildAll() => Networking.System?.RebuildAllObservers();

    /// <summary>The clients of the session, not the host itself. Kept until a peer joins or leaves, so it costs nothing to ask again.</summary>
    protected static IReadOnlyList<NetPeer> Clients() => Networking.System?.RemotePeers() ?? [];
}
