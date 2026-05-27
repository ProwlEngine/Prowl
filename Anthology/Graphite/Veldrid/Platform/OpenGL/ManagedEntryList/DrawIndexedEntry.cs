namespace Prowl.Veldrid.OpenGL.ManagedEntryList;

internal class DrawIndexedEntry : OpenGLCommandEntry
{
    public uint InstanceCount;
    public uint IndexStart;
    public int VertexOffset;
    public uint InstanceStart;

    public DrawIndexedEntry(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        InstanceCount = instanceCount;
        IndexStart = indexStart;
        VertexOffset = vertexOffset;
        InstanceStart = instanceStart;
    }

    public DrawIndexedEntry() { }

    public DrawIndexedEntry Init(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        InstanceCount = instanceCount;
        IndexStart = indexStart;
        VertexOffset = vertexOffset;
        InstanceStart = instanceStart;
        return this;
    }

    public override void ClearReferences()
    {
    }
}
