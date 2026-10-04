using System;

namespace Prowl.Graphite;

/// <summary>
/// Buffer creation params.
/// </summary>
public record struct BufferDescription
{
    /// <summary>
    /// Size in bytes.
    /// </summary>
    public uint SizeInBytes;
    /// <summary>
    /// Buffer usage.
    /// </summary>
    public BufferUsage Usage;
    /// <summary>
    /// Element size for structured buffers, else zero.
    /// </summary>
    public uint StructureByteStride;
    /// <summary>
    /// Skips write-hazard tracking. Risks a torn frame for cheap in-place updates.
    /// </summary>
    public bool TransientWrites;

    /// <summary>
    /// Non-dynamic buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Size in bytes.</param>
    /// <param name="usage">Usage.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = 0;
        TransientWrites = false;
    }

    /// <summary>
    /// Buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Size in bytes.</param>
    /// <param name="usage">Usage.</param>
    /// <param name="structureByteStride">Element size for structured buffers, else zero.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        TransientWrites = false;
    }
}
