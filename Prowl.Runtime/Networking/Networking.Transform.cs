// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects' transforms. Whoever controls an object sends its local position, rotation and
// scale every tick it changes (and once a second regardless, since these normally go unreliably). Everyone
// else buffers them and shows the object InterpolationDelay in the past, blending between updates.
//
// A Transform message is a run of [guid][u32 generation][u32 tick][f64 sender time][float3 position][quaternion rotation][float3 scale].
public static partial class Networking
{
    private const int TransformRecordSize = 16 + 4 + 4 + 8 + 12 + 16 + 12;

    private const int SessionMessageHeaderSize = sizeof(ushort);

    // A preferred working buffer size, not a requirement on the transport.
    private const int TransformBatchBytes = 1000;
    private const int MaxSamples = 32;
    private const double TransformRefreshSeconds = 1;

    private static uint s_tick;

    private static void ValidateSceneMessageLimit(NetSessionOptions? options)
    {
        if ((options ?? s_configuration.CreateSessionOptions()).MaxMessageSize <
            TransformRecordSize + SessionMessageHeaderSize)
        {
            throw new NotSupportedException(
                $"Scene replication requires a message limit of at least {TransformRecordSize + SessionMessageHeaderSize} bytes.");
        }
    }

    private static void SendTransforms()
    {
        s_tick++;
        double now = Now;

        using NetWriter message = new(1024);
        foreach (NetworkEntry entry in s_entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid() || !go.Network.HasTransformSync || !HasControl(go))
            {
                continue;
            }

            Transform t = go.Transform;
            Float3 position = t.LocalPosition;
            Quaternion rotation = t.LocalRotation;
            Float3 scale = t.LocalScale;

            bool unchanged = entry.HasSent && (!go.Network.SyncsTransform(NetworkFlags.NoPositionSync) || position.Equals(entry.SentPosition)) &&
                             (!go.Network.SyncsTransform(NetworkFlags.NoRotationSync) || rotation.Equals(entry.SentRotation)) &&
                             (!go.Network.SyncsTransform(NetworkFlags.NoScaleSync) || scale.Equals(entry.SentScale));
            if (unchanged && now - entry.LastSentTime < TransformRefreshSeconds)
            {
                continue;
            }

            entry.HasSent = true;
            entry.LastSentTime = now;
            entry.SentPosition = position;
            entry.SentRotation = rotation;
            entry.SentScale = scale;

            message.WriteGuid(go.Identifier);
            message.WriteUInt32(entry.Generation);
            message.WriteUInt32(s_tick);
            message.WriteDouble(now);
            NetSerializers.WriteFloat3(message, position);
            NetSerializers.WriteQuaternion(message, rotation);
            NetSerializers.WriteFloat3(message, scale);

            // Keep capture bounded; sending splits records to each connection's limit.
            if (message.Length + TransformRecordSize > TransformBatchBytes)
            {
                SendTransformMessage(message, null);
                message.Reset();
            }
        }

