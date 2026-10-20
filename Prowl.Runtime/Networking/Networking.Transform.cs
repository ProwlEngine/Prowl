// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects' transforms. Whoever controls an object sends its local position, rotation and
// scale every tick it changes (and once a second regardless, since these go unreliably). Everyone
// else buffers them and shows the object InterpolationDelay in the past, blending between updates.
//
// A Transform message is a run of [guid][u32 tick][float3 position][quaternion rotation][float3 scale].
public static partial class Networking
{
    private const int TransformRecordSize = 16 + 4 + 12 + 16 + 12;
    private const int MaxSamples = 32;
    private const double TransformRefreshSeconds = 1;

    private static uint s_tick;

    private static void SendTransforms()
    {
        s_tick++;
        double now = Now;

        using NetWriter message = new(1024);
        foreach (NetworkEntry entry in s_entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid() || !go.NetworkSyncTransform || !HasControl(go))
            {
                continue;
            }

            Transform t = go.Transform;
            Float3 position = t.LocalPosition;
            Quaternion rotation = t.LocalRotation;
            Float3 scale = t.LocalScale;

            bool unchanged = entry.HasSent && position.Equals(entry.SentPosition) &&
                             rotation.Equals(entry.SentRotation) && scale.Equals(entry.SentScale);
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
            message.WriteUInt32(s_tick);
            NetSerializers.WriteFloat3(message, position);
            NetSerializers.WriteQuaternion(message, rotation);
            NetSerializers.WriteFloat3(message, scale);

            // Unreliable messages have to fit in one packet.
            if (message.Length + TransformRecordSize > 1000)
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
            BroadcastEngine(EngineMessage.Transform, message.WrittenSpan, Delivery.Unreliable, except);
        }
        else
        {
            SendEngine(session.HostPeer!, EngineMessage.Transform, message.WrittenSpan, Delivery.Unreliable);
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
            uint tick = reader.ReadUInt32();
            Float3 position = NetSerializers.ReadFloat3(ref reader);
            Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
            Float3 scale = NetSerializers.ReadFloat3(ref reader);

            if (!s_entries.TryGetValue(id, out NetworkEntry? entry) || !entry.GameObject.IsValid() ||
                HasControl(entry.GameObject))
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

            entry.HasReceived = true;
            entry.LastReceivedTick = tick;
            if (entry.Samples.Count == MaxSamples)
            {
                entry.Samples.RemoveAt(0);
            }

            entry.Samples.Add(new TransformSample(now, position, rotation, scale));

            if (relay != null)
            {
                relay.WriteGuid(id);
                relay.WriteUInt32(tick);
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
        double renderTime = Now - InterpolationDelay.TotalSeconds;

        foreach (NetworkEntry entry in s_entries.Values)
        {
            if (entry.Samples.Count == 0 || !entry.GameObject.IsValid() || HasControl(entry.GameObject))
            {
                continue;
            }

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

            Transform transform = entry.GameObject.Transform;
            transform.LocalPosition = Maths.Lerp(from.Position, to.Position, t);
            transform.LocalRotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
            transform.LocalScale = Maths.Lerp(from.Scale, to.Scale, t);
        }
    }

    private readonly record struct TransformSample(double Time, Float3 Position, Quaternion Rotation, Float3 Scale);
}
