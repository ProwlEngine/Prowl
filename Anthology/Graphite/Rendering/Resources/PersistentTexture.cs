namespace Prowl.Graphite.Rendering;

/// <summary>
/// A graph texture the renderer resolves before the graph runs, so its bindless slot is known while records are written. Resolving
/// again gives the same texture until the store replaces it, and the slot follows.
/// </summary>
internal sealed class PersistentTexture(RenderResourceID id, GraphTextureDesc description, GraphInstanceKey key, MaterialPropertyType type)
{
    public RenderTexture? Texture { get; private set; }

    public uint Slot { get; private set; }

    public RenderTexture Resolve(GraphicsDevice device, GraphStore store, IMaterialResources resources, out bool changed)
    {
        RenderTexture resolved = store.ResolvePersistent(device, id, description, key);
        changed = !ReferenceEquals(resolved, Texture);
        if (changed)
        {
            Texture = resolved;
            Slot = resources.TextureSlot(resolved.ColorTextures[0], type, "black");
        }
        return resolved;
    }
}
