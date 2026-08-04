namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>Resolves multisampled texture into non-multisampled one.</summary>
    /// <param name="source">Source, sample count > 1.</param>
    /// <param name="destination">Destination, sample count 1.</param>
    public void ResolveTexture(Texture source, Texture destination)
    {
        ResolveTexture_CheckSampleCounts(source, destination);
        ResolveTextureCore(source, destination);
    }

    /// <summary>Resolves multisampled texture into non-multisampled one.</summary>
    /// <param name="source">Source, sample count > 1.</param>
    /// <param name="destination">Destination, sample count 1.</param>
    protected abstract void ResolveTextureCore(Texture source, Texture destination);
}
