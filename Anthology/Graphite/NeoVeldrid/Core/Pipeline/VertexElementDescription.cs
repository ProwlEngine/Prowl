using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a single element of a vertex.
/// </summary>
public struct VertexElementDescription : IEquatable<VertexElementDescription>
{
    /// <summary>
    /// The name of the element. Used as the HLSL Semantic in the DX backend.
    /// </summary>
    public string Name;

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
    public bool Equals(VertexElementDescription other)
    {
        return Name.Equals(other.Name)
            && Format == other.Format
            && Offset == other.Offset;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Name.GetHashCode(),
            (int)Format,
            (int)Offset);
    }
}
