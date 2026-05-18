using System.Diagnostics;

namespace Prowl.Veldrid;

public partial struct FramebufferAttachmentDescription
{
    [Conditional("VALIDATE_USAGE")]
    private static void FramebufferAttachmentDescription_CheckLayerAndMip(Texture target, uint arrayLayer, uint mipLevel)
    {
#if VALIDATE_USAGE
        uint effectiveArrayLayers = target.ArrayLayers;
        if ((target.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveArrayLayers *= 6;
        }

        if (arrayLayer >= effectiveArrayLayers)
        {
            throw new VeldridException(
                $"{nameof(arrayLayer)} must be less than {nameof(target)}.{nameof(Texture.ArrayLayers)}.");
        }
        if (mipLevel >= target.MipLevels)
        {
            throw new VeldridException(
                $"{nameof(mipLevel)} must be less than {nameof(target)}.{nameof(Texture.MipLevels)}.");
        }
#endif
    }
}
