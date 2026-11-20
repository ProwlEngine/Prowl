// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// Which clients have which networked objects, decided on the host as soon as anything deciding it changes.
internal sealed partial class NetworkSystem
{
    // Networked objects at least one client does not have, so their messages go peer by peer.
    internal readonly HashSet<NetworkObject> Restricted = new();
    internal readonly Dictionary<GameObject, Dictionary<int, bool>> VisibilityOverrides = new(ReferenceEqualityComparer.Instance);
    private readonly List<NetPeer> _entering = new(), _leaving = new();

    /// <summary>The game's own interest rule, set through <see cref="NetworkInterest.Rule" />.</summary>
    internal Func<GameObject, NetPeer, bool?>? InterestRule;

    private bool _warnedHiddenOwner;

    // A section being registered and sent, whose objects reach each client inside it.
    private SceneSection? _sectionBeingSent;

    internal static bool Sees(NetworkObject? entry, NetPeer peer) =>
        entry?.HiddenFrom is not { } hidden || !hidden.Contains(peer.Id);

    /// <summary>False when <paramref name="peer" /> does not have the networked object <paramref name="go" /> belongs to.</summary>
    internal bool Sees(GameObject go, NetPeer peer) => Restricted.Count == 0 || Sees(FindEntry(go), peer);

    /// <summary>On the host, <paramref name="peer" /> when it is a client, whose references are checked as they are read.</summary>
    internal NetPeer? RemoteClient(NetPeer peer) => IsHost && !peer.IsLocal ? peer : null;

    // What the visibility rules say about peer having go, or null to leave it to interest.
    private bool? Rule(GameObject go, int owner, NetPeer peer)
    {
        if (peer.IsLocal || owner == peer.Id)
        {
            return true;
        }

        if (VisibilityOverrides.TryGetValue(go, out Dictionary<int, bool>? forced) &&
            forced.TryGetValue(peer.Id, out bool visible))
        {
            return visible;
        }

        switch (go.Network.Visibility)
        {
            case NetworkVisibility.Always:
                return true;
            case NetworkVisibility.OwnerOnly or NetworkVisibility.Manual:
                return false;
        }

        if (InterestRule?.Invoke(go, peer) is { } custom)
        {
            return custom;
        }

        _interests.TryGetValue(peer.Id, out PeerInterest? interest);
        if (interest?.SeesEverything == true)
        {
            return true;
        }

        NetworkGroups groups = go.Network.Groups;
        return groups.IsEmpty || interest?.Groups.Overlaps(groups) == true ? null : false;
    }

    private bool ShouldSee(NetworkObject entry, NetPeer peer, bool wasVisible = false, PeerInterest? gathered = null)
    {
        if (Rule(entry.GameObject, entry.Owner, peer) is { } decided)
        {
            return decided;
        }

        if (Configuration.InterestMode == InterestMode.None)
        {
            return true;
        }

        return InRange(entry, gathered ?? ViewpointsOf(peer), wasVisible);
    }

    private bool HasCurrentWorld(NetPeer peer) =>
        LoadingPeers.TryGetValue(peer.Id, out PeerReadiness? state) && state.Generation == SceneGeneration;

    private void SetHidden(NetworkObject entry, int peer, bool hidden)
    {
        if (!hidden && Configuration.InterestMode == InterestMode.None)
        {
            _revealedWithoutInterest = true;
        }
        else if (_interests.Count > 0 && _interests.TryGetValue(peer, out PeerInterest? interest))
        {
            if (hidden)
            {
                interest.InRangeAt.Remove(entry);
            }
            else
            {
                interest.InRangeAt[entry] = Now;
            }
        }

        if (hidden)
        {
            (entry.HiddenFrom ??= new HashSet<int>()).Add(peer);
            Restricted.Add(entry);
        }
        else if (entry.HiddenFrom is { } set && set.Remove(peer) && set.Count == 0)
        {
            Restricted.Remove(entry);
        }
    }

