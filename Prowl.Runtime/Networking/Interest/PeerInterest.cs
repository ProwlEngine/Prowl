// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Host: where one client sees from under <see cref="InterestMode.Distance" />. Get it from <see cref="NetworkInterest.For" />.</summary>
public sealed class PeerInterest
{
    private readonly NetworkSystem? _system;
    private bool _seesEverything;

    internal readonly List<Float3> Explicit = new();
    internal bool HasExplicit;

    // The viewpoints last gathered, with their radius, and the tick they were gathered on.
    internal readonly List<(Float3 Position, float Radius)> Viewpoints = new();
    internal uint GatheredTick;
    internal bool Gathered;

    // When each object the client has was last in range, for the linger time.
    internal readonly Dictionary<NetworkObject, double> InRangeAt = new(ReferenceEqualityComparer.Instance);

    internal PeerInterest(NetPeer peer, NetworkSystem? system)
    {
        Peer = peer;
        _system = system;
    }

    public NetPeer Peer { get; }

    /// <summary>
    ///     Receives every object whatever the distance, as a spectator or admin would. Objects that are
    ///     <see cref="NetworkVisibility.Manual" /> or <see cref="NetworkVisibility.OwnerOnly" /> stay hidden. Applies at once.
    /// </summary>
    public bool SeesEverything
    {
        get => _seesEverything;
        set
        {
            if (value == _seesEverything)
            {
                return;
            }

            _seesEverything = value;
            _system?.RefreshInterest(this);
        }
    }

    /// <summary>
    ///     Sees from these points, with the project's interest radius, in place of the NetworkObservers the client owns.
    ///     Applies at the client's next interest check, or at once from OnPeerConnected. Cleared when the host loads
    ///     another scene.
    /// </summary>
    public void SetViewpoints(ReadOnlySpan<Float3> viewpoints)
    {
        Explicit.Clear();
        foreach (Float3 viewpoint in viewpoints)
        {
            Explicit.Add(viewpoint);
        }

        HasExplicit = true;
        Gathered = false;
    }

    /// <summary>Sees from the NetworkObservers the client owns again.</summary>
    public void ClearViewpoints()
    {
        Explicit.Clear();
        HasExplicit = false;
        Gathered = false;
    }
}
