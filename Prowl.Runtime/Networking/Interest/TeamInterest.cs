// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;
using Prowl.Echo;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A networked object with a <see cref="NetworkTeam" /> reaches the clients that own an object of the same team.
///     One with no team, or one that is <see cref="NetworkTeam.ForceShown" />, reaches every client, and one whose team
///     is empty reaches none.
/// </summary>
public sealed class TeamInterest : NetworkInterest
{
    private readonly Dictionary<string, HashSet<NetworkTeam>> _teams = new();
    private readonly HashSet<string> _dirty = new();

    public override bool OnCheckObserver(GameObject go, NetPeer peer)
    {
        if (go.GetComponent<NetworkTeam>() is not { } team || team.ForceShown)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(team.TeamId))
        {
            return false;
        }

        if (Networking.PlayerOf(peer) is not { } player || player.IsNotValid() ||
            player.GetComponent<NetworkTeam>() is not { } viewer)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(viewer.TeamId) && viewer.TeamId == team.TeamId;
    }

    public override void OnRebuildObservers(GameObject go, HashSet<NetPeer> observers)
    {
        if (go.GetComponent<NetworkTeam>() is not { } team || team.ForceShown)
        {
            foreach (NetPeer peer in Clients())
            {
                observers.Add(peer);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(team.TeamId) || !_teams.TryGetValue(team.TeamId, out HashSet<NetworkTeam>? members))
        {
            return;
        }

        foreach (NetworkTeam member in members)
        {
            if (member.IsValid() && member.GameObject.Network.Owner is { } owner)
            {
                observers.Add(owner);
            }
        }
    }

    public override void OnSpawned(GameObject go)
    {
        if (go.GetComponent<NetworkTeam>() is { } team && !string.IsNullOrWhiteSpace(team.TeamId))
        {
            Join(team, team.TeamId);
        }
    }

    public override void OnDestroyed(GameObject go)
    {
        if (go.GetComponent<NetworkTeam>() is { } team && !string.IsNullOrWhiteSpace(team.TeamId))
        {
            Leave(team, team.TeamId);
        }
    }

    public override void ResetState()
    {
        _teams.Clear();
        _dirty.Clear();
    }

    public override void Update()
    {
        if (_dirty.Count == 0)
        {
            return;
        }

        string[] dirty = [.. _dirty];
        _dirty.Clear();
        foreach (string id in dirty)
        {
            if (!_teams.TryGetValue(id, out HashSet<NetworkTeam>? members))
            {
                continue;
            }

            foreach (NetworkTeam member in new List<NetworkTeam>(members))
            {
                if (member.IsValid())
                {
                    Rebuild(member.GameObject);
                }
            }

            members.RemoveWhere(m => m.IsNotValid());
            if (members.Count == 0)
            {
                _teams.Remove(id);
            }
        }
    }

    internal void OnTeamChanged(NetworkTeam team, string? previous)
    {
        if (!team.GameObject.Network.Active)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(previous))
        {
            Leave(team, previous);
        }

        if (!string.IsNullOrWhiteSpace(team.TeamId))
        {
            Join(team, team.TeamId);
        }

        Rebuild(team.GameObject);
    }

    private void Join(NetworkTeam team, string id)
    {
        if (!_teams.TryGetValue(id, out HashSet<NetworkTeam>? members))
        {
            _teams[id] = members = new HashSet<NetworkTeam>();
        }

        members.Add(team);
        _dirty.Add(id);
    }

    private void Leave(NetworkTeam team, string id)
    {
        if (_teams.TryGetValue(id, out HashSet<NetworkTeam>? members) && members.Remove(team))
        {
            _dirty.Add(id);
        }
    }
}

/// <summary>The team a networked object is on, for <see cref="TeamInterest" />. Put it on the networked object itself.</summary>
[AddComponentMenu("Networking/Network Team")]
public sealed class NetworkTeam : Component
{
    [SerializeField] private string _teamId = "";

    /// <summary>The team, the same on every object of it. Only the host changes it while a session runs.</summary>
    public string TeamId
    {
        get => _teamId;
        set
        {
            if (Networking.IsClient)
            {
                throw new InvalidOperationException("Only the host changes an object's team.");
            }

            string previous = _teamId;
            _teamId = value ?? "";
            if (previous != _teamId && Networking.IsHost && Networking.Interest is TeamInterest interest)
            {
                interest.OnTeamChanged(this, previous);
            }
        }
    }

    /// <summary>Every client receives this object, whatever its team.</summary>
    public bool ForceShown;
}
