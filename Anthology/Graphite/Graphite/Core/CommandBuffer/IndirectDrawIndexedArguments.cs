namespace Prowl.Graphite;

/// <summary>Layout of one indexed indirect draw command in an indirect buffer.</summary>
public struct IndirectDrawIndexedArguments
{
    /// <summary>Index count for the draw.</summary>
    public uint IndexCount;
    /// <summary>Instance count.</summary>
    public uint InstanceCount;
    /// <summary>Start index.</summary>
    public uint FirstIndex;
    /// <summary>Offset added to each referenced vertex.</summary>
    public int VertexOffset;
    /// <summary>First instance ID; later instances increment by 1.</summary>
    public uint FirstInstance;
}
