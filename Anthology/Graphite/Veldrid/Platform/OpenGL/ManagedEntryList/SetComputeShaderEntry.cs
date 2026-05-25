namespace Prowl.Veldrid.OpenGL.ManagedEntryList;

internal class SetComputeShaderEntry : OpenGLCommandEntry
{
    public ComputeProgram Program;

    public SetComputeShaderEntry(ComputeProgram program)
    {
        Program = program;
    }

    public SetComputeShaderEntry() { }

    public SetComputeShaderEntry Init(ComputeProgram program)
    {
        Program = program;
        return this;
    }

    public override void ClearReferences()
    {
        Program = null;
    }
}
