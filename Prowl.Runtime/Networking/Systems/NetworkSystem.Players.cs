// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// Each peer's player object, as Mirror's connection identity and local player.
internal sealed partial class NetworkSystem
{
    // Host: each peer's player.
    private readonly Dictionary<int, NetworkObject> _players = new();

    // This peer's own player, and on a client the one the host named before it arrived.
    private NetworkObject? _localPlayer;
    private Guid _pendingPlayer;

    internal GameObject? LocalPlayer => _localPlayer is { } entry && FindEntry(entry.GameObject) == entry ? entry.GameObject : null;

    internal GameObject? PlayerOf(NetPeer peer) =>
        _players.TryGetValue(peer.Id, out NetworkObject? entry) && FindEntry(entry.GameObject) == entry ? entry.GameObject : null;

    /// <summary>The id of the peer <paramref name="entry" /> is the player of, or null.</summary>
    private int? PlayerPeerOf(NetworkObject entry)
    {
        foreach ((int peer, NetworkObject player) in _players)
        {
            if (ReferenceEquals(player, entry))
            {
                return peer;
            }
        }

        return null;
    }

    /// <summary>Host: makes <paramref name="go" /> the player of <paramref name="peer" />, spawning it owned by them.</summary>
    internal void AddPlayer(NetPeer peer, GameObject go)
    {
        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can add players.");
        }

        ValidateOwner(peer);
        if (FindEntry(go) is { } spawned && spawned.GameObject == go)
        {
            SetOwner(go, peer);
        }
        else
        {
            Spawn(go, peer, go.Network.Orphaned);
        }

        if (FindEntry(go) is not { } entry || entry.GameObject != go)
        {
            return;
        }

        _players[peer.Id] = entry;
        if (peer.IsLocal)
        {
            SetLocalPlayer(entry);
        }
        else
        {
            SendPlayer(peer);
        }
    }

    /// <summary>Host: <paramref name="peer" /> has no player any more. The object stays spawned.</summary>
    internal void RemovePlayer(NetPeer peer)
    {
        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can remove players.");
        }

        if (!_players.Remove(peer.Id))
        {
            return;
        }

        if (peer.IsLocal)
        {
            SetLocalPlayer(null);
        }
        else
        {
            SendPlayer(peer);
        }
    }

    /// <summary>Tells a ready client which object is its player now.</summary>
    internal void SendPlayer(NetPeer peer)
    {
        if (!IsPeerReady(peer))
        {
            return;
        }

        using NetWriter message = new(16);
        message.WriteGuid(PlayerOf(peer) is { } player ? player.Identifier : Guid.Empty);
        SendEngine(peer, EngineMessage.Player, message.WrittenSpan);
    }

    internal void OnPlayer(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("A player message contains trailing bytes.");
        }

        _pendingPlayer = id;
        SetLocalPlayer(id != Guid.Empty && Entries.TryGetValue(id, out NetworkObject? entry) ? entry : null);
    }

    /// <summary>A client's player arrived after the host named it.</summary>
    internal void ClaimPendingPlayer(NetworkObject entry)
    {
        if (!IsHost && _pendingPlayer != Guid.Empty && entry.GameObject.Identifier == _pendingPlayer)
        {
            SetLocalPlayer(entry);
        }
    }

    /// <summary>A networked object is leaving: if it was a player, it isn't any more.</summary>
    internal void ForgetPlayer(NetworkObject entry)
    {
        if (_localPlayer == entry)
        {
            SetLocalPlayer(null);
        }

        foreach ((int peer, NetworkObject player) in _players)
        {
            if (player == entry)
            {
                _players.Remove(peer);
                break;
            }
        }
    }

    internal void ForgetPlayerOf(int peer) => _players.Remove(peer);

    private void SetLocalPlayer(NetworkObject? entry)
    {
        if (_localPlayer == entry)
        {
            return;
        }

        NetworkObject? previous = _localPlayer;
        _localPlayer = entry;
        if (previous != null && previous.GameObject.IsValid())
        {
            SceneEvents.RunLifecycle<INetworkEvents>(previous.GameObject, e => e.OnStopLocalPlayer());
        }

        if (entry != null && entry.GameObject.IsValid())
        {
            SceneEvents.RunLifecycle<INetworkEvents>(entry.GameObject, e => e.OnStartLocalPlayer());
        }
    }
}
