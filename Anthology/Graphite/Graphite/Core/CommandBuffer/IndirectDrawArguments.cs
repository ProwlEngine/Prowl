namespace Prowl.Graphite;

/// <summary>
/// Format expected by indirect draw commands in an indirect buffer.
/// </summary>
public struct IndirectDrawArguments
{
    /// <summary>
    /// Vertex count.
    /// </summary>
    public uint VertexCount;
    /// <summary>
    /// Instance count.
    /// </summary>
    public uint InstanceCount;
    /// <summary>
    /// First vertex index.
    /// </summary>
    public uint FirstVertex;
    /// <summary>
    /// First instance index.
    /// </summary>
    public uint FirstInstance;
}
