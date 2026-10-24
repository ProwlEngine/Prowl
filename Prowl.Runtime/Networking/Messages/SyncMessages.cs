// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Original writer followed by a SyncBlock; host relays preserve OriginId.</summary>
internal readonly record struct SyncMessage(int OriginId)
{
    internal void Write(NetWriter writer) => writer.WriteInt32(OriginId);

    internal static SyncMessage Read(ref NetReader reader) => new(
        reader.ReadInt32());
}

/// <summary>Count followed by Count SyncValue records. Used by Sync, snapshots, spawn and catch-up.</summary>
internal readonly record struct SyncBlock(uint Count)
{
    internal void Write(NetWriter writer) => writer.WriteVarUInt32(Count);

    internal static SyncBlock Read(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        NetworkValidation.SyncCount(count, reader.Remaining);
        return new SyncBlock(count);
    }
}

internal readonly ref struct SyncValue(Guid componentId, uint slot, uint generation, ReadOnlySpan<byte> value)
{
    internal const int MinimumSize = 16 + 1 + 1 + 1;
    internal readonly Guid ComponentId = componentId;
    internal readonly uint Slot = slot;
    internal readonly uint Generation = generation;
    internal readonly ReadOnlySpan<byte> Value = value;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(ComponentId);
        writer.WriteVarUInt32(Slot);
        writer.WriteVarUInt32(Generation);
        writer.WriteBytesWithLength(Value);
    }

    internal static SyncValue Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadVarUInt32(),
        reader.ReadVarUInt32(),
        reader.ReadBytesWithLength());
}
