// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp;

/// <summary>What a client sent when asking to join.</summary>
public sealed class JoinRequest
{
    internal JoinRequest(uint appVersion, string name, byte[] token)
    {
        AppVersion = appVersion;
        Name = name;
        Token = token;
    }

    public uint AppVersion { get; }

    /// <summary>The name the client asked to join with. Untrusted: validate before displaying it.</summary>
    public string Name { get; }

    /// <summary>Opaque data from the client, such as an auth ticket. Empty if none was given.</summary>
    public ReadOnlyMemory<byte> Token { get; }
}

/// <summary>
/// A client waiting for the host to let it in, handed to <see cref="NetSession.JoinRequested"/>.
/// Call <see cref="Accept"/> or <see cref="Reject"/>, straight away or later (after checking an auth
/// ticket, say). Undecided joins are refused after <see cref="NetSessionOptions.JoinTimeout"/>.
/// </summary>
public sealed class PendingJoin
{
    private readonly NetSession _session;

    internal PendingJoin(NetSession session, Connection connection, JoinRequest request)
    {
        _session = session;
        Connection = connection;
        Request = request;
    }

    public JoinRequest Request { get; }

    /// <summary>The client's connection, for its address and round trip time.</summary>
    public Connection Connection { get; }

    /// <summary>True once accepted, rejected, timed out or disconnected. Further calls then do nothing.</summary>
    public bool IsResolved { get; internal set; }

    /// <summary>Lets the client in. It becomes a <see cref="NetPeer"/> and <see cref="NetSession.PeerJoined"/> fires.</summary>
    public void Accept() => _session.ResolveJoin(this, accept: true, null);

    /// <summary>Turns the client away. It ends with <see cref="SessionEndReason.Rejected"/> and this message.</summary>
    public void Reject(string message) => _session.ResolveJoin(this, accept: false, message);
}
