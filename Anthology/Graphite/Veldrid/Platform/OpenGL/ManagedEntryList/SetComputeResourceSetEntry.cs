namespace Prowl.Veldrid.OpenGL.ManagedEntryList;

internal class SetComputeResourceSetEntry : OpenGLCommandEntry
{
    public uint Slot;
    public ResourceSet ResourceSet;
    public uint[] DynamicOffsets;

    public SetComputeResourceSetEntry(uint slot, ResourceSet rs, uint[] dynamicOffsets)
    {
        Slot = slot;
        ResourceSet = rs;
        DynamicOffsets = dynamicOffsets;
    }

    public SetComputeResourceSetEntry() { }

    public SetComputeResourceSetEntry Init(uint slot, ResourceSet rs, uint[] dynamicOffsets)
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
