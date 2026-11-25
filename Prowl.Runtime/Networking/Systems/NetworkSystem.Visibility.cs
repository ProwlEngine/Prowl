// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// Which clients have which networked objects, decided on the host by each object's visibility and the interest.
internal sealed partial class NetworkSystem
{
    // Networked objects at least one client does not have, so their messages go peer by peer.
    internal readonly HashSet<NetworkObject> Restricted = new();
    private readonly HashSet<NetPeer> _observers = new();
    private readonly List<NetPeer> _entering = new(), _leaving = new();

    // A section being registered and sent, whose objects reach each client inside it.
    private SceneSection? _sectionBeingSent;

    internal static bool Sees(NetworkObject? entry, NetPeer peer) =>
        entry?.HiddenFrom is not { } hidden || !hidden.Contains(peer.Id);

    /// <summary>False when <paramref name="peer" /> does not have the networked object <paramref name="go" /> belongs to.</summary>
    internal bool Sees(GameObject go, NetPeer peer) => Restricted.Count == 0 || Sees(FindEntry(go), peer);

    /// <summary>On the host, <paramref name="peer" /> when it is a client, whose references are checked as they are read.</summary>
    internal NetPeer? RemoteClient(NetPeer peer) => IsHost && !peer.IsLocal ? peer : null;

    // The session's clients, made again after a peer joins or leaves rather than cleared, so a list in use stays whole.
    private List<NetPeer>? _remotePeers;

    internal List<NetPeer> RemotePeers()
    {
        if (_remotePeers != null)
        {
            return _remotePeers;
        }

        List<NetPeer> peers = new();
        foreach (NetPeer peer in Session?.Peers ?? [])
        {
            if (!peer.IsLocal)
            {
                peers.Add(peer);
            }
        }

        return Session == null ? peers : _remotePeers = peers;
    }

    private void ForgetRemotePeers() => _remotePeers = null;

    // What the owner and the object's visibility decide for peer, or null to leave it to the interest.
    private static bool? Forced(NetworkObject entry, NetPeer peer)
    {
        if (peer.IsLocal || entry.Owner == peer.Id)
        {
            return true;
        }

        return entry.GameObject.Network.Visibility switch
        {
            NetworkVisibility.ForceShown => true,
            NetworkVisibility.ForceHidden => false,
            _ => Networking.Interest is null ? true : null
        };
    }

    private static bool ShouldSee(NetworkObject entry, NetPeer peer) =>
        Forced(entry, peer) ?? Networking.Interest!.OnCheckObserver(entry.GameObject, peer);

    // Asks the interest who observes entry, when it is the interest that decides.
    private bool GatherObservers(NetworkObject entry)
    {
        _observers.Clear();
        if (entry.GameObject.Network.Visibility != NetworkVisibility.Default || Networking.Interest is not { } interest)
        {
            return false;
        }

        interest.OnRebuildObservers(entry.GameObject, _observers);
        return true;
    }

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
        if (IsHost)
        {
            Networking.Interest?.OnSpawned(entry.GameObject);
        }

        bool asked = GatherObservers(entry);
        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal)
            {
                SetHidden(entry, peer.Id, !(Forced(entry, peer) ?? (!asked || _observers.Contains(peer))));
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
    }

    private void ForgetVisibility()
    {
        Restricted.Clear();
        ForgetRemotePeers();
        Networking.Interest?.ResetState();
    }

    // Tells the interest a networked object left the host.
    private void ForgetObserved(NetworkObject entry)
    {
        Restricted.Remove(entry);
        if (IsHost)
        {
            Networking.Interest?.OnDestroyed(entry.GameObject);
        }
    }

    /// <summary>The interest was replaced: it learns of every object, and every object's observers are worked out again.</summary>
    internal void OnInterestChanged()
    {
        if (!IsActive || !IsHost)
        {
            return;
        }

        Sync.RefuseWhileWriting("The interest can't be changed");
        if (Networking.Interest is { } interest)
        {
            interest.ResetState();
            foreach (NetworkObject entry in new List<NetworkObject>(Entries.Values))
            {
                interest.OnSpawned(entry.GameObject);
            }
        }

        RebuildAllObservers();
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

    internal void RebuildObservers(GameObject go)
    {
        if (IsActive && IsHost && FindEntry(go) is { TearingDown: false } entry && entry.GameObject == go)
        {
            Sync.RefuseWhileWriting("Who receives an object can't be changed");
            RebuildObservers(entry);
        }
    }

    internal void RebuildAllObservers()
    {
        if (!IsActive || !IsHost)
        {
            return;
        }

        Sync.RefuseWhileWriting("Who receives an object can't be changed");
        NetSession session = Session!;
        foreach (NetworkObject entry in new List<NetworkObject>(Entries.Values))
        {
            if (Session != session)
            {
                return;
            }

            if (!entry.TearingDown && FindEntry(entry.GameObject) == entry)
            {
                RebuildObservers(entry);
            }
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

    /// <summary>Sends <paramref name="entry" /> to the clients that should have it now, and despawns it where they shouldn't.</summary>
    internal void RebuildObservers(NetworkObject entry)
    {
        if (!IsActive || !IsHost)
        {
            return;
        }

        NetSession session = Session!;
        bool sending = _sectionBeingSent == null || !ReferenceEquals(entry.GameObject.Section, _sectionBeingSent);
        bool asked = GatherObservers(entry);
        _entering.Clear();
        _leaving.Clear();
        foreach (NetPeer peer in session.Peers)
        {
            if (peer.IsLocal)
            {
                continue;
            }

            bool seen = Sees(entry, peer), should = Forced(entry, peer) ?? (!asked || _observers.Contains(peer));
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

        if (_entering.Count > 0 || _leaving.Count > 0)
        {
            SendVisibility(entry, [.. _entering], [.. _leaving]);
        }
    }

    // Removes entry from leaving and sends it whole to entering.
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
    }

    // Sends entry whole to peers, written once for all of them.
    private void SendSpawn(NetworkObject entry, IReadOnlyList<NetPeer> peers, bool entering)
    {
        GameObject go = entry.GameObject;
        using NetWriter message = new(4096);
        WriteSpawn(entry, message, entering);
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
                Sync.QueueMembers(peer, entry, SyncFlags.ToOwner);
            }
        }
    }
}
