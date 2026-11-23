// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A client receives the objects within <see cref="Range" /> of its player object, set with
///     <see cref="Networking.AddPlayer" />. A client with no player receives only what it owns. An object with a
///     <see cref="DistanceInterestRange" /> uses that range instead.
/// </summary>
public sealed class DistanceInterest : NetworkInterest
{
    /// <summary>How far from its player a client still receives an object.</summary>
    public float Range = 500;

    /// <summary>Seconds between rebuilds of every object's observers.</summary>
    public float RebuildInterval = 1;

    private double _lastRebuild = double.NegativeInfinity;

    public override bool OnCheckObserver(GameObject go, NetPeer peer) =>
        Networking.PlayerOf(peer) is { } player && player.IsValid() && InRange(go, player);

    public override void OnRebuildObservers(GameObject go, HashSet<NetPeer> observers)
    {
        foreach (NetPeer peer in Clients())
        {
            if (OnCheckObserver(go, peer))
            {
                observers.Add(peer);
            }
        }
    }

    public override void Update()
    {
        double now = Networking.ServerTime;
        if (now >= _lastRebuild + RebuildInterval)
        {
            _lastRebuild = now;
            RebuildAll();
        }
    }

    public override void ResetState() => _lastRebuild = double.NegativeInfinity;

    private bool InRange(GameObject go, GameObject player)
    {
        float range = go.GetComponent<DistanceInterestRange>() is { } custom ? custom.Range : Range;
        return Float3.LengthSquared(go.Transform.Position - player.Transform.Position) < range * range;
    }
}

/// <summary>The range <see cref="DistanceInterest" /> sends this networked object within, in place of its own.</summary>
[AddComponentMenu("Networking/Distance Interest Range")]
public sealed class DistanceInterestRange : Component
{
    /// <summary>How far from its player a client still receives this object.</summary>
    public float Range = 100;
}
