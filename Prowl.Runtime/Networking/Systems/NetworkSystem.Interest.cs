// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// Distance interest: a client has the objects its rules allow while they are in range of one of its viewpoints.
internal sealed partial class NetworkSystem
{
    private readonly Dictionary<int, PeerInterest> _interests = new();
    private readonly List<NetPeer> _interestPeers = new();
    private readonly List<(NetworkObject Entry, bool Visible)> _interestChanges = new();
    private readonly HashSet<int> _warnedNoViewpoint = new();
    private readonly InterestGrid _grid = new();
    private readonly HashSet<NetworkObject> _nearby = new(ReferenceEqualityComparer.Instance);
    private NetworkInterest? _interest;
    private InterestMode _interestMode;
    private bool _revealedWithoutInterest;

    // Counts visibility listener calls, whose game code may move objects after the grid was built.
    internal int VisibilityCallbacks;
    private uint _interestTick;

    internal NetworkInterest Interest => _interest ??= new NetworkInterest(this);

    /// <summary>The interest of <paramref name="peer" />, made on first use. A peer not in the session gets a detached one.</summary>
    internal PeerInterest InterestOf(NetPeer peer)
    {
        if (_interests.TryGetValue(peer.Id, out PeerInterest? interest) && ReferenceEquals(interest.Peer, peer))
        {
            return interest;
        }

        if (peer.IsLocal || Session is not { } session || !session.TryGetPeer(peer.Id, out NetPeer? current) ||
            !ReferenceEquals(current, peer))
        {
            return new PeerInterest(peer, null);
        }

        interest = new PeerInterest(peer, this);
        _interests[peer.Id] = interest;
        return interest;
    }

    internal List<NetPeer> RemotePeers()
    {
        List<NetPeer> peers = new();
        foreach (NetPeer peer in Session?.Peers ?? [])
        {
            if (!peer.IsLocal)
            {
                peers.Add(peer);
            }
        }

        return peers;
    }

    /// <summary>Checks this tick's share of the clients, each every InterestUpdateTicks ticks.</summary>
    internal void UpdateInterest()
    {
        if (!IsHost || Session is not { } session)
        {
            return;
        }

        // A changed mode applies to every client at once, so turning it off sends what distance held back.
        if (Configuration.InterestMode != _interestMode ||
            (_revealedWithoutInterest && Configuration.InterestMode != InterestMode.None))
        {
            _interestMode = Configuration.InterestMode;
            _revealedWithoutInterest = false;
            foreach (NetPeer peer in RemotePeers())
            {
                if (Session != session)
                {
                    return;
                }

                CheckInterest(peer);
            }

            return;
        }

        if (_interestMode == InterestMode.None)
        {
            return;
        }

        uint every = (uint)Configuration.InterestUpdateTicks;
        _interestTick++;
        _interestPeers.Clear();
        foreach (NetPeer peer in session.Peers)
        {
            if (!peer.IsLocal && ((uint)peer.Id + _interestTick) % every == 0)
            {
                _interestPeers.Add(peer);
            }
        }

        foreach (NetPeer peer in _interestPeers)
        {
            if (Session != session)
            {
                return;
            }

            CheckInterest(peer, true);
        }
    }

    internal void RefreshInterest(PeerInterest interest)
    {
        if (IsActive && IsHost && ReferenceEquals(InterestOf(interest.Peer), interest))
        {
            CheckInterest(interest.Peer);
        }
    }

    private void CheckInterest(NetPeer peer, bool onlyNearby = false)
    {
        if (!HasCurrentWorld(peer))
        {
            return;
        }

        PeerInterest? interest = null;
        if (Configuration.InterestMode != InterestMode.None)
        {
            interest = Gather(InterestOf(peer));
            if (IsPeerReady(peer))
            {
                WarnWithoutViewpoint(interest);
            }
        }

        _interestChanges.Clear();
        if (onlyNearby && interest != null)
        {
            foreach (NetworkObject entry in Nearby(interest))
            {
                CheckInterest(entry, peer, interest);
            }
        }
        else
        {
            foreach (NetworkObject entry in Entries.Values)
            {
                CheckInterest(entry, peer, interest);
            }
        }

        if (_interestChanges.Count == 0)
        {
            return;
        }

        // Listeners run game code, which may check interest again.
        NetSession session = Session!;
        foreach ((NetworkObject entry, bool visible) in _interestChanges.ToArray())
        {
            if (Session != session || !HasCurrentWorld(peer))
            {
                return;
            }

            if (FindEntry(entry.GameObject) == entry && !entry.TearingDown && Sees(entry, peer) != visible)
            {
                SetHidden(entry, peer.Id, !visible);
                SendVisibility(entry, visible ? [peer] : [], visible ? [] : [peer]);
            }
        }
    }

