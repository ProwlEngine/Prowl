namespace Prowl.Graphite.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocSetShaderEntry
{
    public readonly Tracked<GraphicsProgram> Program;

    public NoAllocSetShaderEntry(Tracked<GraphicsProgram> program)
    {
        Program = program;
    }
}
