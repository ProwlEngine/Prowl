// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     One engine session's networked objects, their state, transforms and RPCs, and which scenes its peers load.
///     All processing runs on the game thread.
/// </summary>
internal sealed partial class NetworkSystem : IDisposable
{
    internal readonly NetworkObjectTable Entries = new();
    internal readonly object Identity = new();
    internal readonly List<(NetPeer Peer, EngineMessageHandler Handler, byte[] Payload, Delivery Delivery)> EarlyRpcs = new();
    internal readonly Dictionary<int, PeerReadiness> LoadingPeers = new();
    private readonly List<NetPeer> _overLimitPeers = new();
    internal readonly RpcSystem Rpcs;
    internal readonly TransformSystem Transforms;
    internal readonly SyncSystem Sync;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    internal int Frame;
    internal double LastTransformTime;
    internal NetworkJoinState LocalJoinState;
    internal uint NextNetIdValue;
    internal double ReadyDeadline;
    internal SceneInstance? ReplicatedWorld;
    internal ulong SceneGeneration;
    internal Dictionary<Guid, EngineObject>? Spawning;
    internal uint Tick;
    private bool _detached;

    internal NetworkSystem(NetworkConfiguration configuration, NetSession? session = null)
    {
        Configuration = configuration.ValidatedCopy();
        Session = session;
        Sync = new SyncSystem(this);
        Transforms = new TransformSystem(this);
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
        SceneInstance.Loaded -= OnWorldLoaded;
        SceneInstance.Unloading -= OnWorldUnloading;
        SceneInstance.MainChanged -= OnMainChanged;
        SceneInstance.RootMoved -= OnRootMoved;
        if (SceneInstance.RefuseAdditive == RefuseDuplicateObjects)
        {
            SceneInstance.RefuseAdditive = null;
        }

        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            entry.NotifyDespawn(NetworkDespawnReason.SessionEnded);
        }

        Entries.Clear();
        ForgetVisibility();
        Spawning = null;
        ReplicatedWorld = null;
        ForgetSections();
        LoadingPeers.Clear();
        _overLimitPeers.Clear();
        EarlyRpcs.Clear();
        Sync.Clear();
        Sync.Dispose();
        Transforms.Clear();
        Transforms.Dispose();
        Rpcs.Dispose();
    }

    internal void Configure(NetworkConfiguration configuration) => Configuration = configuration.ValidatedCopy();

    internal void Attach(NetSession session)
    {
        _wallStepAt = Now;
        LocalJoinState = session.IsHost ? NetworkJoinState.Loading : NetworkJoinState.Connecting;
        ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;
        SceneInstance.Loaded += OnWorldLoaded;
        SceneInstance.Unloading += OnWorldUnloading;
        SceneInstance.MainChanged += OnMainChanged;
        SceneInstance.RootMoved += OnRootMoved;
        SceneInstance.RefuseAdditive = RefuseDuplicateObjects;
        session.Joined += OnSessionAdmitted;
        NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty,
            session.IsHost ? "Hosting" : "Joining");
        session.Joined += () => NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty, "Joined");
        session.PeerJoined += peer => NetworkDiagnostics.Record("Session", "Local", peer, Guid.Empty, "Peer joined");
        session.PeerLeft += (peer, reason) =>
            NetworkDiagnostics.Record("Session", "Local", peer, Guid.Empty, $"Peer left: {reason}");

        RegisterEngineMessage(session, EngineMessage.Scene, OnScene);
        RegisterEngineMessage(session, EngineMessage.Spawn, OnSpawn);
        RegisterEngineMessage(session, EngineMessage.Despawn, OnDespawn);
        RegisterEngineMessage(session, EngineMessage.Owner, OnOwner);
        RegisterEngineMessage(session, EngineMessage.Sync, Sync.OnSync);
        RegisterEngineMessage(session, EngineMessage.Transform, Transforms.OnTransforms);
        RegisterEngineMessage(session, EngineMessage.TransformAck, Transforms.OnAck);
        RegisterEngineMessage(session, EngineMessage.RpcToHost, Rpcs.OnRpcToHost);
        RegisterEngineMessage(session, EngineMessage.RpcFromHost, Rpcs.OnRpcFromHost);

        RegisterEngineMessage(session, EngineMessage.Ready, OnClientReady);
        RegisterEngineMessage(session, EngineMessage.SpawnStarted, OnSpawnStarted);
        RegisterEngineMessage(session, EngineMessage.SpawnFinished, OnSpawnFinished);
        RegisterEngineMessage(session, EngineMessage.SectionAdd, OnSectionAdd);
        RegisterEngineMessage(session, EngineMessage.SectionRemove, OnSectionRemove);
        RegisterEngineMessage(session, EngineMessage.SectionMove, OnSectionMove);
        RegisterEngineMessage(session, EngineMessage.Time, OnTime);

        RegisterEngineMessage(session, EngineMessage.StaticRpcToHost, Rpcs.OnStaticRpcToHost);
        RegisterEngineMessage(session, EngineMessage.StaticRpcFromHost, Rpcs.OnStaticRpcFromHost);
        RegisterEngineMessage(session, EngineMessage.Orphaned, OnOrphaned);
        RegisterEngineMessage(session, EngineMessage.OwnershipState, OnOwnershipState);
        RegisterEngineMessage(session, EngineMessage.Player, OnPlayer);
        if (session.IsHost)
        {
            SceneInstance world = SceneInstance.Main;
            ReplicatedWorld = world;
            foreach (SceneSection section in world.Sections)
            {
                SectionId(section);
            }

            SceneGeneration = 1;
            session.PeerJoined += OnPeerConnected;
            session.PeerLeft += OnPeerLeft;
            RegisterSceneObjects(world, world.Objects);
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

        KickOverLimitPeers();
    }

    internal void Flush()
    {
        SendSectionRemovals();
        StepWallClock();
        if (IsActive && (IsHost || IsReady) && _tickDue)
        {
            _tickDue = false;
            UpdateInterest();
            if (IsActive)
            {
                Sync.SendSyncChanges();
                Transforms.Send();
            }
        }

        SendTime();

        // Acknowledged every frame, not every tick, so the sender learns as soon as it can.
        if (IsActive)
        {
            Transforms.SendAcks();
        }

        KickOverLimitPeers();
    }
}
