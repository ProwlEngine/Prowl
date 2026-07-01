namespace Prowl.Graphite.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocSetComputeShaderEntry
{
    public readonly Tracked<ComputeProgram> Program;

    public NoAllocSetComputeShaderEntry(Tracked<ComputeProgram> program)
    {
        Program = program;
    }
}
