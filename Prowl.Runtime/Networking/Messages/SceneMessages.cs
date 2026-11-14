// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Snapshot header, whose sections hold every section of the world, followed by an ObjectStateBlock and a SyncBlock.</summary>
internal readonly ref struct SceneSnapshotMessage(
    byte format,
    ulong generation,
    ReadOnlySpan<byte> sections)
{
    internal const byte CurrentFormat = 12;
    internal readonly byte Format = format;
    internal readonly ulong Generation = generation;
    internal readonly ReadOnlySpan<byte> Sections = sections;

    internal void Write(NetWriter writer)
    {
        writer.WriteByte(Format);
        writer.WriteUInt64(Generation);
        writer.WriteBytesWithLength(Sections);
    }

    internal static SceneSnapshotMessage Read(ref NetReader reader)
    {
        byte format = reader.ReadByte();
        NetworkValidation.SnapshotFormat(format);
        ulong generation = reader.ReadUInt64();
        NetworkValidation.SceneGeneration(generation);
        return new SceneSnapshotMessage(format, generation, reader.ReadBytesWithLength());
    }
}

/// <summary>A section the host loaded additively, followed by an ObjectStateBlock and a SyncBlock.</summary>
internal readonly ref struct SectionAddMessage(ulong generation, ReadOnlySpan<byte> sections)
{
    internal readonly ulong Generation = generation;
    internal readonly ReadOnlySpan<byte> Sections = sections;

    internal void Write(NetWriter writer)
    {
        writer.WriteUInt64(Generation);
        writer.WriteBytesWithLength(Sections);
    }

    internal static SectionAddMessage Read(ref NetReader reader) => new(reader.ReadUInt64(), reader.ReadBytesWithLength());
}

/// <summary>A section the host unloaded.</summary>
internal readonly record struct SectionRemoveMessage(ulong Generation, uint SectionId)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteUInt64(Generation);
        writer.WriteVarUInt32(SectionId);
    }

    internal static SectionRemoveMessage Read(ref NetReader reader) => new(reader.ReadUInt64(), reader.ReadVarUInt32());
}

internal readonly record struct SnapshotObjectMessage(
    Guid Identifier,
    int OwnerId,
    NetworkOrphaned Orphaned,
    uint Generation,
    uint NetId)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteVarUInt32((uint)(OwnerId + 1));
        writer.WriteByte((byte)Orphaned);
        writer.WriteUInt32(Generation);
        writer.WriteVarUInt32(NetId);
    }

    internal static SnapshotObjectMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        (int)reader.ReadVarUInt32() - 1,
        NetworkValidation.Orphaned((NetworkOrphaned)reader.ReadByte()),
        reader.ReadUInt32(),
        NetworkValidation.NetId(reader.ReadVarUInt32()));
}

/// <summary>A networked root the host moved into another section.</summary>
internal readonly record struct SectionMoveMessage(Guid Identifier, uint SectionId)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteVarUInt32(SectionId);
    }

    internal static SectionMoveMessage Read(ref NetReader reader) => new(reader.ReadGuid(), reader.ReadVarUInt32());
}

/// <summary>SceneLoaded, SceneReady and SceneActivate acknowledgements carry only this generation.</summary>
internal readonly record struct SceneGenerationMessage(ulong Generation)
{
    internal void Write(NetWriter writer) => writer.WriteUInt64(Generation);

    internal static SceneGenerationMessage Read(ref NetReader reader) => new(
        reader.ReadUInt64());
}

/// <summary>Catch-up header followed by a SyncBlock and a CatchUpTransformBlock.</summary>
internal readonly record struct SceneCatchUpMessage(ulong Generation)
{
    internal void Write(NetWriter writer) => writer.WriteUInt64(Generation);

    internal static SceneCatchUpMessage Read(ref NetReader reader) => new(
        reader.ReadUInt64());
}

internal readonly record struct CatchUpTransformMessage(
    uint NetId,
    uint Generation,
    uint Tick,
    Float3 Position,
    Quaternion Rotation,
    Float3 Scale)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(NetId);
        writer.WriteVarUInt32(Generation);
        writer.WriteVarUInt32(Tick);
        NetSerializers.WriteFloat3(writer, Position);
        NetSerializers.WriteQuaternion(writer, Rotation);
        NetSerializers.WriteFloat3(writer, Scale);
    }

    internal static CatchUpTransformMessage Read(ref NetReader reader) => new(
        reader.ReadVarUInt32(),
        reader.ReadVarUInt32(),
        reader.ReadVarUInt32(),
        NetSerializers.ReadFloat3(ref reader),
        NetSerializers.ReadQuaternion(ref reader),
        NetSerializers.ReadFloat3(ref reader));
}

/// <summary>Count followed by Count SnapshotObjectMessage records.</summary>
internal readonly record struct ObjectStateBlock(uint Count)
{
    internal void Write(NetWriter writer) => writer.WriteVarUInt32(Count);

    internal static ObjectStateBlock Read(ref NetReader reader) => new(
        reader.ReadVarUInt32());
}

/// <summary>Count followed by Count CatchUpTransformMessage records.</summary>
internal readonly record struct CatchUpTransformBlock(uint Count)
{
    internal void Write(NetWriter writer) => writer.WriteVarUInt32(Count);

    internal static CatchUpTransformBlock Read(ref NetReader reader) => new(
        reader.ReadVarUInt32());
}
