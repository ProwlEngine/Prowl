// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkSystem
{
    private readonly List<(int Peer, NetworkJoinState Stage)> _timedOut = new();

    /// <summary>Current connection, admission, scene loading, or gameplay readiness stage.</summary>
    internal NetworkJoinState JoinState
    {
        get
        {
            if (Session is not { } session)
            {
                return NetworkJoinState.Disconnected;
            }

            if (session.State != SessionState.Joining)
            {
                return LocalJoinState;
            }

            return session.HostConnection is { IsConnected: true }
                ? NetworkJoinState.Joining
                : NetworkJoinState.Connecting;
        }
    }

    /// <summary>True after scene activation and the readiness handshake. IsActive only means session admission.</summary>
    internal bool IsReady => IsActive && LocalJoinState == NetworkJoinState.Ready;

    /// <summary>Readiness of this peer. Remote readiness is known only on the host; other clients return false.</summary>
    internal bool IsPeerReady(NetPeer peer) => Session is { } session &&
                                               session.TryGetPeer(peer.Id, out NetPeer? current) &&
                                               ReferenceEquals(current, peer) &&
                                               (peer.IsLocal
                                                   ? IsReady
                                                   : session.IsHost &&
                                                     LoadingPeers.TryGetValue(peer.Id,
                                                         out PeerReadiness? state) &&
                                                     state.Generation == SceneGeneration &&
                                                     state.Stage == NetworkJoinState.Ready);

    /// <summary>
    ///     Host: whether <paramref name="peer" /> has loaded the current scene, so calls and new spawns go to it: one
    ///     that is ready, or one whose spawns are still on their way, which holds calls until the last has arrived.
    ///     One still loading the scene gets neither.
    /// </summary>
    internal bool HasLoadedScene(NetPeer peer) => peer.IsLocal
        ? IsReady
        : Session is { IsHost: true } session && session.TryGetPeer(peer.Id, out NetPeer? current) &&
          ReferenceEquals(current, peer) && LoadingPeers.TryGetValue(peer.Id, out PeerReadiness? state) &&
          state.Generation == SceneGeneration &&
          state.Stage is NetworkJoinState.Ready or NetworkJoinState.Synchronizing;

    internal void OnSessionAdmitted()
    {
        LocalJoinState = NetworkJoinState.WaitingForScene;
        ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;
    }

    internal void OnPeerConnected(NetPeer peer)
    {
        if (!FitsPeer(peer, EngineMessage.Transform, TransformBatchMessage.MinMessageSize - sizeof(ushort)))
        {
            return;
        }

        NetSession? session = Session;
        ResetVisibility(peer);
        DispatchNetworkListener(e =>
        {
            if (session!.TryGetPeer(peer.Id, out NetPeer? current) && current == peer)
            {
                e.OnPeerConnected(peer);
            }
        });
        if (Session == session && session!.TryGetPeer(peer.Id, out NetPeer? current) && current == peer)
        {
            SendScene(peer);
        }
    }

    internal void BeginPeerLoad(NetPeer peer)
    {
        LoadingPeers[peer.Id] = new PeerReadiness(SceneGeneration, Now + Configuration.SceneLoadTimeoutSeconds);
        Transforms.Restart(peer);
        ResetVisibility(peer);
    }

    /// <summary>The host's scenes are loaded on this client, so it tells the host it is ready for their objects.</summary>
    internal void OnNetworkSceneActivated()
    {
        if (!IsClient || JoinState != NetworkJoinState.Loading)
        {
            return;
        }

        LocalJoinState = NetworkJoinState.Synchronizing;
        SendSceneGeneration(Session!.HostPeer!, EngineMessage.Ready);
    }

    internal void RunEarlyRpcs()
    {
        NetSession? session = Session;
        (NetPeer Peer, EngineMessageHandler Handler, byte[] Payload, Delivery Delivery)[] calls = EarlyRpcs.ToArray();
        EarlyRpcs.Clear();
        foreach ((NetPeer peer, EngineMessageHandler handler, byte[] payload, Delivery delivery) in calls)
        {
            if (Session != session || !IsReady)
            {
                return;
            }

            // Each call stands alone, as it would have arriving by itself: one that fails here does not cost the rest.
            try
            {
                NetReader reader = new(payload);
                handler(peer, ref reader, delivery);
            }
            catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException or OverflowException
                                          or NotSupportedException or FormatException or ArgumentException))
            {
                NetworkDiagnostics.Record("Rpc", "Failed", peer, Guid.Empty, e.Message);
                Debug.LogError($"[Network] Handling a call held until the spawns finished failed: {e}");
            }
        }
    }

    internal bool TryGetLoadingPeer(NetPeer peer, ulong generation, NetworkJoinState stage,
        out PeerReadiness? state)
    {
        state = null;
        return IsHost && generation == SceneGeneration &&
               LoadingPeers.TryGetValue(peer.Id, out state) && state.Generation == generation &&
               state.Stage == stage;
    }

    internal ulong ReadSceneGeneration(ref NetReader reader, Delivery delivery)
    {
        ulong generation = SceneGenerationMessage.Read(ref reader).Generation;
        if (delivery != Delivery.Reliable || generation == 0 || !reader.IsAtEnd)
        {
            throw new InvalidDataException("Invalid scene readiness acknowledgement.");
        }

        return generation;
    }

    internal void SendSceneGeneration(NetPeer peer, EngineMessage kind)
    {
        using NetWriter message = new();
        new SceneGenerationMessage(SceneGeneration).Write(message);
        SendEngine(peer, kind, message.WrittenSpan);
    }

    internal void SetLocalReady()
    {
        if (!IsActive || LocalJoinState == NetworkJoinState.Ready)
        {
            return;
        }

        LocalJoinState = NetworkJoinState.Ready;
        DispatchNetworkListener(e => e.OnNetworkReady());
    }

    internal void DispatchNetworkListener(Action<INetworkListener> action)
    {
        NetSession? session = Session;
        Scene? world = ReplicatedWorld;
        if (world.IsNotValid())
        {
            return;
        }

        world!.RunEvent<INetworkListener>(listener =>
        {
            // A previous listener can shut down or replace the world during the same dispatch.
            if (Session == session && ReplicatedWorld == world)
            {
                action(listener);
            }
        }, true);
    }

    /// <summary>Runs <paramref name="action" /> on the listeners of one section, for a section loaded during the session.</summary>
    internal void DispatchSectionListener(SceneSection section, Action<INetworkListener> action)
    {
        NetSession? session = Session;
        foreach (GameObject root in section.Roots.ToArray())
        {
            root.RunEvent<INetworkListener>(listener =>
            {
                if (Session == session && ReferenceEquals(root.Section, section))
                {
                    action(listener);
                }
            }, EventScope.SelfAndDescendants, true);
        }
    }

    internal bool CanReceiveEngineMessage(NetPeer peer, EngineMessage kind)
    {
        if (IsHost && !peer.IsLocal && kind is EngineMessage.Sync or EngineMessage.Transform or EngineMessage.TransformAck
                or EngineMessage.RpcToHost)
        {
            return IsPeerReady(peer);
        }

        if (IsClient && kind is EngineMessage.RpcFromHost)
        {
            return IsReady;
        }

        return true;
    }

    internal void UpdateReadiness()
    {
        if (!IsActive)
        {
            return;
        }

        if (IsClient)
        {
            if (!IsReady && Now >= ReadyDeadline)
            {
                Networking.Shutdown($"Scene readiness timed out during {JoinState}.");
            }

            return;
        }

        NetSession session = Session!;
        double now = Now;
        _timedOut.Clear();
        foreach ((int id, PeerReadiness state) in LoadingPeers)
        {
            if (state.Stage != NetworkJoinState.Ready && now >= state.Deadline)
            {
                _timedOut.Add((id, state.Stage));
            }
        }

        foreach ((int id, NetworkJoinState stage) in _timedOut)
        {
            if (Session != session)
            {
                break;
            }

            if (session.TryGetPeer(id, out NetPeer? peer))
            {
                session.Kick(peer!, $"Scene readiness timed out during {stage}.");
            }
        }
    }

    internal sealed class PeerReadiness(ulong generation, double deadline)
    {
        public double Deadline = deadline;
        public readonly ulong Generation = generation;
        public NetworkJoinState Stage = NetworkJoinState.Loading;
    }
}
