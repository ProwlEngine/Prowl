namespace Prowl.Graphite;

/// <summary>
/// Type of a single field in a uniform block, for per-field layout in property-driven binding.
/// </summary>
public enum UniformScalarType : byte
{
    /// <summary>1 float.</summary>
    Float1,
    /// <summary>2 floats.</summary>
    Float2,
    /// <summary>3 floats.</summary>
    Float3,
    /// <summary>4 floats.</summary>
    Float4,

    /// <summary>1 int.</summary>
    Int1,
    /// <summary>2 ints.</summary>
    Int2,
    /// <summary>3 ints.</summary>
    Int3,
    /// <summary>4 ints.</summary>
    Int4,

    /// <summary>1 double.</summary>
    Double1,
    /// <summary>2 doubles.</summary>
    Double2,
    /// <summary>3 doubles.</summary>
    Double3,
    /// <summary>4 doubles.</summary>
    Double4,

    /// <summary>4x4 float matrix, column-major.</summary>
    Float4x4,
    /// <summary>4x4 double matrix, column-major.</summary>
    Double4x4,
}
