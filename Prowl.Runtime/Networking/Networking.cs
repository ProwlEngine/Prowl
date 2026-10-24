// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Linq;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Multiplayer, built on <see cref="NetSession" /> from Prowl.Warp. Host or join a session here and the
///     engine pumps it every frame, around the fixed update. With no session running, none of this does anything.
/// </summary>
/// <remarks>
///     <para>
///         The host shares its scene: a client that joins, and every client whenever the host changes scene,
///         gets the host's current scene and loads it in place of its own. Objects that are
///         <see cref="NetworkMode.Never" /> stay behind. Scene-placed objects keep their
///         <see cref="GameObject.Identifier" />s, so host and clients agree on which object is which.
///     </para>
///     <para>
///         <see cref="NetworkMode.Object" /> objects are networked objects: the host spawns them with
///         <see cref="NetworkAccessor.Spawn" />, each has an owner (or the host), and their transform and
///         <see cref="SyncAttribute" /> properties follow whoever controls them. <see cref="Rpc" /> methods call across
///         peers.
///     </para>
///     <para>
///         Game messages can also go through <see cref="Session" /> directly (<see cref="NetSession.On{T}" />,
///         <see cref="NetSession.Send{T}" />), with ids below <see cref="EngineMessageIds" />.
///         In the editor, leaving play mode ends the session.
///     </para>
/// </remarks>
public static partial class Networking
{
    /// <summary>
    ///     Message ids from here up to <see cref="NetSession.ReservedMessageIds" /> are the engine's own.
    ///     Game messages use ids below it.
    /// </summary>
    public const ushort EngineMessageIds = 0xFE00;

    // The scene this session replicates: the current one on the host, the last one received on a client
    // (which only becomes current at the end of the frame it arrived in).
    private static Scene? s_scene;

    private static readonly Stopwatch s_clock = Stopwatch.StartNew();
    private static double s_nextTick;
    private static int s_frame;
    private static NetworkingContext? s_context;
    private static bool s_endingSession;

    /// <summary>The running session, or null when there is none.</summary>
    public static NetSession? Session { get; private set; }

    /// <summary>The session's bound IP port, or zero when it has no IP listener.</summary>
    public static int LocalPort => s_context?.LocalPort ?? Session?.Transport.LocalPort ?? 0;

    /// <summary>True while hosting, or joined to a host.</summary>
    public static bool IsActive => Session?.State == SessionState.Active;

    public static bool IsHost => Session?.IsHost == true;

    public static bool IsClient => Session is { IsHost: false };

    /// <summary>How many times a second [Sync] changes and networked transforms are sent. RPCs go out every frame.</summary>
    public static float TickRate { get; set; } = 30;

    /// <summary>How far behind proxies show networked transforms, so they move smoothly between updates despite jitter.</summary>
    public static TimeSpan InterpolationDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    private static double Now => s_clock.Elapsed.TotalSeconds;

    /// <summary>
    ///     Hosts a session on <paramref name="transport" />, sharing the current scene with everyone who joins.
    /// </summary>
    /// <exception cref="InvalidOperationException">A session is already running.</exception>
    public static NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        BeginSession();
        ValidateSceneMessageLimit(options);
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetworkingContext context = new(s_configuration);
        NetSession session;
        try
        {
            session = context.Host(transport, name, options);
        }
        catch
        {
            context.Dispose();
            throw;
        }

        Attach(session, context);

        s_scene = Scene.Current;
        s_sceneGeneration = 1;
        session.PeerJoined += OnPeerConnected;
        session.PeerLeft += OnPeerLeft;
        Scene.OnSceneLoaded += OnHostSceneLoaded;
        RegisterSceneObjects(s_scene);
        if (Session == session)
        {
            DispatchNetworkListener(e => e.OnNetworkLoading());
        }

        if (Session == session)
        {
            SetLocalReady();
        }

