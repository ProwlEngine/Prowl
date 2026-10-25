// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkSystem
{
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

    internal void OnSessionAdmitted()
    {
        LocalJoinState = NetworkJoinState.WaitingForScene;
        ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;
    }

    internal void OnPeerConnected(NetPeer peer)
    {
        NetSession? session = Session;
        DispatchNetworkListener(e =>
        {
            if (session!.TryGetPeer(peer.Id, out NetPeer? current) && current == peer)
            {
                e.OnPeerConnected(peer);
            }
        });
        if (Session == session && session!.TryGetPeer(peer.Id, out NetPeer? current) && current == peer)
        {
            SendSnapshot(peer);
        }
    }

    internal void BeginPeerLoad(NetPeer peer)
    {
        LoadingPeers[peer.Id] = new PeerReadiness(SceneGeneration, Now + Configuration.SceneLoadTimeoutSeconds);
        Snapshots.Delivered(peer);
    }

    internal void OnNetworkSceneActivated()
    {
        // A joining client can still activate local scenes before the host supplies its first snapshot.
        // Only guard scene replacement once there is a network scene to protect (including a queued one).
        if (!IsClient || ReplicatedScene is null)
        {
            return;
        }

        if (Scene.Current != ReplicatedScene)
        {
            Networking.Shutdown("The network scene was replaced locally during the session.");
            return;
        }

        if (JoinState != NetworkJoinState.Loading)
        {
            return;
        }

        LocalJoinState = NetworkJoinState.Synchronizing;
        SendSceneGeneration(Session!.HostPeer!, EngineMessage.SceneLoaded);
    }

    // Snapshot -> scene activation -> full catch-up -> acknowledgement -> gameplay activation.
    // Reliable delivery puts lifecycle/Sync traffic ahead of each following handshake message.
    internal void OnClientSceneLoaded(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (!TryGetLoadingPeer(peer, generation, NetworkJoinState.Loading, out PeerReadiness? state))
        {
            return;
        }

        using NetWriter message = new();
        Snapshots.Delivered(peer);
        new SceneCatchUpMessage(generation).Write(message);
        Sync.WriteSyncState(message, ReplicatedScene!.AllObjects);
        WriteCatchUpTransforms(message);
        state!.Stage = NetworkJoinState.Synchronizing;
        SendEngine(peer, EngineMessage.SceneCatchUp, message.WrittenSpan);
    }

    internal void OnSceneCatchUp(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Scene catch-up must be reliable.");
        }

        ulong generation = SceneCatchUpMessage.Read(ref reader).Generation;
        if (!IsClient || !peer.IsHost || generation != SceneGeneration ||
            JoinState != NetworkJoinState.Synchronizing || Scene.Current != ReplicatedScene || CatchUpApplied)
        {
            return;
        }

        NetSession? session = Session;
        Sync.ApplySyncState(peer, ref reader, null, true);
        if (Session != session)
        {
            return;
        }

        ApplyCatchUpTransforms(ref reader);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Scene catch-up contains trailing bytes.");
        }

        CatchUpApplied = true;
        SendSceneGeneration(peer, EngineMessage.SceneReady);
    }

    internal void OnClientSceneReady(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (!TryGetLoadingPeer(peer, generation, NetworkJoinState.Synchronizing, out PeerReadiness? state))
        {
            return;
        }

        // Queue activation first, so RPCs from ready callbacks arrive after the client's local ready notification.
        state!.Stage = NetworkJoinState.Ready;
        SendSceneGeneration(peer, EngineMessage.SceneActivate);
        DispatchNetworkListener(e =>
        {
            if (IsPeerReady(peer))
            {
                e.OnPeerReady(peer);
            }
        });
    }

    internal void OnSceneActivate(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (IsClient && peer.IsHost && generation == SceneGeneration && CatchUpApplied &&
            LocalJoinState == NetworkJoinState.Synchronizing && Scene.Current == ReplicatedScene)
        {
            SetLocalReady();
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
        Scene? scene = ReplicatedScene;
        if (scene.IsNotValid())
        {
            return;
        }

        scene!.RunEvent<INetworkListener>(listener =>
        {
            // A previous listener can shut down or replace the scene during the same dispatch.
            if (Session == session && ReplicatedScene == scene)
            {
                action(listener);
            }
        }, true);
    }

    internal bool CanReceiveEngineMessage(NetPeer peer, EngineMessage kind)
    {
        if (IsHost && !peer.IsLocal && kind is EngineMessage.Sync or EngineMessage.Snapshot or EngineMessage.SnapshotAck
                or EngineMessage.RpcToHost or EngineMessage.StaticRpcToHost)
        {
            return IsPeerReady(peer);
        }

        if (IsClient && kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost)
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
        foreach ((int id, PeerReadiness state) in LoadingPeers.ToArray())
        {
            if (Session != session)
            {
                break;
            }

            if (state.Stage != NetworkJoinState.Ready && Now >= state.Deadline &&
                session.TryGetPeer(id, out NetPeer? peer))
            {
                session.Kick(peer!, $"Scene readiness timed out during {state.Stage}.");
            }
        }
    }

    internal void WriteCatchUpTransforms(NetWriter message)
    {
        NetworkObject[] entries = Entries.Values
            .Where(e => e.GameObject.IsValid() && e.GameObject.Network.HasTransformSync).ToArray();
        new CatchUpTransformBlock((uint)entries.Length).Write(message);
        foreach (NetworkObject entry in entries)
        {
            Transform transform = entry.GameObject.Transform;
            new CatchUpTransformMessage(entry.NetId, entry.Generation,
                entry.TransformSync.HasReceived ? entry.TransformSync.LastReceivedTick : Tick,
                transform.LocalPosition, transform.LocalRotation, transform.LocalScale).Write(message);
        }
    }

    internal void ApplyCatchUpTransforms(ref NetReader reader)
    {
        uint count = CatchUpTransformBlock.Read(ref reader).Count;
        for (uint i = 0; i < count; i++)
        {
            CatchUpTransformMessage message = CatchUpTransformMessage.Read(ref reader);
            uint netId = message.NetId, generation = message.Generation, tick = message.Tick;
            Float3 position = message.Position, scale = message.Scale;
            Quaternion rotation = message.Rotation;
            float length = Quaternion.LengthSquared(rotation);
            if (!Finite(position) || !Finite(scale) || !float.IsFinite(length) || length < 1e-12f)
            {
                throw new InvalidDataException("Scene catch-up contains an invalid transform.");
            }

            if (Entries.TryGetNet(netId, out NetworkObject? entry) && entry.Generation == generation)
            {
                entry.TransformSync.Snap(entry.GameObject, tick, position, Quaternion.Normalize(rotation), scale);
            }
        }
    }

    internal sealed class PeerReadiness(ulong generation, double deadline)
    {
        public readonly double Deadline = deadline;
        public readonly ulong Generation = generation;
        public NetworkJoinState Stage = NetworkJoinState.Loading;
    }
}