    private void CheckInterest(NetworkObject entry, NetPeer peer, PeerInterest? interest)
    {
        // Objects of a section being sent reach each client inside it.
        if (entry.TearingDown || !entry.GameObject.IsValid() || FindEntry(entry.GameObject) != entry ||
            (_sectionBeingSent != null && ReferenceEquals(entry.GameObject.Section, _sectionBeingSent)))
        {
            return;
        }

        bool seen = Sees(entry, peer);
        if (ShouldSee(entry, peer, seen, interest) != seen)
        {
            _interestChanges.Add((entry, !seen));
        }
    }

    // The objects a periodic check can change for interest: the ones it has, and the ones near its viewpoints.
    private HashSet<NetworkObject> Nearby(PeerInterest interest)
    {
        if (!_grid.Built || _grid.BuiltTick != Tick || _grid.BuiltCallbacks != VisibilityCallbacks)
        {
            _grid.Build(Entries.Values, Configuration.InterestRadius, Tick, VisibilityCallbacks);
        }

        _nearby.Clear();
        foreach ((Float3 viewpoint, float radius) in interest.Viewpoints)
        {
            _grid.Gather(viewpoint, radius, _nearby);
        }

        foreach (NetworkObject entry in interest.InRangeAt.Keys)
        {
            _nearby.Add(entry);
        }

        return _nearby;
    }

    // The viewpoints of peer as gathered this tick, gathering them if they weren't.
    private PeerInterest ViewpointsOf(NetPeer peer)
    {
        PeerInterest interest = InterestOf(peer);
        return interest.Gathered && interest.GatheredTick == Tick ? interest : Gather(interest);
    }

    // Fills in the viewpoints interest sees from now: its own, or the observers its client owns.
    private PeerInterest Gather(PeerInterest interest)
    {
        List<(Float3 Position, float Radius)> viewpoints = interest.Viewpoints;
        viewpoints.Clear();
        interest.Gathered = true;
        interest.GatheredTick = Tick;
        float radius = Configuration.InterestRadius;
        if (interest.HasExplicit)
        {
            foreach (Float3 viewpoint in interest.Explicit)
            {
                viewpoints.Add((viewpoint, radius));
            }

            return interest;
        }

        List<NetworkObserver> observers = NetworkObserver.Observing;
        for (int i = 0; i < observers.Count; i++)
        {
            NetworkObserver observer = observers[i];
            if (observer.IsNotValid())
            {
                observers.RemoveAt(i--);
                continue;
            }

            if (FindEntry(observer.GameObject) is { TearingDown: false } owned &&
                owned.Owner == interest.Peer.Id)
            {
                viewpoints.Add((observer.Viewpoint, observer.Radius > 0 ? observer.Radius : radius));
            }
        }

        return interest;
    }

    private bool InRange(NetworkObject entry, PeerInterest interest, bool wasVisible)
    {
        GameObject go = entry.GameObject;
        Float3 position = go.Transform.Position;
        float cullDistance = go.Network.CullDistance;
        float scale = wasVisible ? 1 + Configuration.InterestHysteresis : 1;
        foreach ((Float3 viewpoint, float radius) in interest.Viewpoints)
        {
            float range = (cullDistance > 0 ? cullDistance : radius) * scale;
            if (Float3.LengthSquared(position - viewpoint) <= range * range)
            {
                interest.InRangeAt[entry] = Now;
                return true;
            }
        }

        return wasVisible && interest.InRangeAt.TryGetValue(entry, out double seen) &&
               Now - seen < Configuration.InterestLingerSeconds;
    }

    private void WarnWithoutViewpoint(PeerInterest interest)
    {
        if (interest.SeesEverything || interest.Viewpoints.Count > 0 || !_warnedNoViewpoint.Add(interest.Peer.Id))
        {
            return;
        }

        NetworkDiagnostics.Record("Interest", "Local", interest.Peer, System.Guid.Empty, "No viewpoint");
        Debug.LogWarning($"[Network] {interest.Peer.Name} has no viewpoint, so it only receives the objects it owns, " +
                         "Always ones and ones shown to it. Give it a NetworkObserver or set its viewpoints with Networking.Interest.");
    }

    private void ClearViewpoints()
    {
        foreach (PeerInterest interest in _interests.Values)
        {
            interest.ClearViewpoints();
        }
    }

    internal int VisibleCount(NetPeer peer)
    {
        int count = 0;
        foreach (NetworkObject entry in Entries.Values)
        {
            if (IsVisibleTo(entry, peer))
            {
                count++;
            }
        }

        return count;
    }

    internal void GetVisible(NetPeer peer, List<GameObject> results)
    {
        foreach (NetworkObject entry in Entries.Values)
        {
            if (IsVisibleTo(entry, peer))
            {
                results.Add(entry.GameObject);
            }
        }
    }

    private bool IsVisibleTo(NetworkObject entry, NetPeer peer) =>
        !entry.TearingDown && entry.GameObject.IsValid() && IsVisibleTo(entry.GameObject, peer);
}
