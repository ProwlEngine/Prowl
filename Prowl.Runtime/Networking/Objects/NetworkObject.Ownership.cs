// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Runtime.Networking.Messages;
using Prowl.Vector;
using Prowl.Warp;

using static Prowl.Runtime.Networking.Systems.NetworkSystem;

namespace Prowl.Runtime.Networking.Objects;

internal sealed partial class NetworkObject
{
    /// <summary>The tree's poses: all of them, or only the ones this peer controls.</summary>
    internal void WriteHandoffPoses(NetWriter output, bool includeAll)
    {
        using NetWriter poses = new();
        uint count = 0;
        foreach (GameObject go in GameObject.GetChildrenDeep().Prepend(GameObject))
        {
            if (NetworkAccessor.IsExcludedFromNetwork(go) || System.FindEntry(go) != this ||
                !go.Network.HasTransformSync ||
                (!includeAll && !System.HasControl(go)))
            {
                continue;
            }

            new HandoffPoseMessage(go.Identifier, go.Transform.LocalPosition,
                go.Transform.LocalRotation, go.Transform.LocalScale).Write(poses);
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

        List<(GameObject Target, Float3 Position, Quaternion Rotation, Float3 Scale)> poses = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            HandoffPoseMessage pose = HandoffPoseMessage.Read(ref reader);
            Guid id = pose.Identifier;
            Float3 position = pose.Position, scale = pose.Scale;
            Quaternion rotation = pose.Rotation;
            if (System.FindByIdentifier(id) is not GameObject go || !InHandoffTree(go) ||
                NetworkAccessor.IsExcludedFromNetwork(go) || (!sender.IsHost && System.OwnerOf(go) != sender.Id))
            {
                throw new InvalidDataException("A handoff contains an unknown or unauthorized transform.");
            }

            float rotationLength = Quaternion.LengthSquared(rotation);
            if (!Finite(position) || !Finite(scale) || !float.IsFinite(rotationLength) || rotationLength < 1e-12f)
            {
                throw new InvalidDataException("A handoff contains an invalid transform.");
            }

            poses.Add((go, position, Quaternion.Normalize(rotation), scale));
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("A handoff contains trailing bytes.");
        }

        foreach ((GameObject go, Float3 position, Quaternion rotation, Float3 scale) in poses)
        {
            if (go.Network.HasTransformSync)
            {
                ApplyNetworkTransform(go, position, rotation, scale);
            }
        }

        // Older interpolation samples would pull it back from the pose it was handed over at.
        TransformSync.ClearHistory();
    }

    internal bool InHandoffTree(GameObject go)
    {
        if (System.FindEntry(go) != this)
        {
            return false;
        }

        for (GameObject? ancestor = go; ancestor.IsValid(); ancestor = ancestor.Parent)
        {
            if (ancestor == GameObject)
            {
                return true;
            }
        }

        return false;
    }

    internal Float3 OwnershipVelocity() => System.HasControl(GameObject) &&
                                           GameObject.GetComponent<Rigidbody3D>() is { } body
        ? body.LinearVelocity
        : SampledVelocity();

    internal void SendOwnershipState(Float3 velocity)
    {
        // A forced takeover starts from the host's latest accepted state, without interpolation delay.
        if (!System.HasControl(GameObject) && TransformSync.Samples.Count > 0)
        {
            TransformSample sample = TransformSync.Samples[^1];
            ApplyNetworkTransform(GameObject, sample.Position, sample.Rotation, sample.Scale);
        }

        // Every value the host has accepted is already on its way to everyone; its own pending ones go now.
        System.Sync.SendSyncChanges();
        using NetWriter poses = new(), message = new();
        WriteHandoffPoses(poses, true);
        new OwnershipStateMessage(GameObject.Identifier, Generation, velocity, poses.WrittenSpan).Write(message);
        System.BroadcastEngine(EngineMessage.OwnershipState, message.WrittenSpan);
    }
}
