// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Diagnostics;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Hosts or joins the engine's replicated scene session. Additional service connections use Courier directly.</summary>
public static partial class Networking
{
    /// <summary>Message IDs from here up are reserved for engine replication.</summary>
    public const ushort EngineMessageIds = 0xFE00;

    internal static NetworkSystem? System { get; private set; }

    /// <summary>The current engine session, or null without one.</summary>
    public static NetSession? Session { get; private set; }

    /// <summary>The bound IP port, or zero for a transport without an IP listener.</summary>
    public static int LocalPort => s_transport?.LocalPort ?? 0;

    /// <summary>Session admission completed. Use IsReady to check scene readiness.</summary>
    public static bool IsActive => Session?.State == SessionState.Active;

    public static bool IsHost => Session?.IsHost == true;
    public static bool IsClient => Session is { IsHost: false };

    /// <summary>Sync and transform ticks per second. RPCs flush every frame.</summary>
    public static float TickRate { get; set; } = 30;

    /// <summary>How far behind proxies display buffered transforms to smooth jitter.</summary>
    public static TimeSpan InterpolationDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    public static NetworkJoinState JoinState => System?.JoinState ?? NetworkJoinState.Disconnected;

    /// <summary>True after scene activation and catch-up. Repeats for each host scene change.</summary>
    public static bool IsReady => System?.IsReady == true;

    /// <summary>Remote readiness is known on the host; clients know only their own readiness.</summary>
    public static bool IsPeerReady(NetPeer peer) => System?.IsPeerReady(peer) == true;

    /// <summary>Hosts a session and shares the current scene with joining peers.</summary>
    public static NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        BeginSession();
        NetworkSystem.ValidateSceneMessageLimit(options);
        if (options is { SharePeerList: false })
        {
            throw new NotSupportedException(
                "Scene replication relays RPCs and changes from one client to the others, so clients need the peer list.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        PrepareTransport(transport);
        try
        {
            if (s_transport is not ThreadedTransport worker)
            {
                return AttachSession(
                    NetSession.Host(transport, name, options ?? s_configuration.CreateSessionOptions()));
            }

            worker.Listen();
            return AttachSession(NetSession.CreateHost(worker, name,
                options ?? s_configuration.CreateSessionOptions()));
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    /// <summary>Joins a host and loads its scene in place of the local scene.</summary>
    public static NetSession Join(Transport transport, string address, string name, ReadOnlySpan<byte> token = default,
        NetSessionOptions? options = null)
    {
        BeginSession();
        NetworkSystem.ValidateSceneMessageLimit(options);
        using FrameStatScope stats = FrameStats.Network.Scope();
        PrepareTransport(transport);
        try
        {
            return AttachSession(s_transport is not ThreadedTransport worker
                ? NetSession.Join(transport, address, name, token, options ?? s_configuration.CreateSessionOptions())
                : NetSession.CreateClient(worker.Connect(address), name, token,
                    options ?? s_configuration.CreateSessionOptions()));
        }
        catch
        {
            Shutdown();
            throw;
        }
    }
}
