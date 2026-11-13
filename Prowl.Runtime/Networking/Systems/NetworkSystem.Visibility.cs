// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

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

    private bool ShouldSee(GameObject go, int owner, NetPeer peer)
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

        return go.Network.Visibility is NetworkVisibility.Default or NetworkVisibility.Always;
    }

    private bool ShouldSee(NetworkObject entry, NetPeer peer) => ShouldSee(entry.GameObject, entry.Owner, peer);

    private bool HasCurrentWorld(NetPeer peer) =>
        LoadingPeers.TryGetValue(peer.Id, out PeerReadiness? state) && state.Generation == SceneGeneration;

    private void SetHidden(NetworkObject entry, int peer, bool hidden)
    {
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
            if (!peer.IsLocal && !ShouldSee(entry, peer))
            {
                SetHidden(entry, peer.Id, true);
            }
        }
    }

    /// <summary>Works out every object <paramref name="peer" /> has, as it is sent the world.</summary>
    private void ResetVisibility(NetPeer peer)
    {
        foreach (NetworkObject entry in Entries.Values)
        {
            SetHidden(entry, peer.Id, !ShouldSee(entry, peer));
        }
    }

    private void ForgetPeerVisibility(int peer)
    {
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
        Restricted.Clear();
        _sentAsContent.Clear();
        VisibilityOverrides.Clear();
    }

    /// <summary>The networked roots <paramref name="peer" /> does not have, or null if it has them all.</summary>
    private HashSet<GameObject>? HiddenRoots(NetPeer peer, IEnumerable<NetworkObject>? among = null)
    {
        HashSet<GameObject>? roots = null;
        foreach (NetworkObject entry in among ?? Restricted)
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

        return _sentAsContent.Contains(go) || go.GetChildrenDeep().Any(_sentAsContent.Contains);
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
            bool should = ShouldSee(entry, peer);
            if (peer.IsLocal || should == Sees(entry, peer))
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
            using NetWriter spawn = new(4096);
            WriteSpawn(go, entry.Owner, entry.Orphaned, spawn, entry.Generation, entry.NetId, true);
            foreach (NetPeer peer in entering)
            {
                SendEngine(peer, EngineMessage.Spawn, spawn.WrittenSpan);
                Snapshots.Delivered(peer, go);
            }
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
}
