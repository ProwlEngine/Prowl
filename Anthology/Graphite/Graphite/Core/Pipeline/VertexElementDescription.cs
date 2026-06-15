using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a single element of a vertex.
/// </summary>
public struct VertexElementDescription : IEquatable<VertexElementDescription>
{
    /// <summary>
    /// The interned name of the element. Used as the HLSL Semantic in the DX backend.
    /// Implicit conversion from <see cref="string"/> is supported.
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
    /// Constructs a new VertexElementDescription describing a per-vertex element.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format)
    {
        Name = name;
        Format = format;
        Offset = 0;
    }

    /// <summary>
    /// Constructs a new VertexElementDescription.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format, uint offset)
    {
        Name = name;
        Format = format;
        Offset = offset;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public readonly bool Equals(VertexElementDescription other)
    {
        return Name == other.Name
            && Format == other.Format
            && Offset == other.Offset;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            Name,
            (int)Format,
            (int)Offset);
    }
}
