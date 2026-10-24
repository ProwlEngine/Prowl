// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{

    /// <summary>Current connection, admission, scene loading, or gameplay readiness stage.</summary>
    public static NetworkJoinState JoinState
    {
        get
        {
            if (Session is not { } session)
            {
                return NetworkJoinState.Disconnected;
            }

            if (session.State != SessionState.Joining)
            {
                return s_state.JoinState;
            }

            return session.HostConnection is { IsConnected: true }
                ? NetworkJoinState.Joining
                : NetworkJoinState.Connecting;
        }
    }

    /// <summary>True after scene activation and the readiness handshake. IsActive only means session admission.</summary>
    public static bool IsReady => IsActive && s_state.JoinState == NetworkJoinState.Ready;

    /// <summary>Readiness of this peer. Remote readiness is known only on the host; other clients return false.</summary>
    public static bool IsPeerReady(NetPeer peer) => Session is { } session &&
                                                    session.TryGetPeer(peer.Id, out NetPeer? current) &&
                                                    ReferenceEquals(current, peer) &&
                                                    (peer.IsLocal
                                                        ? IsReady
                                                        : session.IsHost &&
                                                          s_state.PeerReadiness.TryGetValue(peer.Id,
                                                              out PeerReadiness? state) &&
                                                          state.Generation == s_state.SceneGeneration &&
                                                          state.Stage == NetworkJoinState.Ready);

    private static void OnSessionAdmitted()
    {
        s_state.JoinState = NetworkJoinState.WaitingForScene;
        s_state.ReadyDeadline = Now + s_configuration.SceneLoadTimeoutSeconds;
    }

    private static void OnPeerConnected(NetPeer peer)
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

    private static void BeginPeerLoad(NetPeer peer) => s_state.PeerReadiness[peer.Id] =
        new PeerReadiness(s_state.SceneGeneration, Now + s_configuration.SceneLoadTimeoutSeconds);

    private static void OnNetworkSceneActivated()
    {
        // A joining client can still activate local scenes before the host supplies its first snapshot.
        // Only guard scene replacement once there is a network scene to protect (including a queued one).
        if (!IsClient || s_state.Scene is null)
        {
            return;
        }

        if (Scene.Current != s_state.Scene)
        {
            Shutdown("The network scene was replaced locally during the session.");
            return;
        }

        if (s_state.JoinState != NetworkJoinState.Loading)
        {
            return;
        }

        s_state.JoinState = NetworkJoinState.Synchronizing;
        SendSceneGeneration(Session!.HostPeer!, EngineMessage.SceneLoaded);
    }

    // Snapshot -> scene activation -> full catch-up -> acknowledgement -> gameplay activation.
    // Reliable delivery puts lifecycle/Sync traffic ahead of each following handshake message.
    private static void OnClientSceneLoaded(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (!TryGetLoadingPeer(peer, generation, NetworkJoinState.Loading, out PeerReadiness? state))
        {
            return;
        }

        using NetWriter message = new();
        message.WriteUInt64(generation);
        WriteSyncState(message, s_state.Scene!.AllObjects);
        WriteCatchUpTransforms(message);
        state!.Stage = NetworkJoinState.Synchronizing;
        SendEngine(peer, EngineMessage.SceneCatchUp, message.WrittenSpan);
    }

    private static void OnSceneCatchUp(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Scene catch-up must be reliable.");
        }

        ulong generation = reader.ReadUInt64();
        if (!IsClient || !peer.IsHost || generation != s_state.SceneGeneration ||
            s_state.JoinState != NetworkJoinState.Synchronizing || Scene.Current != s_state.Scene || s_state.CatchUpApplied)
        {
            return;
        }

        NetSession? session = Session;
        ApplySyncState(peer, ref reader, null, true);
        if (Session != session)
        {
            return;
        }

        ApplyCatchUpTransforms(ref reader);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Scene catch-up contains trailing bytes.");
        }

        s_state.CatchUpApplied = true;
        SendSceneGeneration(peer, EngineMessage.SceneReady);
    }

    private static void OnClientSceneReady(NetPeer peer, ref NetReader reader, Delivery delivery)
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

    private static void OnSceneActivate(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (IsClient && peer.IsHost && generation == s_state.SceneGeneration && s_state.CatchUpApplied &&
            s_state.JoinState == NetworkJoinState.Synchronizing && Scene.Current == s_state.Scene)
        {
            SetLocalReady();
        }
    }

    private static bool TryGetLoadingPeer(NetPeer peer, ulong generation, NetworkJoinState stage,
        out PeerReadiness? state)
    {
        state = null;
        return IsHost && generation == s_state.SceneGeneration &&
               s_state.PeerReadiness.TryGetValue(peer.Id, out state) && state.Generation == generation &&
               state.Stage == stage;
    }

    private static ulong ReadSceneGeneration(ref NetReader reader, Delivery delivery)
    {
        ulong generation = reader.ReadUInt64();
        if (delivery != Delivery.Reliable || generation == 0 || !reader.IsAtEnd)
        {
            throw new InvalidDataException("Invalid scene readiness acknowledgement.");
        }

        return generation;
    }

    private static void SendSceneGeneration(NetPeer peer, EngineMessage kind)
    {
        using NetWriter message = new();
        message.WriteUInt64(s_state.SceneGeneration);
        SendEngine(peer, kind, message.WrittenSpan);
    }

    private static void SetLocalReady()
    {
        if (!IsActive || s_state.JoinState == NetworkJoinState.Ready)
        {
            return;
        }

        s_state.JoinState = NetworkJoinState.Ready;
        DispatchNetworkListener(e => e.OnNetworkReady());
    }

    private static void DispatchNetworkListener(Action<INetworkListener> action)
    {
        NetSession? session = Session;
        Scene? scene = s_state.Scene;
        if (scene.IsNotValid())
        {
            return;
        }

        scene!.RunEvent<INetworkListener>(listener =>
        {
            // A previous listener can shut down or replace the scene during the same dispatch.
            if (Session == session && s_state.Scene == scene)
            {
                action(listener);
            }
        }, true);
    }

    private static bool CanReceiveEngineMessage(NetPeer peer, EngineMessage kind)
    {
        if (IsHost && !peer.IsLocal && kind is EngineMessage.Sync or EngineMessage.Transform or EngineMessage.RpcToHost
                or EngineMessage.StaticRpcToHost)
        {
            return IsPeerReady(peer);
        }

        if (IsClient && kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost)
        {
            return IsReady;
        }

        return true;
    }

    private static void UpdateReadiness()
    {
        if (!IsActive)
        {
            return;
        }

        if (IsClient)
        {
            if (!IsReady && Now >= s_state.ReadyDeadline)
            {
                Shutdown($"Scene readiness timed out during {JoinState}.");
            }

            return;
        }

        NetSession session = Session!;
        foreach ((int id, PeerReadiness state) in s_state.PeerReadiness.ToArray())
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

    private static void WriteCatchUpTransforms(NetWriter message)
    {
        NetworkEntry[] entries = s_state.Entries.Values
            .Where(e => e.GameObject.IsValid() && e.GameObject.Network.HasTransformSync).ToArray();
        message.WriteVarUInt32((uint)entries.Length);
        foreach (NetworkEntry entry in entries)
        {
            message.WriteVarUInt32(entry.NetId);
            message.WriteVarUInt32(entry.Generation);
            message.WriteVarUInt32(entry.TransformSync.HasReceived ? entry.TransformSync.LastReceivedTick : s_state.Tick);
            Transform transform = entry.GameObject.Transform;
            NetSerializers.WriteFloat3(message, transform.LocalPosition);
            NetSerializers.WriteQuaternion(message, transform.LocalRotation);
            NetSerializers.WriteFloat3(message, transform.LocalScale);
        }
    }

    private static void ApplyCatchUpTransforms(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            uint netId = reader.ReadVarUInt32();
            uint generation = reader.ReadVarUInt32();
            uint tick = reader.ReadVarUInt32();
            Float3 position = NetSerializers.ReadFloat3(ref reader);
            Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
            Float3 scale = NetSerializers.ReadFloat3(ref reader);
            float length = Quaternion.LengthSquared(rotation);
            if (!Finite(position) || !Finite(scale) || !float.IsFinite(length) || length < 1e-12f)
            {
                throw new InvalidDataException("Scene catch-up contains an invalid transform.");
            }

            if (s_state.Entries.TryGetNet(netId, out NetworkEntry? entry) && entry.Generation == generation)
            {
                entry.TransformSync.Snap(entry.GameObject, tick, position, Quaternion.Normalize(rotation), scale);
            }
        }
    }

    private sealed class PeerReadiness(ulong generation, double deadline)
    {
        public readonly double Deadline = deadline;
        public readonly ulong Generation = generation;
        public NetworkJoinState Stage = NetworkJoinState.Loading;
    }
}
