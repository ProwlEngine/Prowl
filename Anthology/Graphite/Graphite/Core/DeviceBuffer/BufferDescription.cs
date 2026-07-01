using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a <see cref="DeviceBuffer"/>, used in the creation of <see cref="DeviceBuffer"/> objects by a
/// <see cref="ResourceFactory"/>.
/// </summary>
public struct BufferDescription : IEquatable<BufferDescription>
{
    /// <summary>
    /// The desired capacity, in bytes, of the <see cref="DeviceBuffer"/>.
    /// </summary>
    public uint SizeInBytes;
    /// <summary>
    /// Indicates how the <see cref="DeviceBuffer"/> will be used.
    /// </summary>
    public BufferUsage Usage;
    /// <summary>
    /// For structured buffers, this value indicates the size in bytes of a single structure element, and must be non-zero.
    /// For all other buffer types, this value must be zero.
    /// </summary>
    public uint StructureByteStride;
    /// <summary>
    /// Controls how a structured buffer is bound on HLSL-based backends (D3D11). Only meaningful when
    /// <see cref="Usage"/> includes <see cref="BufferUsage.StructuredBufferReadOnly"/> or
    /// <see cref="BufferUsage.StructuredBufferReadWrite"/>. When true, binds as a typed
    /// <c>(RW)StructuredBuffer&lt;T&gt;</c>; use this when binding hand-written HLSL that declares its
    /// storage buffers with those types. When false (default), binds as a raw <c>(RW)ByteAddressBuffer</c>.
    /// Has no effect on non-HLSL backends.
    /// </summary>
    public bool UseTypedHlslBinding;

    /// <summary>
    /// When true, this <see cref="DeviceBuffer"/> opts out of in-flight write-hazard tracking. Writing to the buffer
    /// (via <see cref="GraphicsDevice.Map(MappableResource, MapMode)"/> or
    /// <see cref="GraphicsDevice.UpdateBuffer(DeviceBuffer, uint, IntPtr, uint)"/>) while a previous frame may still
    /// be reading it will no longer be reported as an error. This accepts a possible one-frame tear in exchange for
    /// being able to update a single buffer in place. Use this for buffers that are only updated occasionally and
    /// where a brief flicker is acceptable; prefer a <see cref="StreamingBuffer"/> when tearing is not acceptable.
    /// Has no effect in builds without usage validation.
    /// </summary>
    public bool TransientWrites;

    /// <summary>
    /// Constructs a new <see cref="BufferDescription"/> describing a non-dynamic <see cref="DeviceBuffer"/>.
    /// </summary>
    /// <param name="sizeInBytes">The desired capacity, in bytes.</param>
    /// <param name="usage">Indicates how the <see cref="DeviceBuffer"/> will be used.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = 0;
        UseTypedHlslBinding = false;
        TransientWrites = false;
    }

    /// <summary>
    /// Constructs a new <see cref="BufferDescription"/>.
    /// </summary>
    /// <param name="sizeInBytes">The desired capacity, in bytes.</param>
    /// <param name="usage">Indicates how the <see cref="DeviceBuffer"/> will be used.</param>
    /// <param name="structureByteStride">For structured buffers, this value indicates the size in bytes of a single
    /// structure element, and must be non-zero. For all other buffer types, this value must be zero.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        UseTypedHlslBinding = false;
        TransientWrites = false;
    }

    /// <summary>
    /// Constructs a new <see cref="BufferDescription"/>.
    /// </summary>
    /// <param name="sizeInBytes">The desired capacity, in bytes.</param>
    /// <param name="usage">Indicates how the <see cref="DeviceBuffer"/> will be used.</param>
    /// <param name="structureByteStride">For structured buffers, this value indicates the size in bytes of a single
    /// structure element, and must be non-zero. For all other buffer types, this value must be zero.</param>
    /// <param name="useTypedHlslBinding">Controls how a structured buffer is bound on HLSL-based backends (D3D11).
    /// Only meaningful when <paramref name="usage"/> includes <see cref="BufferUsage.StructuredBufferReadOnly"/> or
    /// <see cref="BufferUsage.StructuredBufferReadWrite"/>. When true, binds as a typed
    /// <c>(RW)StructuredBuffer&lt;T&gt;</c>; use this when binding hand-written HLSL that declares its storage
    /// buffers with those types. When false, binds as a raw <c>(RW)ByteAddressBuffer</c>. Has no effect on
    /// non-HLSL backends.</param>
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
    /// <param name="other">The instance to compare to.</param>
    /// <returns>True if all elements are equal; false otherswise.</returns>
    public readonly bool Equals(BufferDescription other)
    {
        return SizeInBytes.Equals(other.SizeInBytes)
            && Usage == other.Usage
            && StructureByteStride.Equals(other.StructureByteStride)
            && UseTypedHlslBinding.Equals(other.UseTypedHlslBinding)
            && TransientWrites.Equals(other.TransientWrites);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
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
