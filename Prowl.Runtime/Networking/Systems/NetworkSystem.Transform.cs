// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Messages;
using Prowl.Runtime.Networking.Objects;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking.Systems;

// Networked objects' transforms on the wire. Each object's TransformSync decides what it sends and shows; this
// batches its records into messages, sends them, and hands received ones back to the right object. Whoever
// controls an object sends the channels (position, rotation, scale) that changed while it moves, and once it is
// still, its full pose once more, reliably and at full precision. Static GameObjects are never synced.
//
// A Transform message is [varuint tick][f64 sender time], then a run of records:
// [varuint net id][varuint generation][u8 channels] then, as the channels say, [f32 seconds since it last
// moved][float3 position][rotation: 4 bytes compact, or 16 exact][float3 scale]. A channel left out is unchanged.
internal sealed partial class NetworkSystem
{
    private const int SessionMessageHeaderSize = sizeof(ushort);


    internal static void ValidateSceneMessageLimit(NetSessionOptions? options)
    {
        if ((options ?? Networking.Configuration.CreateSessionOptions()).MaxMessageSize <
            TransformMessage.MinMessageSize)
        {
            throw new NotSupportedException(
                $"Scene replication requires a message limit of at least {TransformMessage.MinMessageSize} bytes.");
        }
    }

    internal static TransformChannels SyncedChannels(GameObject go) =>
        (go.Network.SyncsTransform(NetworkFlags.NoPositionSync) ? TransformChannels.Position : 0) |
        (go.Network.SyncsTransform(NetworkFlags.NoRotationSync) ? TransformChannels.Rotation : 0) |
        (go.Network.SyncsTransform(NetworkFlags.NoScaleSync) ? TransformChannels.Scale : 0);

    internal void SendTransforms()
    {
        uint tick = ++Tick;
        double now = Now;

        using TransformBatch moving = new(this, tick, now), settled = new(this, tick, now);
        foreach (NetworkObject entry in Entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid() || !go.Network.HasTransformSync || !HasControl(go) ||
                entry.TransformSync.Capture(go, now, LastTransformTime) is not { } update)
            {
                continue;
            }

            TransformBatch batch = entry.TransformSync.Settled ? settled : moving;
            new TransformRecord(entry.NetId, entry.Generation, update).Write(batch.Begin());
            batch.End();
        }

        moving.Send(null, false);
        settled.Send(null, true);
        LastTransformTime = now;
    }


    internal void OnTransform(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost && !sender.IsHost)
        {
            return;
        }

        TransformMessage message = TransformMessage.Read(ref reader);
        uint tick = message.Tick;
        double sentAt = message.SentAt;
        if (!double.IsFinite(sentAt) || sentAt < 0)
        {
            throw new InvalidDataException("A transform message has an invalid time.");
        }

        double now = Now;
        using TransformBatch? relay = IsHost ? new TransformBatch(this, tick, sentAt) : null;
        while (!reader.IsAtEnd)
        {
            TransformRecord record = TransformRecord.Read(ref reader);
            (uint netId, uint generation, TransformUpdate update) = record;
            if (!Entries.TryGetNet(netId, out NetworkObject? entry) || !entry.GameObject.IsValid() ||
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
                new TransformRecord(netId, generation, taken).Write(relay.Begin());
                relay.End();
            }
        }

        // Relay as received, so a settled pose stays reliable.
        relay?.Send(sender, delivery == Delivery.Reliable);
    }

    /// <summary>Moves every object this peer doesn't control to where its controller had it, InterpolationDelay ago.</summary>
    internal void ApplyInterpolation()
    {
        double now = Now;
        foreach (NetworkObject entry in Entries.Values)
        {
            if (entry.GameObject.IsValid() && !HasControl(entry.GameObject))
            {
                entry.TransformSync.Interpolate(entry.GameObject, now);
            }
        }
    }

    internal static void ApplyNetworkTransform(GameObject go, Float3 position, Quaternion rotation, Float3 scale)
    {
        if (go.Network.SyncsTransform(NetworkFlags.NoPositionSync))
        {
            go.Transform.LocalPosition = position;
        }

        if (go.Network.SyncsTransform(NetworkFlags.NoRotationSync))
        {
            go.Transform.LocalRotation = rotation;
        }

        if (go.Network.SyncsTransform(NetworkFlags.NoScaleSync))
        {
            go.Transform.LocalScale = scale;
        }
    }

    internal static bool Finite(Float3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);


    /// <summary>One tick's transform records, sent to each peer in messages as large as its connection allows.</summary>
    private sealed class TransformBatch(NetworkSystem scene, uint tick, double time) : IDisposable
    {
        private readonly List<int> _ends = new();
        private readonly NetWriter _records = new(1024);

        public void Dispose() => _records.Dispose();

        /// <summary>Where the next record goes; <see cref="End" /> once it's written.</summary>
        public NetWriter Begin() => _records;

        public void End() => _ends.Add(_records.Length);

        /// <summary>To the host from a client, or from the host to every client but <paramref name="except" />.</summary>
        public void Send(NetPeer? except, bool reliable)
        {
            if (_ends.Count == 0)
            {
                return;
            }

            NetSession session = scene.Session!;
            if (!scene.IsHost)
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

            int limit = scene.Session!.Options.MaxMessageSize;
            Delivery delivery = Delivery.Reliable;
            if (!reliable && scene.Session.Transport.Capabilities.Supports(Delivery.Unreliable) &&
                connection.GetMaxMessageSize(Delivery.Unreliable) >= TransformMessage.MinMessageSize)
            {
                delivery = Delivery.Unreliable;
                limit = Math.Min(limit, connection.GetMaxMessageSize(Delivery.Unreliable));
            }

            // Warp adds a two-byte message id. Its optional batch framing is handled by Warp itself.
            int budget = limit - SessionMessageHeaderSize;
            using NetWriter message = new(Math.Min(budget, _records.Length + TransformMessage.HeaderSize));
            int start = 0;
            for (int i = 0; i < _ends.Count && connection.IsConnected; i++)
            {
                if (message.Length == 0)
                {
                    new TransformMessage(tick, time).Write(message);
                }

                ReadOnlySpan<byte> record = _records.WrittenSpan[start.._ends[i]];
                start = _ends[i];
                message.WriteBytes(record);
                bool full = i + 1 == _ends.Count ||
                            message.Length + (_ends[i + 1] - start) > budget;
                if (full)
                {
                    scene.SendEngine(peer, EngineMessage.Transform, message.WrittenSpan, delivery);
                    message.Reset();
                }
            }
        }
    }
}
