namespace Prowl.Veldrid.OpenGL.ManagedEntryList;

internal class SetGraphicsResourceSetEntry : OpenGLCommandEntry
{
    public uint Slot;
    public ResourceSet ResourceSet;
    public uint[] DynamicOffsets;

    public SetGraphicsResourceSetEntry(uint slot, ResourceSet rs, uint[] dynamicOffsets)
    {
        Slot = slot;
        ResourceSet = rs;
        DynamicOffsets = dynamicOffsets;
    }

    public SetGraphicsResourceSetEntry() { }

    public SetGraphicsResourceSetEntry Init(uint slot, ResourceSet rs, uint[] dynamicOffsets)
    {
        Slot = slot;
        ResourceSet = rs;
        DynamicOffsets = dynamicOffsets;
        return this;
    }

    public override void ClearReferences()
    {
        ResourceSet = null;
        DynamicOffsets = null;
    }
}
