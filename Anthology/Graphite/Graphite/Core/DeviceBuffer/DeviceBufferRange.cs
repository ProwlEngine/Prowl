using System;

namespace Prowl.Graphite;

/// <summary>
/// Slice of a buffer; bind via PropertySet to expose part to shaders.
/// </summary>
public struct DeviceBufferRange
{
    /// <summary>
    /// Buffer this range points into.
    /// </summary>
    public DeviceBuffer Buffer;
    /// <summary>
    /// Byte offset from the start of the buffer.
    /// </summary>
    public uint Offset;
    /// <summary>
    /// Size of the range in bytes.
    /// </summary>
    public uint SizeInBytes;

    /// <summary>
    /// True if this range covers the whole buffer.
    /// </summary>
    public readonly bool IsFullRange => Offset == 0 && SizeInBytes == Buffer.SizeInBytes;

    /// <summary>
    /// New DeviceBufferRange.
    /// </summary>
    /// <param name="buffer">Buffer to slice.</param>
    /// <param name="offset">Byte offset into the buffer.</param>
    /// <param name="sizeInBytes">Size of the range in bytes.</param>
    public DeviceBufferRange(DeviceBuffer buffer, uint offset, uint sizeInBytes)
    {
        Buffer = buffer;
        Offset = offset;
        SizeInBytes = sizeInBytes;
    }
}
