// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects' transforms. Whoever controls an object sends, each tick it moves, the channels (position,
// rotation, scale) that changed, and keeps sending a channel for LingerTicks after it stops, in case its last
// change was lost. Once the whole object has been still for SettleTicks, its full pose goes once more, reliably
// and at full precision; after that nothing is sent until it moves again. Static GameObjects are never synced.
// Everyone else buffers the updates and shows the object InterpolationDelay in the past, blending between them.
//
// A Transform message is [varuint tick][f64 sender time], then a run of records:
// [varuint net id][varuint generation][u8 channels] then, as the channels say, [f32 seconds since it last
// moved][float3 position][rotation: 4 bytes compact, or 16 exact][float3 scale]. A channel left out is unchanged.
public static partial class Networking
{
    private const int SessionMessageHeaderSize = sizeof(ushort);
    private const int TransformHeaderSize = 5 + 8;
    private const int MaxTransformRecordSize = 5 + 5 + 1 + 4 + 12 + 16 + 12;
    private const int MinTransformMessageSize = SessionMessageHeaderSize + TransformHeaderSize + MaxTransformRecordSize;
    private const int MaxSamples = 32;

    // Quiet ticks before the final pose goes out reliably.
    private const int SettleTicks = 3;

    // Ticks a channel is still sent after it stops changing, while the rest of the object moves on.
    private const int LingerTicks = 3;

    // Smaller changes don't count as movement, so a body jittering in place still settles. They accumulate
    // against the last pose sent, so slow motion is still sent once it adds up.
    private const float PositionEpsilon = 1e-4f;
    private const float RotationEpsilon = 1e-5f;
    private const float ScaleEpsilon = 1e-4f;

    private static uint s_tick;
    private static double s_lastTransformTime;

    [Flags]
    private enum TransformChannels : byte
    {
        Position = 1,
        Rotation = 2,
        Scale = 4,

        // The rotation is the full 16 bytes, as a settled pose sends it, rather than the compact 4.
        ExactRotation = 8,

        // It was still until the previous tick: receivers hold it at rest until then, then blend from there.
        Resumed = 16,

        Pose = Position | Rotation | Scale,
        All = Pose | ExactRotation | Resumed
    }

    private static void ValidateSceneMessageLimit(NetSessionOptions? options)
    {
        if ((options ?? s_configuration.CreateSessionOptions()).MaxMessageSize < MinTransformMessageSize)
        {
            throw new NotSupportedException(
                $"Scene replication requires a message limit of at least {MinTransformMessageSize} bytes.");
        }
    }

    private static TransformChannels SyncedChannels(GameObject go) =>
        (go.Network.SyncsTransform(NetworkFlags.NoPositionSync) ? TransformChannels.Position : 0) |
        (go.Network.SyncsTransform(NetworkFlags.NoRotationSync) ? TransformChannels.Rotation : 0) |
        (go.Network.SyncsTransform(NetworkFlags.NoScaleSync) ? TransformChannels.Scale : 0);

    private static void SendTransforms()
    {
        s_tick++;
        double now = Now;

        using TransformBatch moving = new(s_tick, now), settled = new(s_tick, now);
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
            TransformChannels synced = SyncedChannels(go);
            TransformChannels changed = entry.HasSent ? Changes(entry, synced, position, rotation, scale) : synced;

            TransformChannels channels;
            float stillFor = 0;
            if (changed != 0)
            {
                channels = changed | Lingering(entry, changed);
                if (entry.HasSent && entry.Settled && s_lastTransformTime > entry.LastSentTime)
                {
                    channels |= TransformChannels.Resumed;
                    stillFor = (float)(now - s_lastTransformTime);
                }

                entry.QuietTicks = 0;
                entry.Settled = false;
            }
            else if (!entry.Settled && ++entry.QuietTicks >= SettleTicks)
            {
                // Updates while moving can be lost, so the pose it comes to rest at must arrive, exactly.
                channels = synced | TransformChannels.ExactRotation;
                entry.Settled = true;
                entry.LingerPosition = entry.LingerRotation = entry.LingerScale = 0;
            }
            else
            {
                continue;
            }

            entry.HasSent = true;
            entry.LastSentTime = now;
            if ((channels & TransformChannels.Position) != 0) entry.SentPosition = position;
            if ((channels & TransformChannels.Rotation) != 0) entry.SentRotation = rotation;
            if ((channels & TransformChannels.Scale) != 0) entry.SentScale = scale;
            TransformBatch batch = entry.Settled ? settled : moving;
            WriteTransformRecord(batch.Begin(), entry.NetId, entry.Generation, channels, stillFor, position, rotation,
                scale);
            batch.End();
        }

