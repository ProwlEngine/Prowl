using System;

namespace Prowl.Graphite;

/// <summary>
/// One scalar/vector/matrix field in a uniform block. PropertySet writes into the buffer at Offset using a writer keyed by Type.
/// </summary>
public readonly struct UniformBlockField : IEquatable<UniformBlockField>
{
    /// <summary>Interned field name. Implicitly converts from string.</summary>
    public readonly PropertyID Name;

    /// <summary>Byte offset in the uniform buffer.</summary>
    public readonly uint Offset;

    /// <summary>Byte size, must match Type's natural size.</summary>
    public readonly uint Size;

    /// <summary>Scalar type for writes.</summary>
    public readonly UniformScalarType Type;

    /// <summary>Builds a field with an interned name.</summary>
    public UniformBlockField(PropertyID name, uint offset, uint size, UniformScalarType type)
    {
        Name = name;
        Offset = offset;
        Size = size;
        Type = type;
    }

    /// <summary>Interns name implicitly.</summary>
    public UniformBlockField(string name, uint offset, uint size, UniformScalarType type)
        : this((PropertyID)name, offset, size, type)
    {
    }

    /// <inheritdoc/>
    public bool Equals(UniformBlockField other)
        => Name == other.Name && Offset == other.Offset && Size == other.Size && Type == other.Type;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is UniformBlockField o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Name, Offset, Size, (int)Type);

    /// <inheritdoc/>
    public static bool operator ==(UniformBlockField a, UniformBlockField b)
        => a.Equals(b);

    /// <inheritdoc/>
    public static bool operator !=(UniformBlockField a, UniformBlockField b)
        => !a.Equals(b);
}
