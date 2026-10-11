using System;

namespace Prowl.Graphite;

/// <summary>A field in a uniform block, written by PropertySet via Offset and Type.</summary>
public record struct UniformBlockField
{
    /// <summary>Interned field name, implicitly converts from string.</summary>
    public PropertyID Name;

    /// <summary>Byte offset in the buffer.</summary>
    public uint Offset;

    /// <summary>Byte size matching Type, or the whole footprint of an array or a padded matrix.</summary>
    public uint Size;

    /// <summary>Scalar type used for writes, the element type of an array.</summary>
    public UniformScalarType Type;

    /// <summary>Element count of an array, 0 when not an array.</summary>
    public uint ArrayLength;

    /// <summary>Bytes between array elements, 0 when not an array.</summary>
    public uint ArrayStride;

    /// <summary>Creates a field with an interned name.</summary>
    public UniformBlockField(PropertyID name, uint offset, uint size, UniformScalarType type)
    {
        Name = name;
        Offset = offset;
        Size = size;
        Type = type;
    }

    /// <summary>Creates an array field, Size covering every element.</summary>
    public UniformBlockField(PropertyID name, uint offset, UniformScalarType type, uint arrayLength, uint arrayStride)
        : this(name, offset, arrayLength * arrayStride, type)
    {
        ArrayLength = arrayLength;
        ArrayStride = arrayStride;
    }

    /// <summary>Creates a field, interns name implicitly.</summary>
    public UniformBlockField(string name, uint offset, uint size, UniformScalarType type)
        : this((PropertyID)name, offset, size, type)
    {
    }
}
