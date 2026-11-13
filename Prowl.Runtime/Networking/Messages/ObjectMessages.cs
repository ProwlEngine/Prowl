// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Spawn header followed by a SyncBlock of initial component values. A root goes into section SectionSlot minus one, or the active one for 0.</summary>
internal readonly ref struct SpawnMessage(
    Guid identifier,
    Guid parentId,
    uint sectionSlot,
    int ownerId,
    NetworkOrphaned orphaned,
    uint generation,
    uint netId,
    ReadOnlySpan<byte> snapshot,
    bool replace = false,
    uint tick = 0)
{
    // Set in the orphaned byte when the object replaces any copy the client still has. The host tick then follows NetId.
    private const byte ReplaceBit = 0x80;

    internal readonly Guid Identifier = identifier;
    internal readonly Guid ParentId = parentId;
    internal readonly uint SectionSlot = sectionSlot;
    internal readonly int OwnerId = ownerId;
    internal readonly NetworkOrphaned Orphaned = orphaned;
    internal readonly uint Generation = generation;
    internal readonly uint NetId = netId;
    internal readonly ReadOnlySpan<byte> Snapshot = snapshot;
    internal readonly bool Replace = replace;
    internal readonly uint Tick = tick;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteGuid(ParentId);
        writer.WriteVarUInt32(SectionSlot);
        writer.WriteVarUInt32((uint)(OwnerId + 1));
        writer.WriteByte((byte)((byte)Orphaned | (Replace ? ReplaceBit : 0)));
        writer.WriteUInt32(Generation);
        writer.WriteVarUInt32(NetId);
        if (Replace)
        {
            writer.WriteVarUInt32(Tick);
        }

        writer.WriteBytesWithLength(Snapshot);
    }

    internal static SpawnMessage Read(ref NetReader reader)
    {
        Guid identifier = reader.ReadGuid(), parentId = reader.ReadGuid();
        uint sectionSlot = reader.ReadVarUInt32();
        int ownerId = (int)reader.ReadVarUInt32() - 1;
        byte policy = reader.ReadByte();
        uint generation = reader.ReadUInt32();
        uint netId = NetworkValidation.NetId(reader.ReadVarUInt32());
        bool replace = (policy & ReplaceBit) != 0;
        uint tick = replace ? reader.ReadVarUInt32() : 0;
        return new SpawnMessage(identifier, parentId, sectionSlot, ownerId,
            NetworkValidation.Orphaned((NetworkOrphaned)(policy & ~ReplaceBit)), generation, netId,
            reader.ReadBytesWithLength(), replace, tick);
    }
}

/// <summary>A despawn, with its reason only when it is not <see cref="NetworkDespawnReason.Destroyed" />.</summary>
internal readonly record struct DespawnMessage(Guid Identifier, NetworkDespawnReason Reason = NetworkDespawnReason.Destroyed)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        if (Reason != NetworkDespawnReason.Destroyed)
        {
            writer.WriteByte((byte)Reason);
        }
    }

    internal static DespawnMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.IsAtEnd ? NetworkDespawnReason.Destroyed : NetworkValidation.DespawnReason((NetworkDespawnReason)reader.ReadByte()));
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
