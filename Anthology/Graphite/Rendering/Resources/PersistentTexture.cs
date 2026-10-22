using System;

namespace Prowl.Graphite.Rendering;

internal sealed class PersistentTexture(RenderResourceID id, GraphTextureDesc description, MaterialPropertyType type)
{
    public RenderResourceID Id { get; } = id;

    public RenderTexture? Texture { get; private set; }

    public uint Slot { get; private set; }

    public void Release(RenderStore store)
    {
        if (Texture != null)
            store.Retire(Texture);
        Texture = null;
    }

    public RenderTexture Resolve(GraphicsDevice device, IMaterialResources resources, out bool changed)
        => Resolve(device, texture => resources.TextureSlot(texture.ColorTextures[0], type, "black"), out changed);

    public RenderTexture Resolve(GraphicsDevice device, Func<RenderTexture, uint> slotOf, out bool changed)
    {
        changed = Texture == null;
        if (changed)
        {
            Texture = new RenderTexture(device, Describe(description));
            Texture.Name = RenderResourceID.ToString(id);
            Slot = slotOf(Texture);
        }
        return Texture!;
    }

    internal static RenderTextureDescription Describe(in GraphTextureDesc desc)
    {
        PixelFormat[] colors = desc.ColorFormats ?? Array.Empty<PixelFormat>();
        if (desc.EnableDepth && desc.DepthFormat is PixelFormat depthFormat)
            return new RenderTextureDescription((uint)desc.Width, (uint)desc.Height, colors, depthFormat, desc.SampleCount, false) { ArrayLayers = desc.ArrayLayers, MipLevels = desc.MipLevels, Cube = desc.Cube };
        return new RenderTextureDescription((uint)desc.Width, (uint)desc.Height, colors, desc.EnableDepth, desc.SampleCount, false) { ArrayLayers = desc.ArrayLayers, MipLevels = desc.MipLevels, Cube = desc.Cube };
    }
}
