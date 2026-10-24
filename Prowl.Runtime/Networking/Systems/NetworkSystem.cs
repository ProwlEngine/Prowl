// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Scene replication for one engine session. All processing runs on the game thread.</summary>
internal sealed partial class NetworkSystem : IDisposable
{
    internal readonly NetworkObjectTable Entries = new();
    internal readonly object Identity = new();
    internal readonly Dictionary<Guid, EngineObject> Index = new();
    internal readonly Dictionary<int, PeerReadiness> LoadingPeers = new();
    internal readonly RpcSystem Rpcs;
    internal readonly SyncSystem Sync;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    internal bool CatchUpApplied;
    internal int Frame;
    internal int IndexFrame = -1;
    internal double LastTransformTime;
    internal NetworkJoinState LocalJoinState;
    internal uint NextNetIdValue;
    internal double NextTick;
    internal double ReadyDeadline;
    internal Scene? ReplicatedScene;
    internal ulong SceneGeneration;
    internal Dictionary<Guid, EngineObject>? Spawning;
    internal uint Tick;
    private bool _detached;

    internal NetworkSystem(NetworkConfiguration configuration, NetSession? session = null)
    {
        Configuration = configuration.ValidatedCopy();
        Session = session;
        Sync = new SyncSystem(this);
        Rpcs = new RpcSystem(this);
    }

    internal NetSession? Session { get; private set; }
    internal bool IsActive => Session?.State == SessionState.Active;
    internal bool IsClient => Session is { IsHost: false };
    internal bool IsHost => Session?.IsHost == true;
    internal NetworkConfiguration Configuration { get; private set; }
    internal double Now => _clock.Elapsed.TotalSeconds;

    public void Dispose()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        Session = null;
        Scene.OnSceneLoaded -= OnHostSceneLoaded;
        Scene.OnSceneLoaded -= OnNetworkSceneActivated;
        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            entry.NotifyDespawn();
        }

        if (ReplicatedScene.IsValid())
        {
            Scene.CancelPendingLoad(ReplicatedScene!);
        }

        Entries.Clear();
        Index.Clear();
        Spawning = null;
        ReplicatedScene = null;
        LoadingPeers.Clear();
        Sync.Dirty.Clear();
        Sync.Dispose();
        Rpcs.Dispose();
    }

    internal void Configure(NetworkConfiguration configuration) => Configuration = configuration.ValidatedCopy();

    internal void Attach(NetSession session)
    {
        NextTick = Now;
        LocalJoinState = session.IsHost ? NetworkJoinState.Loading : NetworkJoinState.Connecting;
        ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;
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
        RegisterEngineMessage(session, EngineMessage.Sync, Sync.OnSync);
        RegisterEngineMessage(session, EngineMessage.Transform, OnTransform);
        RegisterEngineMessage(session, EngineMessage.RpcToHost, Rpcs.OnRpcToHost);
        RegisterEngineMessage(session, EngineMessage.RpcFromHost, Rpcs.OnRpcFromHost);

        RegisterEngineMessage(session, EngineMessage.SceneLoaded, OnClientSceneLoaded);
        RegisterEngineMessage(session, EngineMessage.SceneCatchUp, OnSceneCatchUp);
        RegisterEngineMessage(session, EngineMessage.SceneReady, OnClientSceneReady);
        RegisterEngineMessage(session, EngineMessage.SceneActivate, OnSceneActivate);

        RegisterEngineMessage(session, EngineMessage.StaticRpcToHost, Rpcs.OnStaticRpcToHost);
        RegisterEngineMessage(session, EngineMessage.StaticRpcFromHost, Rpcs.OnStaticRpcFromHost);
        RegisterEngineMessage(session, EngineMessage.Orphaned, OnOrphaned);
        RegisterEngineMessage(session, EngineMessage.OwnershipRequest, OnOwnershipRequest);
        RegisterEngineMessage(session, EngineMessage.OwnershipState, OnOwnershipState);
        RegisterEngineMessage(session, EngineMessage.OwnerTransfer, OnOwnerTransfer);
        RegisterEngineMessage(session, EngineMessage.NetworkFlags, OnNetworkFlags);
        if (session.IsHost)
        {
            ReplicatedScene = Scene.Current;
            SceneGeneration = 1;
            session.PeerJoined += OnPeerConnected;
            session.PeerLeft += OnPeerLeft;
            Scene.OnSceneLoaded += OnHostSceneLoaded;
            RegisterSceneObjects(ReplicatedScene);
            if (Session == session)
            {
                DispatchNetworkListener(e => e.OnNetworkLoading());
            }

            if (Session == session)
            {
                SetLocalReady();
            }
        }
    }

    internal void Receive()
    {
        Frame++;
        UpdateReadiness();
        if (IsActive)
        {
            ApplyInterpolation();
        }
    }

    internal void Flush()
    {
        if (IsActive && (IsHost || IsReady) && Now >= NextTick)
        {
            double interval = 1.0 / Math.Max(Networking.TickRate, 1f);
            NextTick = Math.Max(NextTick + interval, Now);
            Sync.SendSyncChanges();
            SendTransforms();
        }
    }
}
