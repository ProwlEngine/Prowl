namespace Prowl.Graphite.OpenGL.NoAllocEntryList;

internal readonly struct NoAllocResolveTextureEntry
{
    public readonly Tracked<Texture> Source;
    public readonly Tracked<Texture> Destination;

    public NoAllocResolveTextureEntry(Tracked<Texture> source, Tracked<Texture> destination)
    {
        Source = source;
        Destination = destination;
    }
}
