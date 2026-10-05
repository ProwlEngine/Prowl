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
    /// Non-dynamic buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Size in bytes.</param>
    /// <param name="usage">Usage.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
    }
}
