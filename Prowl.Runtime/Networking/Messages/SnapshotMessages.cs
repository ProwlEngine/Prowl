// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Starts a Snapshot message: then a TransformGroupBlock of groups, then the values (count, then SnapshotValue
///     records). The id is the sender's own count for this receiver, which acknowledges it; zero asks for no
///     acknowledgement. The epoch is the scene generation it was sent in; one from another scene is dropped, since its
///     component ids belonged to that scene. Values are ordered by <see cref="Tick" />, the sender's.
/// </summary>
internal readonly record struct SnapshotMessage(uint Id, uint Epoch, uint Tick)
{
    internal const int HeaderSize = 5 + 5 + 5;

    /// <summary>The smallest message that still holds one whole transform record.</summary>
    internal const int MinMessageSize = sizeof(ushort) + HeaderSize + 1 + TransformGroup.HeaderSize +
                                        TransformRecord.MaxSize + 1;

    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(Id);
        writer.WriteVarUInt32(Epoch);
        writer.WriteVarUInt32(Tick);
    }

    internal static SnapshotMessage Read(ref NetReader reader) => new(
        reader.ReadVarUInt32(),
        reader.ReadVarUInt32(),
        reader.ReadVarUInt32());
}

/// <summary>
///     One [Sync] value in a snapshot: [varuint key][guid, if the key's low bit is set][varuint slot][varuint
///     generation][bytes value, length prefixed]. The key's other bits are a small id the sender gave the component for
///     this receiver; the guid defines it, and goes along until the receiver has acknowledged a snapshot carrying it.
/// </summary>
internal readonly ref struct SnapshotValue(uint componentId, Guid? definition, uint slot, uint generation,
    ReadOnlySpan<byte> value)
{
    internal const int MinimumSize = 1 + 1 + 1 + 1;

    internal readonly uint ComponentId = componentId;
    internal readonly Guid? Definition = definition;
    internal readonly uint Slot = slot;
    internal readonly uint Generation = generation;
    internal readonly ReadOnlySpan<byte> Value = value;

    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(ComponentId << 1 | (Definition.HasValue ? 1u : 0u));
        if (Definition is { } guid)
        {
            writer.WriteGuid(guid);
        }

        writer.WriteVarUInt32(Slot);
        writer.WriteVarUInt32(Generation);
        writer.WriteBytesWithLength(Value);
    }

    internal static SnapshotValue Read(ref NetReader reader)
    {
        uint key = reader.ReadVarUInt32();
        Guid? definition = (key & 1) != 0 ? reader.ReadGuid() : null;
        return new SnapshotValue(key >> 1, definition, reader.ReadVarUInt32(), reader.ReadVarUInt32(),
            reader.ReadBytesWithLength());
    }
}

/// <summary>Count followed by Count TransformGroup records.</summary>
internal readonly record struct TransformGroupBlock(uint Count)
{
    // A group's smallest encoding: one-byte tick, time, one-byte count.
    private const int HeaderBytes = 1 + 8 + 1;

    internal void Write(NetWriter writer) => writer.WriteVarUInt32(Count);

    internal static TransformGroupBlock Read(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining / HeaderBytes)
        {
            throw new InvalidDataException("Transform group count exceeds the remaining data.");
        }

        return new TransformGroupBlock(count);
    }
}

/// <summary>
///     Transform records from one controller's tick, at its clock: [varuint tick][f64 sent at][varuint count] then
///     Count TransformRecord values. The host relays a client's records under that client's tick and clock.
/// </summary>
internal readonly record struct TransformGroup(uint Tick, double SentAt, uint Count)
{
    internal const int HeaderSize = 5 + 8 + 5;

    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(Tick);
        writer.WriteDouble(SentAt);
        writer.WriteVarUInt32(Count);
    }

    internal static TransformGroup Read(ref NetReader reader)
    {
        uint tick = reader.ReadVarUInt32();
        double sentAt = reader.ReadDouble();
        uint count = reader.ReadVarUInt32();
        if (!double.IsFinite(sentAt) || sentAt < 0)
        {
            throw new InvalidDataException("A transform group has an invalid time.");
        }

        if (count > (uint)reader.Remaining / (5 + 5 + 1))
        {
            throw new InvalidDataException("Transform record count exceeds the remaining data.");
        }

        return new TransformGroup(tick, sentAt, count);
    }
}

/// <summary>
///     Acknowledges Snapshot ids: [varuint count] ids, then the components ([varuint count] guids) and objects
///     ([varuint count] net ids) whose state in them couldn't be applied, so the sender keeps it pending.
/// </summary>
internal static class SnapshotAckMessage
{
    internal static void WriteIds(NetWriter writer, ReadOnlySpan<uint> ids)
    {
        writer.WriteVarUInt32((uint)ids.Length);
        foreach (uint id in ids)
        {
            writer.WriteVarUInt32(id);
        }
    }

    internal static uint ReadCount(ref NetReader reader, int minimumItemSize)
    {
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining / (uint)minimumItemSize)
        {
            throw new InvalidDataException("Snapshot acknowledgement count exceeds the remaining data.");
        }

        return count;
    }
}
