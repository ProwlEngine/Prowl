using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a buffer for creation via ResourceFactory.
/// </summary>
public struct BufferDescription : IEquatable<BufferDescription>
{
    /// <summary>
    /// Buffer capacity in bytes.
    /// </summary>
    public uint SizeInBytes;
    /// <summary>
    /// How the buffer will be used.
    /// </summary>
    public BufferUsage Usage;
    /// <summary>
    /// Structured buffers: element size in bytes, must be nonzero. Other buffers: must be zero.
    /// </summary>
    public uint StructureByteStride;
    /// <summary>
    /// HLSL-only. Only matters for structured buffer usage. True binds as typed (RW)StructuredBuffer&lt;T&gt;
    /// for hand-written HLSL. False (default) binds as raw (RW)ByteAddressBuffer. No effect elsewhere.
    /// </summary>
    public bool UseTypedHlslBinding;

    /// <summary>
    /// True skips write-hazard tracking for this buffer. Writing while a previous frame might still be
    /// reading won't error. Trades a possible one-frame tear for in-place updates. Use for buffers updated
    /// occasionally where flicker's fine; use a transient buffer per execution if tearing's not acceptable.
    /// No effect without usage validation builds.
    /// </summary>
    public bool TransientWrites;

    /// <summary>
    /// Makes a non-dynamic buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Capacity in bytes.</param>
    /// <param name="usage">Buffer usage.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = 0;
        UseTypedHlslBinding = false;
        TransientWrites = false;
    }

    /// <summary>
    /// Makes a buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Capacity in bytes.</param>
    /// <param name="usage">Buffer usage.</param>
    /// <param name="structureByteStride">Structured buffers: element size in bytes, must be nonzero. Otherwise zero.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        UseTypedHlslBinding = false;
        TransientWrites = false;
    }

    /// <summary>
    /// Makes a buffer description.
    /// </summary>
    /// <param name="sizeInBytes">Capacity in bytes.</param>
    /// <param name="usage">Buffer usage.</param>
    /// <param name="structureByteStride">Structured buffers: element size in bytes, must be nonzero. Otherwise zero.</param>
    /// <param name="useTypedHlslBinding">HLSL only, structured buffer usage only. True binds typed
    /// (RW)StructuredBuffer&lt;T&gt; for hand-written HLSL. False binds raw (RW)ByteAddressBuffer.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool useTypedHlslBinding)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        UseTypedHlslBinding = useTypedHlslBinding;
        TransientWrites = false;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
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
    /// Hash code for this instance.
    /// </summary>
    /// <returns>32-bit hash.</returns>
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
