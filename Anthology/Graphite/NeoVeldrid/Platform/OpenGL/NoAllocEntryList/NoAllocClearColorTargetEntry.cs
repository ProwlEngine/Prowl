using Prowl.Vector;

namespace NeoVeldrid.OpenGL.NoAllocEntryList;

internal struct NoAllocClearColorTargetEntry
{
    public readonly uint Index;
    public readonly Color ClearColor;

    public NoAllocClearColorTargetEntry(uint index, Color clearColor)
    {
        Index = index;
        ClearColor = clearColor;
    }
}
