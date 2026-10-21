// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp;

/// <summary>Settings for a <see cref="NetSession"/>.</summary>
public sealed class NetSessionOptions
{
    /// <summary>
    /// Your game's version. The host turns away clients whose version differs, with
    /// <see cref="SessionEndReason.VersionMismatch"/>.
    /// </summary>
    public uint AppVersion { get; set; }

    /// <summary>The most clients that may be joined at once, not counting the host.</summary>
    public int MaxPeers { get; set; } = 64;

    /// <summary>
    /// How long a client may take to complete joining once connected. On the host this also bounds how
    /// long a <see cref="PendingJoin"/> may wait for a decision before it is refused.
    /// </summary>
    public TimeSpan JoinTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// After the host kicks or refuses a client, how long it waits for the client to hang up before
    /// closing the connection itself. Gives the reason time to arrive over a lossy link.
    /// </summary>
    public TimeSpan GoodbyeLinger { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The largest reliable message this side accepts, in bytes. Reliable messages bigger than the
    /// transport allows are split into fragments and reassembled; a sender that announces more than this
    /// is removed with <see cref="SessionEndReason.ProtocolError"/>. Bounds the memory a peer can make
    /// this side allocate.
    /// </summary>
    public int MaxMessageSize { get; set; } = 16 * 1024 * 1024;

    /// <summary>Maximum pooled bytes retained by all incomplete incoming messages in this session.</summary>
    public long MaxReassemblyBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Absolute deadline for a fragmented message, unaffected by keepalives or trickled fragments.</summary>
    public TimeSpan ReassemblyTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal NetSessionOptions CopyValidated()
    {
        if (MaxPeers < 0 || MaxPeers > 65534) throw new ArgumentOutOfRangeException(nameof(MaxPeers));
        if (MaxMessageSize < 64 || MaxMessageSize > 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaxMessageSize));
        if (MaxReassemblyBytes < 64) throw new ArgumentOutOfRangeException(nameof(MaxReassemblyBytes));
        ValidateDuration(ReassemblyTimeout, nameof(ReassemblyTimeout));
        ValidateDuration(JoinTimeout, nameof(JoinTimeout));
        ValidateDuration(GoodbyeLinger, nameof(GoodbyeLinger));
        return (NetSessionOptions)MemberwiseClone();
    }

    internal static void ValidateDuration(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(name, "Duration must be positive and at most one day.");
    }
}

/// <summary>Why a session ended, or why a peer left it.</summary>
public enum SessionEndReason : byte
{
    /// <summary>The peer left on purpose: <see cref="NetSession.Shutdown"/> locally, or a client closing its connection.</summary>
    Left,

    /// <summary>The host removed the peer with <see cref="NetSession.Kick"/>.</summary>
    Kicked,

    /// <summary>The host refused the join, through <see cref="PendingJoin.Reject"/> or a transport level refusal.</summary>
    Rejected,

    /// <summary>The client's <see cref="NetSessionOptions.AppVersion"/> differs from the host's.</summary>
    VersionMismatch,

    /// <summary>The host has no room for another peer.</summary>
    ServerFull,

    /// <summary>The host shut the session down.</summary>
    HostShutdown,

    /// <summary>The connection went silent, or joining took longer than <see cref="NetSessionOptions.JoinTimeout"/>.</summary>
    Timeout,

    /// <summary>The host could not be reached at all.</summary>
    Unreachable,

    /// <summary>The remote end sent something malformed.</summary>
    ProtocolError,

    /// <summary>A transport or session resource budget was exceeded.</summary>
    ResourceLimit,

    /// <summary>The secure transport could not authenticate its peer.</summary>
    AuthenticationFailed,
}
