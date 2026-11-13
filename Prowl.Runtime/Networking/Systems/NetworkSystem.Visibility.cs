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

    // Objects written as scene content, which clients may hold.
    private readonly HashSet<GameObject> _sentAsContent = new(ReferenceEqualityComparer.Instance);
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

        return go.Network.Visibility switch
        {
            NetworkVisibility.Always => true,
            NetworkVisibility.OwnerOnly or NetworkVisibility.Manual => false,
            _ => null
        };
    }

    private bool ShouldSee(GameObject go, int owner, NetPeer peer) => Rule(go, owner, peer) ?? true;

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

        PeerInterest interest = gathered ?? ViewpointsOf(peer);
        return interest.SeesEverything || InRange(entry, interest, wasVisible);
    }

    internal bool IsForcedHidden(GameObject go) =>
        VisibilityOverrides.TryGetValue(go, out Dictionary<int, bool>? forced) && forced.ContainsValue(false);

    private bool HasCurrentWorld(NetPeer peer) =>
        LoadingPeers.TryGetValue(peer.Id, out PeerReadiness? state) && state.Generation == SceneGeneration;

    private void SetHidden(NetworkObject entry, int peer, bool hidden)
    {
        if (_interests.Count > 0 && Configuration.InterestMode != InterestMode.None &&
            _interests.TryGetValue(peer, out PeerInterest? interest))
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
    private void InitVisibility(NetworkObject entry, bool heldAsContent = false)
    {
        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal)
            {
                SetHidden(entry, peer.Id,
                    !(heldAsContent ? ShouldSee(entry.GameObject, entry.Owner, peer) : ShouldSee(entry, peer)));
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
        Restricted.Clear();
        _sentAsContent.Clear();
        VisibilityOverrides.Clear();
    }

    /// <summary>The networked roots <paramref name="peer" /> does not have, or null if it has them all.</summary>
    private HashSet<GameObject>? HiddenRoots(NetPeer peer)
    {
        HashSet<GameObject>? roots = null;
        foreach (NetworkObject entry in Restricted)
        {
            if (!Sees(entry, peer) && entry.GameObject.IsValid())
            {
                (roots ??= new HashSet<GameObject>(ReferenceEqualityComparer.Instance)).Add(entry.GameObject);
            }
        }

        return roots;
    }

    /// <summary>Runs <paramref name="write" /> leaving out the networked roots in <paramref name="roots" />.</summary>
    private static void WithoutRoots(HashSet<GameObject>? roots, Action write)
    {
        HashSet<GameObject>? previous = NetworkSerialization.HiddenRoots;
        NetworkSerialization.HiddenRoots = roots;
        try
        {
            write();
        }
        finally
        {
            NetworkSerialization.HiddenRoots = previous;
        }
    }

    /// <summary>True if a client that would not get <paramref name="go" /> may hold it as scene content already.</summary>
    private bool HiddenFromItsCopies(GameObject go, int owner)
    {
        if (!HiddenFromAnyLoadedPeer(go, owner))
        {
            return false;
        }

        return HeldAsContent(go);
    }

    // True if go, or part of its tree, was sent to clients as scene content.
    private bool HeldAsContent(GameObject go)
    {
        if (_sentAsContent.Count == 0)
        {
            return false;
        }

        if (_sentAsContent.Contains(go))
        {
            return true;
        }

        foreach (GameObject child in go.Children)
        {
            if (HeldAsContent(child))
            {
                return true;
            }
        }

        return false;
    }

    private void ForgetInRange(NetworkObject entry)
    {
        foreach (PeerInterest interest in _interests.Values)
        {
            interest.InRangeAt.Remove(entry);
        }
    }

    private bool HiddenFromAnyLoadedPeer(GameObject go, int owner)
    {
        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal && LoadingPeers.ContainsKey(peer.Id) && !ShouldSee(go, owner, peer))
            {
                return true;
            }
        }

        return false;
    }

    internal void NoteSentObjects(IEnumerable<GameObject> objects) => _sentAsContent.UnionWith(objects);

    private void ForgetSentContent(GameObject go)
    {
        if (_sentAsContent.Count > 0)
        {
            _sentAsContent.Remove(go);
        }
    }

    internal void CheckVisibilityChange(GameObject go)
    {
        if (IsActive && !IsHost && FindEntry(go) is { TearingDown: false } entry && entry.GameObject == go)
        {
            throw new InvalidOperationException("Only the host can change a spawned object's visibility.");
        }
    }

    internal void RefreshVisibility(GameObject go)
    {
        if (IsActive && IsHost && FindEntry(go) is { TearingDown: false } entry && entry.GameObject == go)
        {
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
                Snapshots.Withdraw(peer, go);
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

    // Sends entry whole to peers, written once for each group of them that has the same secret objects it links to.
    private void SendSpawn(NetworkObject entry, IReadOnlyList<NetPeer> peers, bool entering)
    {
        if (SendSpawn(entry, peers, entering, true) is not { } partlyHidden)
        {
            return;
        }

        List<(bool[] Seen, List<NetPeer> Peers)> groups = [];
        foreach (NetPeer peer in peers)
        {
            bool[] seen = new bool[partlyHidden.Count];
            for (int i = 0; i < seen.Length; i++)
            {
                seen[i] = Sees(FindEntry(partlyHidden[i]), peer);
            }

            int group = groups.FindIndex(g => g.Seen.AsSpan().SequenceEqual(seen));
            if (group < 0)
            {
                groups.Add((seen, [peer]));
            }
            else
            {
                groups[group].Peers.Add(peer);
            }
        }

        foreach ((_, List<NetPeer> members) in groups)
        {
            SendSpawn(entry, members, entering, false);
        }
    }

    private List<GameObject>? SendSpawn(NetworkObject entry, IReadOnlyList<NetPeer> peers, bool entering, bool everyone)
    {
        GameObject go = entry.GameObject;
        List<WrittenValue> written = new();
        using NetWriter message = new(4096);
        using (new NetworkSerialization.WritingFor(null, peers, written, everyone))
        {
            WriteSpawn(go, entry.Owner, entry.Orphaned, message, entry.Generation, entry.NetId, entering);
            if (NetworkSerialization.PartlyHidden is { } partlyHidden)
            {
                return partlyHidden;
            }
        }

        if (everyone && !entering)
        {
            SendToObservers(entry, EngineMessage.Spawn, message.WrittenSpan);
        }
        else
        {
            foreach (NetPeer peer in peers)
            {
                SendEngine(peer, EngineMessage.Spawn, message.WrittenSpan);
            }
        }

        foreach (NetPeer peer in peers)
        {
            Snapshots.Delivered(peer, go);
            if (entering)
            {
                Snapshots.Entered(peer, go, written, peers.Count > 1);
            }
            else
            {
                Snapshots.Wrote(peer, written, peers.Count > 1);
            }
        }

        return null;
    }
}
