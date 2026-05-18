using Prowl.Vector;

namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

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
