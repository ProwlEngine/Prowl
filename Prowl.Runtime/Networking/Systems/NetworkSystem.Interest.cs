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
    private NetworkInterest? _interest;
    private InterestMode _interestMode;
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
        if (Configuration.InterestMode != _interestMode)
        {
            _interestMode = Configuration.InterestMode;
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

            CheckInterest(peer);
        }
    }

    internal void RefreshInterest(PeerInterest interest)
    {
        if (IsActive && IsHost && ReferenceEquals(InterestOf(interest.Peer), interest))
        {
            CheckInterest(interest.Peer);
        }
    }

    private void CheckInterest(NetPeer peer)
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
        foreach (NetworkObject entry in Entries.Values)
        {
            // Objects of a section being sent reach each client inside it.
            if (entry.TearingDown || !entry.GameObject.IsValid() ||
                (_sectionBeingSent != null && ReferenceEquals(entry.GameObject.Section, _sectionBeingSent)))
            {
                continue;
            }

            bool seen = Sees(entry, peer);
            if (ShouldSee(entry, peer, seen, interest) != seen)
            {
                _interestChanges.Add((entry, !seen));
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
