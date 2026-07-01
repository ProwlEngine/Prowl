using Prowl.Vector;

namespace Prowl.Graphite.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocClearColorTargetEntry
{
    public readonly uint Index;
    public readonly Color ClearColor;

    public NoAllocClearColorTargetEntry(uint index, Color clearColor)
    {
        Index = index;
        ClearColor = clearColor;
    }
}
