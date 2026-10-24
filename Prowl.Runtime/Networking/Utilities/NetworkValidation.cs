// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>Structural validation shared by wire readers. Gameplay authorization stays in the systems.</summary>
internal static class NetworkValidation
{
    internal static uint NetId(uint id) => id != 0
        ? id
        : throw new InvalidDataException("A networked object came without a network id.");

    internal static NetworkOrphaned Orphaned(NetworkOrphaned policy) => Enum.IsDefined(policy)
        ? policy
        : throw new InvalidDataException("Invalid network orphaned policy.");

    internal static void SnapshotFormat(byte format)
    {
        if (format != SceneSnapshotMessage.CurrentFormat)
        {
            throw new InvalidDataException(
                $"The host sent scene snapshot format {format}, this build reads {SceneSnapshotMessage.CurrentFormat}.");
        }
    }

    internal static void SceneGeneration(ulong generation)
    {
        if (generation == 0)
        {
            throw new InvalidDataException("Scene generation cannot be zero.");
        }
    }

    internal static void SyncCount(uint count, int remaining)
    {
        if (count > remaining / SyncValue.MinimumSize)
        {
            throw new InvalidDataException("Sync count exceeds the remaining data.");
        }
    }

    internal static void ValidateTransformChannels(TransformChannels channels)
    {
        if ((channels & ~TransformChannels.All) != 0)
        {
            throw new InvalidDataException("A transform has unknown channels.");
        }
    }

    internal static void Transform(float stillFor, Float3 position, Quaternion rotation, Float3 scale)
    {
        float rotationLength = Quaternion.LengthSquared(rotation);
        if (!float.IsFinite(stillFor) || stillFor < 0 || !Finite(position) || !Finite(scale) ||
            !float.IsFinite(rotationLength) || rotationLength < 1e-12f)
        {
            throw new InvalidDataException("A transform contains invalid coordinates, rotation or time.");
        }
    }

    internal static void RecipientCount(uint count, int maxPeers)
    {
        if (count > (uint)maxPeers + 1)
        {
            throw new InvalidDataException("RPC recipient list exceeds the session peer limit.");
        }
    }

    internal static void AddRecipient(HashSet<int> ids, int id)
    {
        if (!ids.Add(id))
        {
            throw new InvalidDataException("RPC recipient list contains duplicate peers.");
        }
    }

    internal static void RecipientEnd(bool atEnd)
    {
        if (!atEnd)
        {
            throw new InvalidDataException("RPC recipient list contains trailing bytes.");
        }
    }

    internal static bool Finite(Float3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