        return session;
    }

    /// <summary>
    ///     Joins a host. Once let in, the client receives the host's scene and loads it, replacing the
    ///     current one at the end of that frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">A session is already running.</exception>
    public static NetSession Join(Transport transport, string address, string name, ReadOnlySpan<byte> token = default,
        NetSessionOptions? options = null)
    {
        BeginSession();
        ValidateSceneMessageLimit(options);
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetworkingContext context = new(s_configuration);
        NetSession session;
        try
        {
            session = context.Join(transport, address, name, token, options);
        }
        catch
        {
            context.Dispose();
            throw;
        }

        Attach(session, context);
        return session;
    }

    /// <summary>Leaves the session, or ends it for everyone when hosting. Does nothing without one.</summary>
    public static void Shutdown(string? message = null)
    {
        if (Session is not { } session)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        s_context!.Shutdown(message);
    }

    // --- Called by the game loop -------------------------------------------------------------

    /// <summary>Before the fixed update: runs whatever arrived, so this frame simulates on it.</summary>
    internal static void Receive()
    {
        NetSession? session = Session;
        if (session == null)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        s_frame++;
        s_context!.Receive();
        if (Session == session)
        {
            UpdateReadiness();
        }

        if (IsActive)
        {
            ApplyInterpolation();
        }
    }

    /// <summary>After the frame's update: sends what this frame produced.</summary>
    internal static void Flush()
    {
        NetSession? session = Session;
        if (session == null)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        if (IsActive && (IsHost || IsReady) && Now >= s_nextTick)
        {
            // Fall behind by more than a tick, after a hitch, and the schedule restarts rather than bursting to catch up.
            double interval = 1.0 / Math.Max(TickRate, 1f);
            s_nextTick = Math.Max(s_nextTick + interval, Now);

            SendSyncChanges();
            SendTransforms();
        }

        if (Session == session)
        {
            s_context!.Flush();
        }
    }

    // --- Session bookkeeping -------------------------------------------------------------------

    private static void BeginSession()
    {
        MainThreadContext.AssertMainThread();
        if (s_endingSession)
        {
            throw new InvalidOperationException(
                "A session cannot start during network teardown. Schedule startup for the next frame.");
        }

        if (Session != null)
        {
            throw new InvalidOperationException(
                "A network session is already running. Call Networking.Shutdown first.");
        }
    }

    private static void Attach(NetSession session, NetworkingContext context)
    {
        Session = session;
        s_context = context;
        s_nextTick = Now;
        s_joinState = session.IsHost ? NetworkJoinState.Loading : NetworkJoinState.Connecting;
        s_readyDeadline = Now + s_configuration.SceneLoadTimeoutSeconds;
        Scene.OnSceneLoaded += OnNetworkSceneActivated;
        session.Joined += OnSessionAdmitted;
        NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty,
            session.IsHost ? "Hosting" : "Joining");
        session.Joined += () => NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty, "Joined");
        session.PeerJoined += peer => NetworkDiagnostics.Record("Session", "Local", peer, Guid.Empty, "Peer joined");
        session.PeerLeft += (peer, reason) =>
            NetworkDiagnostics.Record("Session", "Local", peer, Guid.Empty, $"Peer left: {reason}");

        RegisterEngineMessage(session, EngineMessage.SceneSnapshot, OnSceneSnapshot);
        RegisterEngineMessage(session, EngineMessage.Spawn, OnSpawn);
        RegisterEngineMessage(session, EngineMessage.Despawn, OnDespawn);
        RegisterEngineMessage(session, EngineMessage.Owner, OnOwner);
        RegisterEngineMessage(session, EngineMessage.Sync, OnSync);
        RegisterEngineMessage(session, EngineMessage.Transform, OnTransform);
        RegisterEngineMessage(session, EngineMessage.RpcToHost, OnRpcToHost);
        RegisterEngineMessage(session, EngineMessage.RpcFromHost, OnRpcFromHost);

        RegisterEngineMessage(session, EngineMessage.SceneLoaded, OnClientSceneLoaded);
        RegisterEngineMessage(session, EngineMessage.SceneCatchUp, OnSceneCatchUp);
        RegisterEngineMessage(session, EngineMessage.SceneReady, OnClientSceneReady);
        RegisterEngineMessage(session, EngineMessage.SceneActivate, OnSceneActivate);

        RegisterEngineMessage(session, EngineMessage.StaticRpcToHost, OnStaticRpcToHost);
        RegisterEngineMessage(session, EngineMessage.StaticRpcFromHost, OnStaticRpcFromHost);
        RegisterEngineMessage(session, EngineMessage.Orphaned, OnOrphaned);
        RegisterEngineMessage(session, EngineMessage.OwnershipRequest, OnOwnershipRequest);
        RegisterEngineMessage(session, EngineMessage.OwnershipState, OnOwnershipState);
        RegisterEngineMessage(session, EngineMessage.OwnerTransfer, OnOwnerTransfer);
        RegisterEngineMessage(session, EngineMessage.NetworkFlags, OnNetworkFlags);

        session.Ended += (reason, message) =>
        {
            if (Session != session)
            {
                return;
            }

            s_endingSession = true;
            try
            {
                NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty,
                    $"Ended: {reason}: {message}");
                Session = null;
                s_context = null;
                Scene.OnSceneLoaded -= OnHostSceneLoaded;
                Scene.OnSceneLoaded -= OnNetworkSceneActivated;
                try
                {
                    context.Dispose();
                }
                finally
                {
                    ResetState();
                }

                Debug.Log(message == null
                    ? $"[Network] Session ended: {reason}."
                    : $"[Network] Session ended: {reason}: {message}");
            }
            finally
            {
                s_endingSession = false;
            }
        };
    }

    private static void ResetState()
    {
        NetworkEntry[] entries = s_entries.Values.ToArray();
        foreach (NetworkEntry entry in entries)
        {
            NotifyDespawn(entry);
        }

        ResetReadiness();
        s_scene = null;
        s_entries.Clear();
        s_index.Clear();
        s_dirty.Clear();
        SessionNumber++;
        s_incoming = null;
        s_incomingStatic = null;
        s_incomingRead = false;
        CurrentCaller = null;

        foreach (NetworkEntry entry in entries)
        {
            ControlChanged(entry.GameObject);
        }
    }

    private enum EngineMessage : ushort
    {
        SceneSnapshot = EngineMessageIds,
        Spawn,
        Despawn,
        Owner,
        Sync,
        Transform,
        RpcToHost,
        RpcFromHost,
        SceneLoaded,
        SceneCatchUp,
        SceneReady,
        SceneActivate,
        StaticRpcToHost,
        StaticRpcFromHost,
        Orphaned,
        OwnershipRequest,
        OwnershipState,
        OwnerTransfer,
        NetworkFlags
    }
}
