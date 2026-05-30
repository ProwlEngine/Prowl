namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal struct NoAllocSetShaderEntry
{
    public readonly Tracked<GraphicsProgram> Program;

    public NoAllocSetShaderEntry(Tracked<GraphicsProgram> program)
    {
        Program = program;
    }
}
