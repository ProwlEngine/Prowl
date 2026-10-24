// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Utilities;
using Prowl.Warp;

namespace Prowl.Runtime.Networking.Messages;

/// <summary>Spawn header followed by a SyncBlock of initial component values.</summary>
internal readonly ref struct SpawnMessage(
    Guid identifier,
    Guid parentId,
    int ownerId,
    NetworkOrphaned orphaned,
    uint generation,
    uint netId,
    ReadOnlySpan<byte> snapshot)
{
    internal readonly Guid Identifier = identifier;
    internal readonly Guid ParentId = parentId;
    internal readonly int OwnerId = ownerId;
    internal readonly NetworkOrphaned Orphaned = orphaned;
    internal readonly uint Generation = generation;
    internal readonly uint NetId = netId;
    internal readonly ReadOnlySpan<byte> Snapshot = snapshot;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteGuid(ParentId);
        writer.WriteVarUInt32((uint)(OwnerId + 1));
        writer.WriteByte((byte)Orphaned);
        writer.WriteUInt32(Generation);
        writer.WriteVarUInt32(NetId);
        writer.WriteBytesWithLength(Snapshot);
    }

    internal static SpawnMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadGuid(),
        (int)reader.ReadVarUInt32() - 1,
        NetworkValidation.Orphaned((NetworkOrphaned)reader.ReadByte()),
        reader.ReadUInt32(),
        NetworkValidation.NetId(reader.ReadVarUInt32()),
        reader.ReadBytesWithLength());
}

internal readonly record struct DespawnMessage(Guid Identifier)
{
    internal void Write(NetWriter writer) => writer.WriteGuid(Identifier);

    internal static DespawnMessage Read(ref NetReader reader) => new(
        reader.ReadGuid());
}

internal readonly record struct OwnerMessage(Guid Identifier, int OwnerId, uint Generation)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteVarUInt32((uint)(OwnerId + 1));
        writer.WriteUInt32(Generation);
    }

    internal static OwnerMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        (int)reader.ReadVarUInt32() - 1,
        reader.ReadUInt32());
}

internal readonly record struct OrphanedMessage(Guid Identifier, uint Generation, NetworkOrphaned Orphaned)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteByte((byte)Orphaned);
    }

    internal static OrphanedMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        NetworkValidation.Orphaned((NetworkOrphaned)reader.ReadByte()));
}

internal readonly record struct OwnerTransferMessage(Guid Identifier, uint Generation, OwnerTransfer Policy)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteByte((byte)Policy);
    }

    internal static OwnerTransferMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        (OwnerTransfer)reader.ReadByte());
}

internal readonly record struct NetworkFlagsMessage(Guid Identifier, uint Generation, NetworkFlags Flags)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteByte((byte)Flags);
    }

    internal static NetworkFlagsMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        (NetworkFlags)reader.ReadByte());
}
