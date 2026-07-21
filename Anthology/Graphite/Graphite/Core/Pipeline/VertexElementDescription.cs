using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes one vertex element.
/// </summary>
public struct VertexElementDescription : IEquatable<VertexElementDescription>
{
    /// <summary>
    /// Interned name, stable across backends. For reflected attributes this is semantic name + index
    /// (e.g. UV0). HLSL backends bind via HlslSemanticName + location instead. Implicit string conversion works.
    /// </summary>
    public VertexAttributeID Name;

    /// <summary>
    /// Element format.
    /// </summary>
    public VertexElementFormat Format;

    /// <summary>
    /// Byte offset from vertex start.
    /// </summary>
    public uint Offset;

    /// <summary>
    /// Raw HLSL semantic name, no index (e.g. UV). Index comes from location. Defaults to the ctor name.
    /// </summary>
    public string HlslSemanticName;

    /// <summary>
    /// Makes a per-vertex element description.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format)
    {
        Name = name;
        Format = format;
        Offset = 0;
        HlslSemanticName = name;
    }

    /// <summary>
    /// Makes a new VertexElementDescription.
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
    /// Hash code for this instance.
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
