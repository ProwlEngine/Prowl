namespace Prowl.Graphite;

public partial struct FramebufferAttachmentDescription
{
    private static void FramebufferAttachmentDescription_CheckLayerAndMip(Texture target, uint arrayLayer, uint mipLevel)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

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
