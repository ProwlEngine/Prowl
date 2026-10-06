using System;

namespace Prowl.Graphite.Debugger.Trace;

public readonly record struct TraceResourceId(uint Value);

public readonly record struct TraceProgramId(uint Value);

public readonly record struct TraceVersion(TraceResourceId Resource, uint Version);

public readonly record struct BlobRef(EquatableArray<byte> Hash, ulong Length);

[Flags]
public enum TraceResourceUsage : ushort
{
    None = 0,
    Vertex = 1 << 0,
    Index = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    Sampled = 1 << 4,
    Attachment = 1 << 5,
    CopySource = 1 << 6,
    CopyDestination = 1 << 7,
    Indirect = 1 << 8,
}

public readonly record struct TraceRange(
    ulong Offset,
    ulong Size,
    uint BaseMipLevel,
    uint MipLevels,
    uint BaseArrayLayer,
    uint ArrayLayers)
{
    public static TraceRange Bytes(ulong offset, ulong size) => new(offset, size, 0, 0, 0, 0);

    public static TraceRange Subresources(uint baseMip, uint mips, uint baseLayer, uint layers) => new(0, 0, baseMip, mips, baseLayer, layers);
}

public readonly record struct TraceUse(
    TraceResourceId Resource,
    uint Version,
    TraceRange Range,
    TraceResourceUsage Usage);

public enum SnapshotState : byte
{
    Captured,
    Skipped,
}

public readonly record struct SnapshotRef(
    TraceResourceId Resource,
    uint Version,
    SnapshotState State,
    BlobRef? Data);
