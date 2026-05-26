namespace Prowl.Veldrid;

/// <summary>
/// Scalar, vector, or matrix type of a single field inside a uniform block. Used by
/// <see cref="UniformBlockField"/> to describe per-field layout for property-driven binding.
/// </summary>
public enum UniformScalarType : byte
{
    /// <summary>Single 32-bit float.</summary>
    Float1,
    /// <summary>Two 32-bit floats.</summary>
    Float2,
    /// <summary>Three 32-bit floats.</summary>
    Float3,
    /// <summary>Four 32-bit floats.</summary>
    Float4,

    /// <summary>Single 32-bit int.</summary>
    Int1,
    /// <summary>Two 32-bit ints.</summary>
    Int2,
    /// <summary>Three 32-bit ints.</summary>
    Int3,
    /// <summary>Four 32-bit ints.</summary>
    Int4,

    /// <summary>Single 64-bit double.</summary>
    Double1,
    /// <summary>Two 64-bit doubles.</summary>
    Double2,
    /// <summary>Three 64-bit doubles.</summary>
    Double3,
    /// <summary>Four 64-bit doubles.</summary>
    Double4,

    /// <summary>4x4 column-major matrix of 32-bit floats.</summary>
    Float4x4,
    /// <summary>4x4 column-major matrix of 64-bit doubles.</summary>
    Double4x4,
}
