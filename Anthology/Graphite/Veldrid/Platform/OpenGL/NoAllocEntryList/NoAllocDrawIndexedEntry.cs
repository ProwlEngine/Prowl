namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocDrawIndexedEntry
{
    public readonly uint InstanceCount;
    public readonly uint IndexStart;
    public readonly int VertexOffset;
    public readonly uint InstanceStart;

    public NoAllocDrawIndexedEntry(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        InstanceCount = instanceCount;
        IndexStart = indexStart;
        VertexOffset = vertexOffset;
        InstanceStart = instanceStart;
    }
}
