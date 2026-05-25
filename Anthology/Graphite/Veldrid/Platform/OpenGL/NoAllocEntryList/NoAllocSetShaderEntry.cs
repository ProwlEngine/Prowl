namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal struct NoAllocSetShaderEntry
{
    public readonly Tracked<ShaderProgram> Program;

    public NoAllocSetShaderEntry(Tracked<ShaderProgram> program)
    {
        Program = program;
    }
}
