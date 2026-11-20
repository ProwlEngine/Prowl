// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Vector;
using Prowl.Courier;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkObject
{
    /// <summary>The pose of each of its transforms.</summary>
    internal void WriteHandoffPoses(NetWriter output)
    {
        using NetWriter poses = new();
        uint count = 0;
        foreach (NetworkTransform transform in Transforms)
        {
            if (transform.IsNotValid() || !transform.Syncs)
            {
                continue;
            }

            Transform moved = transform.Moved.Transform;
            new HandoffPoseMessage(transform.Index, moved.LocalPosition, moved.LocalRotation, moved.LocalScale)
                .Write(poses);
            count++;
        }

        new HandoffPoseBlock(count).Write(output);
        output.WriteBytes(poses.WrittenSpan);
    }

    /// <summary>Applies the poses <paramref name="sender" /> handed over, once every one of them checks out.</summary>
    internal void ApplyHandoffPoses(ReadOnlySpan<byte> data, NetPeer sender)
    {
        NetReader reader = new(data);
        uint count = HandoffPoseBlock.Read(ref reader).Count;
        if (count > reader.Remaining / HandoffPoseMessage.Size)
        {
            throw new InvalidDataException("Handoff pose count exceeds the remaining data.");
        }

        List<(NetworkTransform Transform, Float3 Position, Quaternion Rotation, Float3 Scale)> poses = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            HandoffPoseMessage pose = HandoffPoseMessage.Read(ref reader);
            Float3 position = pose.Position, scale = pose.Scale;
            Quaternion rotation = pose.Rotation;
            if (pose.Index >= Transforms.Length || (!sender.IsHost && Owner != sender.Id))
            {
                throw new InvalidDataException("A handoff contains an unknown or unauthorized transform.");
            }

            float rotationLength = Quaternion.LengthSquared(rotation);
            if (!NetworkValidation.Finite(position) || !NetworkValidation.Finite(scale) || !float.IsFinite(rotationLength) || rotationLength < 1e-12f)
            {
                throw new InvalidDataException("A handoff contains an invalid transform.");
            }

            poses.Add((Transforms[pose.Index], position, Quaternion.Normalize(rotation), scale));
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("A handoff contains trailing bytes.");
        }

        foreach ((NetworkTransform transform, Float3 position, Quaternion rotation, Float3 scale) in poses)
        {
            if (transform.IsValid() && transform.Syncs)
            {
                transform.Apply(position, rotation, scale);

                // Older interpolation samples would pull it back from the pose it was handed over at.
                transform.Sync.ClearHistory();
            }
        }
    }

    /// <summary>Forgets what its transforms had sent and received, as when its controller changes.</summary>
    internal void ResetTransforms()
    {
        foreach (NetworkTransform transform in Transforms)
        {
            transform.Sync.Reset();
        }
    }

    internal Float3 OwnershipVelocity() => System.HasControl(GameObject) &&
                                           GameObject.GetComponent<Rigidbody3D>() is { } body
        ? body.LinearVelocity
        : SampledVelocity();

    internal void SendOwnershipState(Float3 velocity)
    {
        // A new controller starts from the host's latest accepted state, without interpolation delay.
        if (!System.HasControl(GameObject))
        {
            foreach (NetworkTransform transform in Transforms)
            {
                if (transform.IsValid() && transform.Sync.Samples.Count > 0)
                {
                    TransformSample sample = transform.Sync.Samples[^1];
                    transform.Apply(sample.Position, sample.Rotation, sample.Scale);
                }
            }
        }

        // What changed so far goes ahead of the handoff, in order with it.
        System.Sync.SendSyncChanges();
        using NetWriter poses = new(), message = new();
        WriteHandoffPoses(poses);
        new OwnershipStateMessage(GameObject.Identifier, Generation, velocity, poses.WrittenSpan).Write(message);
        System.SendToObservers(this, EngineMessage.OwnershipState, message.WrittenSpan);
        foreach (NetPeer peer in System.Session!.Peers)
        {
            if (!peer.IsLocal && Sees(this, peer))
            {
                System.Transforms.Delivered(peer, GameObject);
            }
        }
    }
}
