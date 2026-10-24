// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Networking.Serialization;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking.Messages;

internal readonly ref struct OwnershipRequestMessage(
    Guid identifier,
    uint generation,
    bool take,
    Float3 velocity,
    ReadOnlySpan<byte> poses)
{
    internal readonly Guid Identifier = identifier;
    internal readonly uint Generation = generation;
    internal readonly bool Take = take;
    internal readonly Float3 Velocity = velocity;
    internal readonly ReadOnlySpan<byte> Poses = poses;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteBool(Take);
        NetSerializers.WriteFloat3(writer, Velocity);
        writer.WriteBytesWithLength(Poses);
    }

    internal static OwnershipRequestMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        reader.ReadBool(),
        NetSerializers.ReadFloat3(ref reader),
        reader.ReadBytesWithLength());
}

internal readonly ref struct OwnershipStateMessage(
    Guid identifier,
    uint generation,
    Float3 velocity,
    ReadOnlySpan<byte> poses)
{
    internal readonly Guid Identifier = identifier;
    internal readonly uint Generation = generation;
    internal readonly Float3 Velocity = velocity;
    internal readonly ReadOnlySpan<byte> Poses = poses;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        NetSerializers.WriteFloat3(writer, Velocity);
        writer.WriteBytesWithLength(Poses);
    }

    internal static OwnershipStateMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        NetSerializers.ReadFloat3(ref reader),
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
