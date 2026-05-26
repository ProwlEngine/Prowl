using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a single scalar/vector/matrix field inside a uniform block. Stage 7's
/// <c>PropertySet</c> writes property values into a transient uniform buffer at <see cref="Offset"/>
/// using a writer keyed by <see cref="Type"/>.
/// </summary>
public readonly struct UniformBlockField : IEquatable<UniformBlockField>
{
    /// <summary>Interned name of the field. Implicit conversion from <see cref="string"/> is supported.</summary>
    public readonly PropertyID Name;

    /// <summary>Byte offset from the start of the containing uniform buffer.</summary>
    public readonly uint Offset;

    /// <summary>Byte size occupied by the field; must equal the natural size of <see cref="Type"/>.</summary>
    public readonly uint Size;

    /// <summary>Scalar type used to interpret writes into the field.</summary>
    public readonly UniformScalarType Type;

    /// <summary>Constructs a field with the supplied interned name.</summary>
    public UniformBlockField(PropertyID name, uint offset, uint size, UniformScalarType type)
    {
        Name = name;
        Offset = offset;
        Size = size;
        Type = type;
    }

    /// <summary>Convenience overload that interns <paramref name="name"/> implicitly.</summary>
    public UniformBlockField(string name, uint offset, uint size, UniformScalarType type)
        : this((PropertyID)name, offset, size, type)
    {
    }

    public bool Equals(UniformBlockField other)
        => Name == other.Name && Offset == other.Offset && Size == other.Size && Type == other.Type;

    public override bool Equals(object? obj) => obj is UniformBlockField o && Equals(o);

    public override int GetHashCode() => HashCode.Combine(Name, Offset, Size, (int)Type);

    public static bool operator ==(UniformBlockField a, UniformBlockField b) => a.Equals(b);
    public static bool operator !=(UniformBlockField a, UniformBlockField b) => !a.Equals(b);
}
