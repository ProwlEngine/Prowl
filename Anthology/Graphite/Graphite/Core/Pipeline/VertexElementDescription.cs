using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes one vertex element.
/// </summary>
public struct VertexElementDescription
{
    /// <summary>
    /// Interned stable name; for reflected attributes this is semantic name + index (e.g. UV0).
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
    /// Makes a per-vertex element description.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format)
    {
        Name = name;
        Format = format;
        Offset = 0;
    }

    /// <summary>
    /// Makes a new VertexElementDescription.
    /// </summary>
    public VertexElementDescription(string name, VertexElementFormat format, uint offset)
    {
        Name = name;
        Format = format;
        Offset = offset;
    }
}
