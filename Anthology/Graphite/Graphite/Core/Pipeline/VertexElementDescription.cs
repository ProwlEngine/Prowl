using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a single element of a vertex.
/// </summary>
public struct VertexElementDescription : IEquatable<VertexElementDescription>
{
    /// <summary>
    /// The user-facing interned name of the element. For attributes reflected from a shader this is
    /// the <i>blended</i> HLSL semantic (semantic name plus index, e.g. <c>UV0</c>), so lookups are
    /// stable across backends. HLSL-based backends bind with <see cref="HlslSemanticName"/> and the
    /// element's location rather than this name. Implicit conversion from <see cref="string"/> is supported.
    /// </summary>
    public VertexAttributeID Name;

    /// <summary>
    /// The format of the element.
    /// </summary>
    public VertexElementFormat Format;

    /// <summary>
    /// The offset in bytes from the beginning of the vertex.
    /// </summary>
    public uint Offset;

    /// <summary>
    /// The raw HLSL semantic name with no index (e.g. <c>UV</c>), used by HLSL-based backends as the
    /// <c>SemanticName</c>; the semantic index comes from the element's location. Mirrors how
    /// <see cref="ResourceLayoutElementDescription.GLUniformName"/> carries the in-shader GL name.
    /// Defaults to the name the element was constructed with.
    /// </summary>
    public string HlslSemanticName;

    /// <summary>
    /// Constructs a new VertexElementDescription describing a per-vertex element.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format)
    {
        Name = name;
        Format = format;
        Offset = 0;
        HlslSemanticName = name;
    }

    /// <summary>
    /// Constructs a new VertexElementDescription.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format, uint offset)
    {
        Name = name;
        Format = format;
        Offset = offset;
        HlslSemanticName = name;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public readonly bool Equals(VertexElementDescription other)
    {
        return Name == other.Name
            && Format == other.Format
            && Offset == other.Offset
            && string.Equals(HlslSemanticName, other.HlslSemanticName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            Name,
            (int)Format,
            (int)Offset,
            HlslSemanticName != null ? StringComparer.Ordinal.GetHashCode(HlslSemanticName) : 0);
    }
}
