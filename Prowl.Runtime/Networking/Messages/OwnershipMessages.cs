// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

internal readonly ref struct OwnershipRequestMessage(
    Guid identifier,
    uint generation,
    bool take,
    Float3 velocity,
    ReadOnlySpan<byte> poses,
    ReadOnlySpan<byte> values)
{
    internal readonly Guid Identifier = identifier;
    internal readonly uint Generation = generation;
    internal readonly bool Take = take;
    internal readonly Float3 Velocity = velocity;
    internal readonly ReadOnlySpan<byte> Poses = poses;

    /// <summary>A release's last [Sync] values, as a SyncBlock. Empty for a takeover.</summary>
    internal readonly ReadOnlySpan<byte> Values = values;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteBool(Take);
        NetSerializers.WriteFloat3(writer, Velocity);
        writer.WriteBytesWithLength(Poses);
        writer.WriteBytesWithLength(Values);
    }

    internal static OwnershipRequestMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        reader.ReadBool(),
        NetSerializers.ReadFloat3(ref reader),
        reader.ReadBytesWithLength(),
        reader.ReadBytesWithLength());
}

internal readonly ref struct OwnershipStateMessage(
    Guid identifier,
    uint generation,
    Float3 velocity,
    ReadOnlySpan<byte> poses,
    ReadOnlySpan<byte> values)
{
    internal readonly Guid Identifier = identifier;
    internal readonly uint Generation = generation;
    internal readonly Float3 Velocity = velocity;
    internal readonly ReadOnlySpan<byte> Poses = poses;

    /// <summary>The tree's [Sync] values as the host has them, as a SyncBlock.</summary>
    internal readonly ReadOnlySpan<byte> Values = values;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        NetSerializers.WriteFloat3(writer, Velocity);
        writer.WriteBytesWithLength(Poses);
        writer.WriteBytesWithLength(Values);
    }

    internal static OwnershipStateMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        NetSerializers.ReadFloat3(ref reader),
        reader.ReadBytesWithLength(),
        reader.ReadBytesWithLength());
}

internal readonly record struct HandoffPoseMessage(Guid Identifier, Float3 Position, Quaternion Rotation, Float3 Scale)
{
    internal const int Size = 16 + 12 + 16 + 12;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        NetSerializers.WriteFloat3(writer, Position);
        NetSerializers.WriteQuaternion(writer, Rotation);
        NetSerializers.WriteFloat3(writer, Scale);
    }

    internal static HandoffPoseMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        NetSerializers.ReadFloat3(ref reader),
        NetSerializers.ReadQuaternion(ref reader),
        NetSerializers.ReadFloat3(ref reader));
}

/// <summary>Count followed by Count HandoffPoseMessage records.</summary>
internal readonly record struct HandoffPoseBlock(uint Count)
{
    internal void Write(NetWriter writer) => writer.WriteVarUInt32(Count);

    internal static HandoffPoseBlock Read(ref NetReader reader) => new(
        reader.ReadVarUInt32());
}
