namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocClearDepthTargetEntry
{
    public readonly float Depth;
    public readonly byte Stencil;

    public NoAllocClearDepthTargetEntry(float depth, byte stencil)
    {
        Depth = depth;
        Stencil = stencil;
    }
}
