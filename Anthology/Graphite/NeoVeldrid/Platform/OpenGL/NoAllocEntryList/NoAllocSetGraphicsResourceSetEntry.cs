namespace NeoVeldrid.OpenGL.NoAllocEntryList;

internal struct NoAllocSetResourceSetEntry
{
    public readonly uint Slot;
    public readonly Tracked<ResourceSet> ResourceSet;
    public readonly bool IsGraphics;

    public NoAllocSetResourceSetEntry(uint slot, Tracked<ResourceSet> rs, bool isGraphics)
    {
        Slot = slot;
        ResourceSet = rs;
        IsGraphics = isGraphics;
    }
}
