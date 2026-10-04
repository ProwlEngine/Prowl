using System;

namespace Prowl.Graphite;

/// <summary>A field in a uniform block, written by PropertySet via Offset and Type.</summary>
public record struct UniformBlockField
{
    /// <summary>Interned field name, implicitly converts from string.</summary>
    public PropertyID Name;

    /// <summary>Byte offset in the buffer.</summary>
    public uint Offset;

    /// <summary>Byte size matching Type.</summary>
    public uint Size;

    /// <summary>Scalar type used for writes.</summary>
    public UniformScalarType Type;

    /// <summary>Creates a field with an interned name.</summary>
    public UniformBlockField(PropertyID name, uint offset, uint size, UniformScalarType type)
    {
        Name = name;
        Offset = offset;
        Size = size;
        Type = type;
    }

    /// <summary>Creates a field, interns name implicitly.</summary>
    public UniformBlockField(string name, uint offset, uint size, UniformScalarType type)
        : this((PropertyID)name, offset, size, type)
    {
    }
}
