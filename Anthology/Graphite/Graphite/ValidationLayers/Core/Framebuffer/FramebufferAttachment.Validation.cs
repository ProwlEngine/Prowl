namespace Prowl.Graphite;

public readonly partial struct FramebufferAttachment
{
    private static void FramebufferAttachment_CheckLayerAndMip(Texture target, uint arrayLayer, uint mipLevel)
    {
        uint effectiveArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(target);
        if (arrayLayer >= effectiveArrayLayers)
        {
            throw new RenderException(
                $"{nameof(arrayLayer)} must be less than {nameof(target)}.{nameof(Texture.ArrayLayers)}.");
        }
        if (mipLevel >= target.MipLevels)
        {
            throw new RenderException(
                $"{nameof(mipLevel)} must be less than {nameof(target)}.{nameof(Texture.MipLevels)}.");
        }
    }
}
