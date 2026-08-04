using System;

namespace Prowl.Graphite;

/// <summary>
/// Buffer creation params.
/// </summary>
public struct BufferDescription : IEquatable<BufferDescription>
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
    /// HLSL structured buffers only. True = typed binding, false = raw ByteAddressBuffer.
    /// </summary>
    public bool UseTypedHlslBinding;

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
        UseTypedHlslBinding = false;
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
        UseTypedHlslBinding = false;
        TransientWrites = false;
    }

    /// <summary>
    /// Buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Size in bytes.</param>
    /// <param name="usage">Usage.</param>
    /// <param name="structureByteStride">Element size for structured buffers, else zero.</param>
    /// <param name="useTypedHlslBinding">HLSL structured buffers only. True = typed binding, false = raw.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool useTypedHlslBinding)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        UseTypedHlslBinding = useTypedHlslBinding;
        TransientWrites = false;
    }

    /// <summary>
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Other instance.</param>
    /// <returns>True if all fields match.</returns>
    public readonly bool Equals(BufferDescription other)
    {
        return SizeInBytes.Equals(other.SizeInBytes)
            && Usage == other.Usage
            && StructureByteStride.Equals(other.StructureByteStride)
            && UseTypedHlslBinding.Equals(other.UseTypedHlslBinding)
            && TransientWrites.Equals(other.TransientWrites);
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            SizeInBytes.GetHashCode(),
            (int)Usage,
            StructureByteStride.GetHashCode(),
            UseTypedHlslBinding.GetHashCode(),
            TransientWrites.GetHashCode());
    }
}