        moving.Send(null, reliable: false);
        settled.Send(null, reliable: true);
        s_lastTransformTime = now;
    }

    private static TransformChannels Changes(NetworkEntry entry, TransformChannels synced, Float3 position,
        Quaternion rotation, Float3 scale) =>
        ((synced & TransformChannels.Position) != 0 && Moved(position, entry.SentPosition, PositionEpsilon)
            ? TransformChannels.Position
            : 0) |
        ((synced & TransformChannels.Rotation) != 0 && Moved(rotation, entry.SentRotation)
            ? TransformChannels.Rotation
            : 0) |
        ((synced & TransformChannels.Scale) != 0 && Moved(scale, entry.SentScale, ScaleEpsilon)
            ? TransformChannels.Scale
            : 0);

    /// <summary>The channels that changed recently but not this tick, counting down how long each has left.</summary>
    private static TransformChannels Lingering(NetworkEntry entry, TransformChannels changed) =>
        Linger(ref entry.LingerPosition, TransformChannels.Position, changed) |
        Linger(ref entry.LingerRotation, TransformChannels.Rotation, changed) |
        Linger(ref entry.LingerScale, TransformChannels.Scale, changed);

    private static TransformChannels Linger(ref int ticks, TransformChannels channel, TransformChannels changed)
    {
        if ((changed & channel) != 0)
        {
            ticks = LingerTicks;
            return 0;
        }

        if (ticks <= 0)
        {
            return 0;
        }

        ticks--;
        return channel;
    }

    private static void WriteTransformRecord(NetWriter writer, uint netId, uint generation, TransformChannels channels,
        float stillFor, Float3 position, Quaternion rotation, Float3 scale)
    {
        writer.WriteVarUInt32(netId);
        writer.WriteVarUInt32(generation);
        writer.WriteByte((byte)channels);
        if ((channels & TransformChannels.Resumed) != 0) writer.WriteSingle(stillFor);
        if ((channels & TransformChannels.Position) != 0) NetSerializers.WriteFloat3(writer, position);
        if ((channels & TransformChannels.Rotation) != 0)
        {
            if ((channels & TransformChannels.ExactRotation) != 0) NetSerializers.WriteQuaternion(writer, rotation);
            else NetSerializers.WriteCompactQuaternion(writer, rotation);
        }

        if ((channels & TransformChannels.Scale) != 0) NetSerializers.WriteFloat3(writer, scale);
    }

    private static bool Moved(Float3 a, Float3 b, float epsilon) =>
        MathF.Abs(a.X - b.X) > epsilon || MathF.Abs(a.Y - b.Y) > epsilon || MathF.Abs(a.Z - b.Z) > epsilon;

    private static bool Moved(Quaternion a, Quaternion b) =>
        MathF.Abs(a.X - b.X) > RotationEpsilon || MathF.Abs(a.Y - b.Y) > RotationEpsilon ||
        MathF.Abs(a.Z - b.Z) > RotationEpsilon || MathF.Abs(a.W - b.W) > RotationEpsilon;

    private static void OnTransform(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost && !sender.IsHost)
        {
            return;
        }

        uint tick = reader.ReadVarUInt32();
        double sentAt = reader.ReadDouble();
        if (!double.IsFinite(sentAt) || sentAt < 0)
        {
            throw new InvalidDataException("A transform message has an invalid time.");
        }

        double now = Now;
        using TransformBatch? relay = IsHost ? new TransformBatch(tick, sentAt) : null;
        while (!reader.IsAtEnd)
        {
            uint netId = reader.ReadVarUInt32();
            uint generation = reader.ReadVarUInt32();
            TransformChannels channels = (TransformChannels)reader.ReadByte();
            if ((channels & ~TransformChannels.All) != 0)
            {
                throw new InvalidDataException("A transform has unknown channels.");
            }

            float stillFor = (channels & TransformChannels.Resumed) != 0 ? reader.ReadSingle() : 0;
            Float3 position = (channels & TransformChannels.Position) != 0 ? NetSerializers.ReadFloat3(ref reader) : default;
            Quaternion rotation = (channels & TransformChannels.Rotation) == 0 ? Quaternion.Identity :
                (channels & TransformChannels.ExactRotation) != 0 ? NetSerializers.ReadQuaternion(ref reader) :
                NetSerializers.ReadCompactQuaternion(ref reader);
            Float3 scale = (channels & TransformChannels.Scale) != 0 ? NetSerializers.ReadFloat3(ref reader) : default;

            float rotationLength = Quaternion.LengthSquared(rotation);
            if (!float.IsFinite(stillFor) || stillFor < 0 || !Finite(position) || !Finite(scale) ||
                !float.IsFinite(rotationLength) || rotationLength < 1e-12f)
            {
                throw new InvalidDataException("A transform contains invalid coordinates, rotation or time.");
            }

            if (!s_entries.TryGetNet(netId, out NetworkEntry? entry) || !entry.GameObject.IsValid() ||
                !entry.GameObject.Network.HasTransformSync || HasControl(entry.GameObject) ||
                entry.Generation != generation)
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

            if (!entry.Timeline.Observe(sentAt, now))
            {
                continue;
            }

            // Use the host-supplied object policy, never channels chosen by the sender.
            channels &= SyncedChannels(entry.GameObject) | TransformChannels.ExactRotation | TransformChannels.Resumed;

            // Channels left out are as they were in the latest update, or as the object is if there's none.
            Transform current = entry.GameObject.Transform;
            TransformSample latest = entry.Samples.Count > 0
                ? entry.Samples[^1]
                : new TransformSample(sentAt, current.LocalPosition, current.LocalRotation, current.LocalScale);
            if ((channels & TransformChannels.Position) == 0) position = latest.Position;
            if ((channels & TransformChannels.Rotation) == 0) rotation = latest.Rotation;
            else rotation = Quaternion.Normalize(rotation);
            if ((channels & TransformChannels.Scale) == 0) scale = latest.Scale;

            // Held where it rested until just before it moved, rather than blended over the whole time it sat still.
            if ((channels & TransformChannels.Resumed) != 0 && entry.Samples.Count > 0 && sentAt - stillFor > latest.Time)
            {
                AddSample(entry, latest with { Time = sentAt - stillFor });
            }

            entry.HasReceived = true;
            entry.LastReceivedTick = tick;
            AddSample(entry, new TransformSample(sentAt, position, rotation, scale));
            if ((entry.GameObject.Network.Flags & NetworkFlags.NoInterpolation) != 0)
            {
                ApplyNetworkTransform(entry.GameObject, position, rotation, scale);
            }

            if (relay != null)
            {
                WriteTransformRecord(relay.Begin(), netId, generation, channels, stillFor, position, rotation, scale);
                relay.End();
            }
        }

        // Relay as received, so a settled pose stays reliable.
        relay?.Send(sender, delivery == Delivery.Reliable);
    }

    private static void AddSample(NetworkEntry entry, TransformSample sample)
    {
        if (entry.Samples.Count == MaxSamples)
        {
            entry.Samples.RemoveAt(0);
        }

        entry.Samples.Add(sample);
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

    /// <summary>One tick's transform records, sent to each peer in messages as large as its connection allows.</summary>
    private sealed class TransformBatch(uint tick, double time) : IDisposable
    {
        private readonly List<int> _ends = new();
        private readonly NetWriter _records = new(1024);

        /// <summary>Where the next record goes; <see cref="End" /> once it's written.</summary>
        public NetWriter Begin() => _records;

        public void End() => _ends.Add(_records.Length);

        public void Dispose() => _records.Dispose();

        /// <summary>To the host from a client, or from the host to every client but <paramref name="except" />.</summary>
        public void Send(NetPeer? except, bool reliable)
        {
            if (_ends.Count == 0)
            {
                return;
            }

            NetSession session = Session!;
            if (!IsHost)
            {
                SendTo(session.HostPeer!, reliable);
                return;
            }

            foreach (NetPeer peer in session.Peers)
            {
                if (!peer.IsLocal && peer != except)
                {
                    SendTo(peer, reliable);
                }
            }
        }

        private void SendTo(NetPeer peer, bool reliable)
        {
            if (peer.Connection is not { IsConnected: true } connection)
            {
                return;
            }

            int limit = Session!.Options.MaxMessageSize;
            Delivery delivery = Delivery.Reliable;
            if (!reliable && Session.Transport.Capabilities.Supports(Delivery.Unreliable) &&
                connection.GetMaxMessageSize(Delivery.Unreliable) >= MinTransformMessageSize)
            {
                delivery = Delivery.Unreliable;
                limit = Math.Min(limit, connection.GetMaxMessageSize(Delivery.Unreliable));
            }

            // Warp adds a two-byte message id. Its optional batch framing is handled by Warp itself.
            int budget = limit - SessionMessageHeaderSize;
            using NetWriter message = new(Math.Min(budget, _records.Length + TransformHeaderSize));
            int start = 0;
            for (int i = 0; i < _ends.Count && connection.IsConnected; i++)
            {
                if (message.Length == 0)
                {
                    message.WriteVarUInt32(tick);
                    message.WriteDouble(time);
                }

                ReadOnlySpan<byte> record = _records.WrittenSpan[start.._ends[i]];
                start = _ends[i];
                message.WriteBytes(record);
                bool full = i + 1 == _ends.Count ||
                            message.Length + (_ends[i + 1] - start) > budget;
                if (full)
                {
                    SendEngine(peer, EngineMessage.Transform, message.WrittenSpan, delivery);
                    message.Reset();
                }
            }
        }
    }
}
