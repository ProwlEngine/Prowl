namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal struct NoAllocSetComputeShaderEntry
{
    public readonly Tracked<ComputeProgram> Program;

    public NoAllocSetComputeShaderEntry(Tracked<ComputeProgram> program)
    {
        Program = program;
    }
}
