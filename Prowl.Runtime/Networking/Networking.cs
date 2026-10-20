// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;

using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime;

/// <summary>
/// Multiplayer, built on <see cref="NetSession"/> from Prowl.Warp. Host or join a session here and the
/// engine pumps it every frame, around the fixed update. With no session running, none of this does anything.
/// </summary>
/// <remarks>
/// <para>
/// The host shares its scene: a client that joins, and every client whenever the host changes scene,
/// gets the host's current scene and loads it in place of its own. Objects that are
/// <see cref="NetworkMode.Never"/> stay behind. Scene-placed objects keep their
/// <see cref="GameObject.Identifier"/>s, so host and clients agree on which object is which.
/// </para>
/// <para>
/// <see cref="NetworkMode.Object"/> objects are networked objects: the host spawns them with
/// <see cref="GameObject.NetworkSpawn"/>, each has an owner (or the host), and their transform and
/// <see cref="SyncAttribute"/> properties follow whoever controls them. <see cref="Rpc"/> methods call across peers.
/// </para>
/// <para>
/// Game messages can also go through <see cref="Session"/> directly (<see cref="NetSession.On{T}"/>,
/// <see cref="NetSession.Send{T}"/>), with ids below <see cref="EngineMessageIds"/>.
/// In the editor, leaving play mode ends the session.
/// </para>
/// </remarks>
public static partial class Networking
{
    /// <summary>
    /// Message ids from here up to <see cref="NetSession.ReservedMessageIds"/> are the engine's own.
    /// Game messages use ids below it.
    /// </summary>
    public const ushort EngineMessageIds = 0xFE00;

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
    }

    /// <summary>The running session, or null when there is none.</summary>
    public static NetSession? Session { get; private set; }

    /// <summary>True while hosting, or joined to a host.</summary>
    public static bool IsActive => Session?.State == SessionState.Active;

    public static bool IsHost => Session?.IsHost == true;

    public static bool IsClient => Session is { IsHost: false };

    /// <summary>How many times a second [Sync] changes and networked transforms are sent. RPCs go out every frame.</summary>
    public static float TickRate { get; set; } = 30;

    /// <summary>How far behind proxies show networked transforms, so they move smoothly between updates despite jitter.</summary>
    public static TimeSpan InterpolationDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    // The scene this session replicates: the current one on the host, the last one received on a client
    // (which only becomes current at the end of the frame it arrived in).
    private static Scene? s_scene;

    private static readonly Stopwatch s_clock = Stopwatch.StartNew();
    private static double s_nextTick;
    private static int s_frame;

    private static double Now => s_clock.Elapsed.TotalSeconds;

    /// <summary>
    /// Hosts a session on <paramref name="transport"/>, sharing the current scene with everyone who joins.
    /// </summary>
    /// <exception cref="InvalidOperationException">A session is already running.</exception>
    public static NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        BeginSession();
        NetSession session = NetSession.Host(transport, name, options ?? s_configuration.CreateSessionOptions());
        Attach(session);

        s_scene = Scene.Current;
        RegisterSceneObjects(s_scene);

        session.PeerJoined += SendSnapshot;
        session.PeerLeft += OnPeerLeft;
        Scene.OnSceneLoaded += OnHostSceneLoaded;
        return session;
    }

    /// <summary>
    /// Joins a host. Once let in, the client receives the host's scene and loads it, replacing the
    /// current one at the end of that frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">A session is already running.</exception>
    public static NetSession Join(Transport transport, string address, string name, ReadOnlySpan<byte> token = default, NetSessionOptions? options = null)
    {
        BeginSession();
        NetSession session = NetSession.Join(transport, address, name, token, options ?? s_configuration.CreateSessionOptions());
        Attach(session);
        return session;
    }

    /// <summary>Leaves the session, or ends it for everyone when hosting. Does nothing without one.</summary>
    public static void Shutdown(string? message = null) => Session?.Shutdown(message);

    // --- Called by the game loop -------------------------------------------------------------

    /// <summary>Before the fixed update: runs whatever arrived, so this frame simulates on it.</summary>
    internal static void Receive()
    {
        NetSession? session = Session;
        if (session == null)
            return;

        s_frame++;
        session.Receive();

        if (IsActive)
            ApplyInterpolation();
    }

    /// <summary>After the frame's update: sends what this frame produced.</summary>
    internal static void Flush()
    {
        NetSession? session = Session;
        if (session == null)
            return;

        if (IsActive && Now >= s_nextTick)
        {
            // Fall behind by more than a tick, after a hitch, and the schedule restarts rather than bursting to catch up.
            double interval = 1.0 / Math.Max(TickRate, 1f);
            s_nextTick = Math.Max(s_nextTick + interval, Now);

            SendSyncChanges();
            SendTransforms();
        }

        session.Flush();
    }

    // --- Session bookkeeping -------------------------------------------------------------------

    private static void BeginSession()
    {
        MainThreadContext.AssertMainThread();
        if (Session != null)
            throw new InvalidOperationException("A network session is already running. Call Networking.Shutdown first.");
    }

    private static void Attach(NetSession session)
    {
        Session = session;
        s_nextTick = Now;
        NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty, session.IsHost ? "Hosting" : "Joining");
        session.Joined += () => NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty, "Joined");
        session.PeerJoined += peer => NetworkDiagnostics.Record("Session", "Local", peer, Guid.Empty, "Peer joined");
        session.PeerLeft += (peer, reason) => NetworkDiagnostics.Record("Session", "Local", peer, Guid.Empty, $"Peer left: {reason}");

        RegisterEngineMessage(session, EngineMessage.SceneSnapshot, OnSceneSnapshot);
        RegisterEngineMessage(session, EngineMessage.Spawn, OnSpawn);
        RegisterEngineMessage(session, EngineMessage.Despawn, OnDespawn);
        RegisterEngineMessage(session, EngineMessage.Owner, OnOwner);
        RegisterEngineMessage(session, EngineMessage.Sync, OnSync);
        RegisterEngineMessage(session, EngineMessage.Transform, OnTransform);
        RegisterEngineMessage(session, EngineMessage.RpcToHost, OnRpcToHost);
        RegisterEngineMessage(session, EngineMessage.RpcFromHost, OnRpcFromHost);

        session.Ended += (reason, message) =>
        {
            if (Session != session)
                return;

            NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty, $"Ended: {reason}: {message}");
            Session = null;
            Scene.OnSceneLoaded -= OnHostSceneLoaded;
            ResetState();
            Debug.Log(message == null ? $"[Network] Session ended: {reason}." : $"[Network] Session ended: {reason}: {message}");
        };
    }

    private static void ResetState()
    {
        s_scene = null;
        s_entries.Clear();
        s_index.Clear();
        s_dirty.Clear();
        s_incoming = null;
        s_incomingRead = false;
        CurrentCaller = null;
    }
}