    /// <summary>Sends to every client that has <paramref name="entry" />, as one broadcast when they all do.</summary>
    internal void SendToObservers(NetworkObject? entry, EngineMessage kind, ReadOnlySpan<byte> payload,
        Delivery delivery = Delivery.Reliable, NetPeer? except = null, byte channel = 0)
    {
        if (entry?.HiddenFrom is not { Count: > 0 } hidden)
        {
            BroadcastEngine(kind, payload, delivery, except, channel);
            return;
        }

        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal && peer != except && !hidden.Contains(peer.Id))
            {
                SendEngine(peer, kind, payload, delivery, channel);
            }
        }
    }

    /// <summary>Works out which clients have <paramref name="entry" /> when it becomes networked, sending nothing.</summary>
    private void InitVisibility(NetworkObject entry)
    {
        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal)
            {
                SetHidden(entry, peer.Id, !ShouldSee(entry, peer));
            }
        }
    }

    /// <summary>Works out every object <paramref name="peer" /> has, as it is sent the world.</summary>
    private void ResetVisibility(NetPeer peer)
    {
        PeerInterest? interest = null;
        if (Configuration.InterestMode != InterestMode.None)
        {
            interest = Gather(InterestOf(peer));
            interest.InRangeAt.Clear();
        }

        foreach (NetworkObject entry in Entries.Values)
        {
            SetHidden(entry, peer.Id, !ShouldSee(entry, peer, false, interest));
        }
    }

    private void ForgetPeerVisibility(int peer)
    {
        _interests.Remove(peer);
        _warnedNoViewpoint.Remove(peer);
        foreach (NetworkObject entry in new List<NetworkObject>(Restricted))
        {
            SetHidden(entry, peer, false);
        }

        foreach (Dictionary<int, bool> forced in VisibilityOverrides.Values)
        {
            forced.Remove(peer);
        }
    }

    private void ForgetVisibility()
    {
        _interests.Clear();
        _warnedNoViewpoint.Clear();
        _grid.Clear();
        _nearby.Clear();
        Restricted.Clear();
        VisibilityOverrides.Clear();
    }

    private void ForgetInRange(NetworkObject entry)
    {
        foreach (PeerInterest interest in _interests.Values)
        {
            interest.InRangeAt.Remove(entry);
        }
    }

    internal void CheckVisibilityChange(GameObject go)
    {
        if (IsActive && !IsHost && FindEntry(go) is { TearingDown: false } entry && entry.GameObject == go)
        {
            throw new InvalidOperationException("Only the host can change a spawned object's visibility.");
        }

        if (IsActive)
        {
            Sync.RefuseWhileWriting("Who receives an object can't be changed");
        }
    }

    internal void RefreshVisibility(GameObject go)
    {
        if (IsActive && IsHost && FindEntry(go) is { TearingDown: false } entry && entry.GameObject == go)
        {
            Sync.RefuseWhileWriting("Who receives an object can't be changed");
            RefreshVisibility(entry);
        }
    }

    internal bool IsVisibleTo(GameObject go, NetPeer peer)
    {
        if (!IsActive || FindEntry(go) is not { } entry)
        {
            return true;
        }

        return peer.IsLocal || (IsHost && Sees(entry, peer));
    }

    internal void SetVisibilityOverride(GameObject go, NetPeer peer, bool? visible)
    {
        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can change who receives a networked object.");
        }

        Sync.RefuseWhileWriting("Who receives an object can't be changed");

        if (peer.IsLocal || !Session!.TryGetPeer(peer.Id, out NetPeer? current) || !ReferenceEquals(current, peer))
        {
            return;
        }

        NetworkObject? entry = FindEntry(go) is { } found && found.GameObject == go ? found : null;
        if (visible == false && entry?.Owner == peer.Id)
        {
            if (!_warnedHiddenOwner)
            {
                _warnedHiddenOwner = true;
                Debug.LogWarning($"[Network] '{go.Name}' can't be hidden from {peer.Name}, its owner, so HideFrom was ignored.");
            }

            return;
        }

        if (visible is { } value)
        {
            if (!VisibilityOverrides.TryGetValue(go, out Dictionary<int, bool>? forced))
            {
                VisibilityOverrides[go] = forced = new Dictionary<int, bool>();
            }

            forced[peer.Id] = value;
        }
        else if (VisibilityOverrides.TryGetValue(go, out Dictionary<int, bool>? forced) && forced.Remove(peer.Id) &&
                 forced.Count == 0)
        {
            VisibilityOverrides.Remove(go);
        }

        if (entry is { TearingDown: false })
        {
            RefreshVisibility(entry);
        }
    }

    /// <summary>Sends <paramref name="entry" /> to the clients that should have it now, and despawns it where they shouldn't.</summary>
    internal void RefreshVisibility(NetworkObject entry)
    {
        if (!IsActive || !IsHost)
        {
            return;
        }

        NetSession session = Session!;
        bool sending = _sectionBeingSent == null || !ReferenceEquals(entry.GameObject.Section, _sectionBeingSent);
        _entering.Clear();
        _leaving.Clear();
        foreach (NetPeer peer in session.Peers)
        {
            if (peer.IsLocal)
            {
                continue;
            }

            bool seen = Sees(entry, peer), should = ShouldSee(entry, peer, seen);
            if (should == seen)
            {
                continue;
            }

            SetHidden(entry, peer.Id, !should);
            if (sending && HasCurrentWorld(peer))
            {
                (should ? _entering : _leaving).Add(peer);
            }
        }

        NetPeer[] entering = [.. _entering], leaving = [.. _leaving];
        SendVisibility(entry, entering, leaving);
    }

    // Removes entry from leaving and sends it whole to entering, then tells its listeners.
    private void SendVisibility(NetworkObject entry, NetPeer[] entering, NetPeer[] leaving)
    {
        GameObject go = entry.GameObject;
        if (leaving.Length > 0)
        {
            using NetWriter despawn = new(32);
            new DespawnMessage(go.Identifier, NetworkDespawnReason.OutOfInterest).Write(despawn);
            foreach (NetPeer peer in leaving)
            {
                SendEngine(peer, EngineMessage.Despawn, despawn.WrittenSpan);
                Transforms.Withdraw(peer, go);
            }
        }

        if (entering.Length > 0)
        {
            SendSpawn(entry, entering, true);
        }

        foreach (NetPeer peer in leaving)
        {
            entry.NotifyVisibility(peer, false);
        }

        foreach (NetPeer peer in entering)
        {
            entry.NotifyVisibility(peer, true);
        }
    }

    // Sends entry whole to peers, written once for all of them.
    private void SendSpawn(NetworkObject entry, IReadOnlyList<NetPeer> peers, bool entering)
    {
        GameObject go = entry.GameObject;
        using NetWriter message = new(4096);
        WriteSpawn(entry, message, entering);
        _lastSpawnBytes = message.Length;
        foreach (NetPeer peer in peers)
        {
            SendEngine(peer, EngineMessage.Spawn, message.WrittenSpan);
        }

        foreach (NetPeer peer in peers)
        {
            Transforms.Delivered(peer, go);

            // A spawn is written for everyone, so what only the owner gets follows to it.
            if (peer.Id == entry.Owner)
            {
                Sync.QueueMembers(peer, entry, static sync => (sync.Flags & SyncFlags.ToOwner) != 0);
            }
        }
    }
}
