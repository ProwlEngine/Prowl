namespace Prowl.Veldrid.OpenGL.ManagedEntryList;

internal class SetShaderEntry : OpenGLCommandEntry
{
    public ShaderProgram Program;

    public SetShaderEntry(ShaderProgram program)
    {
        Program = program;
    }

    public SetShaderEntry() { }

    public SetShaderEntry Init(ShaderProgram program)
    {
        Program = program;
        return this;
    }

    public override void ClearReferences()
    {
        Program = null;
    }
}
