namespace Prowl.Veldrid.OpenGL.ManagedEntryList;

internal class SetVertexSourceEntry : OpenGLCommandEntry
{
    public IVertexSource Source;

    public SetVertexSourceEntry(IVertexSource source)
    {
        Source = source;
    }

    public SetVertexSourceEntry() { }

    public SetVertexSourceEntry Init(IVertexSource source)
    {
        Source = source;
        return this;
    }

    public override void ClearReferences()
    {
        Source = null;
    }
}
