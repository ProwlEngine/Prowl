// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A Snapshot message: this header, then [varuint count] TransformRecord values. The id is the sender's own count
///     for this receiver, which acknowledges it; zero asks for no acknowledgement. The epoch is the scene generation
///     it was sent in; one from another scene is dropped. <see cref="Tick" /> and <see cref="Time" /> are the
///     sender's tick and clock as it sends, which each record counts back from.
/// </summary>
internal readonly record struct SnapshotMessage(uint Id, uint Epoch, uint Tick, double Time)
{
    internal const int HeaderSize = 5 + 5 + 5 + 8;

    /// <summary>The smallest message that still holds one whole transform record.</summary>
    internal const int MinMessageSize = sizeof(ushort) + HeaderSize + 1 + TransformRecord.MaxSize;

    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(Id);
        writer.WriteVarUInt32(Epoch);
        writer.WriteVarUInt32(Tick);
        writer.WriteDouble(Time);
    }

    internal static SnapshotMessage Read(ref NetReader reader)
    {
        SnapshotMessage message = new(reader.ReadVarUInt32(), reader.ReadVarUInt32(), reader.ReadVarUInt32(),
            reader.ReadDouble());
        if (!double.IsFinite(message.Time) || message.Time < 0)
        {
            throw new InvalidDataException("A snapshot has an invalid time.");
        }

        return message;
    }

    /// <summary>How many records follow the header.</summary>
    internal static uint ReadRecordCount(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining / (1 + 1 + 1))
        {
            throw new InvalidDataException("Transform record count exceeds the remaining data.");
        }

        return count;
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
