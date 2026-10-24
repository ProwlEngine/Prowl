// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects' transforms on the wire. Each object's TransformSync decides what it sends and shows; this
// batches its records into messages, sends them, and hands received ones back to the right object. Whoever
// controls an object sends the channels (position, rotation, scale) that changed while it moves, and once it is
// still, its full pose once more, reliably and at full precision. Static GameObjects are never synced.
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
        uint tick = ++s_state.Tick;
        double now = Now;

        using TransformBatch moving = new(tick, now), settled = new(tick, now);
        foreach (NetworkEntry entry in s_state.Entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid() || !go.Network.HasTransformSync || !HasControl(go) ||
                entry.TransformSync.Capture(go, now, s_state.LastTransformTime) is not { } update)
            {
                continue;
            }

            TransformBatch batch = entry.TransformSync.Settled ? settled : moving;
            WriteTransformRecord(batch.Begin(), entry.NetId, entry.Generation, update);
            batch.End();
        }

        moving.Send(null, reliable: false);
        settled.Send(null, reliable: true);
        s_state.LastTransformTime = now;
    }

    private static void WriteTransformRecord(NetWriter writer, uint netId, uint generation, in TransformUpdate update)
    {
        TransformChannels channels = update.Channels;
        writer.WriteVarUInt32(netId);
        writer.WriteVarUInt32(generation);
        writer.WriteByte((byte)channels);
        if ((channels & TransformChannels.Resumed) != 0) writer.WriteSingle(update.StillFor);
        if ((channels & TransformChannels.Position) != 0) NetSerializers.WriteFloat3(writer, update.Position);
        if ((channels & TransformChannels.Rotation) != 0)
        {
            if ((channels & TransformChannels.ExactRotation) != 0) NetSerializers.WriteQuaternion(writer, update.Rotation);
            else NetSerializers.WriteCompactQuaternion(writer, update.Rotation);
        }

        if ((channels & TransformChannels.Scale) != 0) NetSerializers.WriteFloat3(writer, update.Scale);
    }

    private static TransformUpdate ReadTransformRecord(ref NetReader reader, out uint netId, out uint generation)
    {
        netId = reader.ReadVarUInt32();
        generation = reader.ReadVarUInt32();
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

        return new TransformUpdate(channels, stillFor, position, rotation, scale);
    }

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
            TransformUpdate update = ReadTransformRecord(ref reader, out uint netId, out uint generation);
            if (!s_state.Entries.TryGetNet(netId, out NetworkEntry? entry) || !entry.GameObject.IsValid() ||
                !entry.GameObject.Network.HasTransformSync || HasControl(entry.GameObject) ||
                entry.Generation != generation)
            {
                continue;
            }

            // From a client, only for what it owns.
            if ((!sender.IsHost && entry.Owner != sender.Id) ||
                entry.TransformSync.Receive(entry.GameObject, tick, sentAt, now, update) is not { } taken)
            {
                continue;
            }

            if (relay != null)
            {
                WriteTransformRecord(relay.Begin(), netId, generation, taken);
                relay.End();
            }
        }

        // Relay as received, so a settled pose stays reliable.
        relay?.Send(sender, delivery == Delivery.Reliable);
    }

    /// <summary>Moves every object this peer doesn't control to where its controller had it, InterpolationDelay ago.</summary>
    private static void ApplyInterpolation()
    {
        double now = Now;
        foreach (NetworkEntry entry in s_state.Entries.Values)
        {
            if (entry.GameObject.IsValid() && !HasControl(entry.GameObject))
            {
                entry.TransformSync.Interpolate(entry.GameObject, now);
            }
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
