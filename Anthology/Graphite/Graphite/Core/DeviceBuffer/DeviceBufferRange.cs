using System;

namespace Prowl.Graphite;

/// <summary>
/// A slice of a buffer. Bind it via PropertySet instead of the whole buffer to expose only part of it to shaders.
/// </summary>
public struct DeviceBufferRange : BindableResource, IEquatable<DeviceBufferRange>
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

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if everything matches.</returns>
    public readonly bool Equals(DeviceBufferRange other)
    {
        return Buffer == other.Buffer && Offset.Equals(other.Offset) && SizeInBytes.Equals(other.SizeInBytes);
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
    {
        int bufferHash = Buffer?.GetHashCode() ?? 0;
        return HashCode.Combine(bufferHash, Offset.GetHashCode(), SizeInBytes.GetHashCode());
    }
}
