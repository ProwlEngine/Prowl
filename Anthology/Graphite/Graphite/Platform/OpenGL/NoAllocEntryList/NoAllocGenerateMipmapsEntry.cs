namespace Prowl.Graphite.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocGenerateMipmapsEntry
{
    public readonly Tracked<Texture> Texture;

    public NoAllocGenerateMipmapsEntry(Tracked<Texture> texture)
    {
        Texture = texture;
    }
}
