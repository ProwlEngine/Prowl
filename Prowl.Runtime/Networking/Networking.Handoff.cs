// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Ownership handoff. Whoever hands an object over sends its pending [Sync] changes first, on the same ordered
// channel, so the handoff itself only carries what otherwise goes unreliably: the final pose of each transform.
//
// Poses are [varuint count] then per object [guid][float3 position][quaternion rotation][float3 scale].
public static partial class Networking
{
    private const int HandoffPoseSize = 16 + 12 + 16 + 12;

    /// <summary>The poses in <paramref name="root" />'s tree: all of them, or only the ones this peer controls.</summary>
    private static void WriteHandoffPoses(GameObject root, NetWriter output, bool includeAll)
    {
        NetworkEntry? entry = FindEntry(root);
        using NetWriter poses = new();
        uint count = 0;
        foreach (GameObject go in root.GetChildrenDeep().Prepend(root))
        {
            if (NetworkAccessor.IsExcludedFromNetwork(go) || FindEntry(go) != entry || !go.Network.HasTransformSync ||
                (!includeAll && !HasControl(go)))
            {
                continue;
            }

            poses.WriteGuid(go.Identifier);
            NetSerializers.WriteFloat3(poses, go.Transform.LocalPosition);
            NetSerializers.WriteQuaternion(poses, go.Transform.LocalRotation);
            NetSerializers.WriteFloat3(poses, go.Transform.LocalScale);
            count++;
        }

        output.WriteVarUInt32(count);
        output.WriteBytes(poses.WrittenSpan);
    }

    /// <summary>Applies the poses <paramref name="sender" /> handed over, once every one of them checks out.</summary>
    private static void ApplyHandoffPoses(GameObject root, ReadOnlySpan<byte> data, NetPeer sender)
    {
        NetReader reader = new(data);
        uint count = reader.ReadVarUInt32();
        if (count > reader.Remaining / HandoffPoseSize)
        {
            throw new InvalidDataException("Handoff pose count exceeds the remaining data.");
        }

        List<(GameObject Target, Float3 Position, Quaternion Rotation, Float3 Scale)> poses = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            Guid id = reader.ReadGuid();
            Float3 position = NetSerializers.ReadFloat3(ref reader);
            Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
            Float3 scale = NetSerializers.ReadFloat3(ref reader);
            if (FindByIdentifier(id) is not GameObject go || !InHandoffTree(go, root) ||
                NetworkAccessor.IsExcludedFromNetwork(go) || (!sender.IsHost && OwnerOf(go) != sender.Id))
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
        if (FindEntry(root) is { } entry)
        {
            entry.Samples.Clear();
            entry.Timeline.Clear();
        }
    }

    private static bool InHandoffTree(GameObject go, GameObject root)
    {
        if (FindEntry(go) != FindEntry(root))
        {
            return false;
        }

        for (GameObject? ancestor = go; ancestor.IsValid(); ancestor = ancestor.Parent)
        {
            if (ancestor == root)
            {
                return true;
            }
        }

        return false;
    }
}
