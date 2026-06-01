namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocSetVertexSourceEntry
{
    public readonly Tracked<IVertexSource> Source;

    public NoAllocSetVertexSourceEntry(Tracked<IVertexSource> source)
    {
        Source = source;
    }
}
