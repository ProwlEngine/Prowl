// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp;

/// <summary>
/// A participant in a <see cref="NetSession"/>: the host, or a client that has joined. Every member
/// of the session sees the same peers with the same ids.
/// </summary>
public sealed class NetPeer
{
    /// <summary>The id the host is always given.</summary>
    public const int HostId = 0;

    internal NetPeer(int id, string name, bool isLocal, Connection? connection)
    {
        Id = id;
        Name = name;
        IsLocal = isLocal;
        Connection = connection;
    }

    /// <summary>Assigned by the host when the peer joins, and never reused within the session. The host is <see cref="HostId"/>.</summary>
    public int Id { get; }

    /// <summary>The name the peer joined with.</summary>
    public string Name { get; }

    public bool IsHost => Id == HostId;

    /// <summary>True for the peer that represents this process.</summary>
    public bool IsLocal { get; }

    /// <summary>
    /// The connection to this peer, if this process has one: on the host, every client's; on a client,
    /// only the host's. Null for the local peer and for other clients seen from a client.
    /// </summary>
    public Connection? Connection { get; }

    /// <summary>Round trip time to this peer, or zero when there is no direct connection.</summary>
    public TimeSpan RoundTripTime => Connection?.RoundTripTime ?? TimeSpan.Zero;

    /// <summary>Free slot for whoever is using the session, such as a player object.</summary>
    public object? UserData { get; set; }

    public override string ToString() => $"{Name} (#{Id}{(IsHost ? ", host" : string.Empty)}{(IsLocal ? ", local" : string.Empty)})";
}