        if (message.Length > 0)
        {
            SendTransformMessage(message, null);
        }
    }

    private static void SendTransformMessage(NetWriter message, NetPeer? except)
    {
        NetSession session = Session!;
        if (IsHost)
        {
            foreach (NetPeer peer in session.Peers)
            {
                if (!peer.IsLocal && peer != except)
                {
                    SendTransformMessage(peer, message.WrittenSpan);
                }
            }
        }
        else
        {
            SendTransformMessage(session.HostPeer!, message.WrittenSpan);
        }
    }

    private static void SendTransformMessage(NetPeer peer, ReadOnlySpan<byte> records)
    {
        if (peer.Connection is not { IsConnected: true } connection)
        {
            return;
        }

        int limit = Session!.Options.MaxMessageSize;
        Delivery delivery = Delivery.Reliable;
        if (Session.Transport.Capabilities.Supports(Delivery.Unreliable) &&
            connection.GetMaxMessageSize(Delivery.Unreliable) >= TransformRecordSize + SessionMessageHeaderSize)
        {
            delivery = Delivery.Unreliable;
            limit = Math.Min(limit, connection.GetMaxMessageSize(Delivery.Unreliable));
        }

        // Warp adds a two-byte message id. Its optional batch framing is handled by Warp itself.
        int batchBytes = (limit - SessionMessageHeaderSize) / TransformRecordSize * TransformRecordSize;
        for (int offset = 0; offset < records.Length && connection.IsConnected; offset += batchBytes)
        {
            SendEngine(peer, EngineMessage.Transform,
                records.Slice(offset, Math.Min(batchBytes, records.Length - offset)), delivery);
        }
    }

    private static void OnTransform(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost && !sender.IsHost)
        {
            return;
        }

        double now = Now;
        using NetWriter? relay = IsHost ? new NetWriter(1024) : null;

        while (!reader.IsAtEnd)
        {
            Guid id = reader.ReadGuid();
            uint generation = reader.ReadUInt32();
            uint tick = reader.ReadUInt32();
            double sentAt = reader.ReadDouble();
            Float3 position = NetSerializers.ReadFloat3(ref reader);
            Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
            Float3 scale = NetSerializers.ReadFloat3(ref reader);

            if (!s_entries.TryGetValue(id, out NetworkEntry? entry) || !entry.GameObject.IsValid() ||
                !entry.GameObject.Network.HasTransformSync || HasControl(entry.GameObject) || entry.Generation != generation)
            {
                continue;
            }

            // From a client, only for what it owns.
            if (!sender.IsHost && entry.Owner != sender.Id)
            {
                continue;
            }

            // Unreliable, so older updates can arrive after newer ones.
            if (entry.HasReceived && (int)(tick - entry.LastReceivedTick) <= 0)
            {
                continue;
            }

            float rotationLength = Quaternion.LengthSquared(rotation);
            if (!double.IsFinite(sentAt) || sentAt < 0 || !Finite(position) || !Finite(scale) ||
                !float.IsFinite(rotationLength) || rotationLength < 1e-12f)
            {
                throw new InvalidDataException("A transform contains invalid coordinates, rotation or time.");
            }

            rotation = Quaternion.Normalize(rotation);
            // Use the host-supplied object policy, never a mask chosen by the sender.
            Transform current = entry.GameObject.Transform;
            if (!entry.GameObject.Network.SyncsTransform(NetworkFlags.NoPositionSync)) position = current.LocalPosition;
            if (!entry.GameObject.Network.SyncsTransform(NetworkFlags.NoRotationSync)) rotation = current.LocalRotation;
            if (!entry.GameObject.Network.SyncsTransform(NetworkFlags.NoScaleSync)) scale = current.LocalScale;
            if (!entry.Timeline.Observe(sentAt, now))
            {
                continue;
            }

            entry.HasReceived = true;
            entry.LastReceivedTick = tick;
            if (entry.Samples.Count == MaxSamples)
            {
                entry.Samples.RemoveAt(0);
            }

            entry.Samples.Add(new TransformSample(sentAt, position, rotation, scale));
            if ((entry.GameObject.Network.Flags & NetworkFlags.NoInterpolation) != 0)
                ApplyNetworkTransform(entry.GameObject, position, rotation, scale);

            if (relay != null)
            {
                relay.WriteGuid(id);
                relay.WriteUInt32(generation);
                relay.WriteUInt32(tick);
                relay.WriteDouble(sentAt);
                NetSerializers.WriteFloat3(relay, position);
                NetSerializers.WriteQuaternion(relay, rotation);
                NetSerializers.WriteFloat3(relay, scale);
            }
        }

        if (relay != null && relay.Length > 0)
        {
            SendTransformMessage(relay, sender);
        }
    }

    /// <summary>Moves every object this peer doesn't control to where its controller had it, InterpolationDelay ago.</summary>
    private static void ApplyInterpolation()
    {
        double now = Now;

        foreach (NetworkEntry entry in s_entries.Values)
        {
            if (entry.Samples.Count == 0 || !entry.GameObject.IsValid() || HasControl(entry.GameObject) ||
                (entry.GameObject.Network.Flags & NetworkFlags.NoInterpolation) != 0)
            {
                continue;
            }

            double renderTime = entry.Timeline.RenderTime(now, InterpolationDelay.TotalSeconds);
            List<TransformSample> samples = entry.Samples;

            // Drop samples nothing will blend from again: everything before the last one at or before renderTime.
            int keepFrom = 0;
            while (keepFrom + 1 < samples.Count && samples[keepFrom + 1].Time <= renderTime)
            {
                keepFrom++;
            }

            if (keepFrom > 0)
            {
                samples.RemoveRange(0, keepFrom);
            }

            TransformSample from = samples[0];
            TransformSample to = samples.Count > 1 ? samples[1] : from;

            float t = to.Time > from.Time
                ? (float)Math.Clamp((renderTime - from.Time) / (to.Time - from.Time), 0, 1)
                : 1;

            ApplyNetworkTransform(entry.GameObject, Maths.Lerp(from.Position, to.Position, t),
                Quaternion.Slerp(from.Rotation, to.Rotation, t), Maths.Lerp(from.Scale, to.Scale, t));
        }
    }

    private static void ApplyNetworkTransform(GameObject go, Float3 position, Quaternion rotation, Float3 scale)
    {
        if (go.Network.SyncsTransform(NetworkFlags.NoPositionSync)) go.Transform.LocalPosition = position;
        if (go.Network.SyncsTransform(NetworkFlags.NoRotationSync)) go.Transform.LocalRotation = rotation;
        if (go.Network.SyncsTransform(NetworkFlags.NoScaleSync)) go.Transform.LocalScale = scale;
    }

    private static bool Finite(Float3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private readonly record struct TransformSample(double Time, Float3 Position, Quaternion Rotation, Float3 Scale);
}
