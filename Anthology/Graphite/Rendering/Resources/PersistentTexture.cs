using System;

namespace Prowl.Graphite.Rendering;

/// <summary>A graph texture resolved before the graph runs, so its bindless slot is known while records are written.</summary>
internal sealed class PersistentTexture(RenderResourceID id, GraphTextureDesc description, GraphInstanceKey key, MaterialPropertyType type)
{
    public RenderResourceID Id { get; } = id;

    public RenderTexture? Texture { get; private set; }

    public uint Slot { get; private set; }

    public RenderTexture Resolve(GraphicsDevice device, GraphStore store, IMaterialResources resources, out bool changed)
        => Resolve(device, store, texture => resources.TextureSlot(texture.ColorTextures[0], type, "black"), out changed);

    public RenderTexture Resolve(GraphicsDevice device, GraphStore store, Func<RenderTexture, uint> slotOf, out bool changed)
    {
        RenderTexture resolved = store.ResolvePersistent(device, id, description, key);
        changed = !ReferenceEquals(resolved, Texture);
        if (changed)
        {
            Texture = resolved;
            Slot = slotOf(resolved);
        }
        return resolved;
    }
}
