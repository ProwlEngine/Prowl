namespace Prowl.Veldrid.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocSetFramebufferEntry
{
    public readonly Tracked<Framebuffer> Framebuffer;

    public NoAllocSetFramebufferEntry(Tracked<Framebuffer> fb)
    {
        Framebuffer = fb;
    }
}
