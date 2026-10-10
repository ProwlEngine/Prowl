using System;

namespace Prowl.Graphite.Debugging;

/// <summary>Where a capture copy runs relative to the pass it was requested for.</summary>
public enum CopyPlacement : byte
{
    BeforePass,
    AfterPass,
}

/// <summary>A copy recorded into a caller-owned staging buffer. Map and dispose the buffer after the execution resolves.</summary>
public readonly record struct CaptureCopy(DeviceBuffer Staging, ReadOnlyMemory<CopyRegion> Regions);

/// <summary>One tightly packed mip, layer, and aspect of a texture copy inside the staging buffer.</summary>
public readonly record struct CopyRegion(
    uint MipLevel,
    uint ArrayLayer,
    ulong Offset,
    uint Width,
    uint Height,
    uint Depth,
    PixelFormat Format);
