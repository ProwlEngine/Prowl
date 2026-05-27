using System;
using System.Diagnostics;

namespace Prowl.Veldrid;

public sealed partial class PropertySet
{
    [Conditional("VALIDATE_USAGE")]
    private static void SetBuffer_CheckBuffer(DeviceBuffer buffer)
    {
#if VALIDATE_USAGE
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer),
                "Buffer passed to PropertySet.SetBuffer must be non-null.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void SetTexture_CheckTexture(Texture texture)
    {
#if VALIDATE_USAGE
        if (texture == null)
        {
            throw new ArgumentNullException(nameof(texture),
                "Texture passed to PropertySet.SetTexture must be non-null.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void SetTexture_CheckView(TextureView view)
    {
#if VALIDATE_USAGE
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view),
                "TextureView passed to PropertySet.SetTexture must be non-null.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void SetSampler_CheckSampler(Sampler sampler)
    {
#if VALIDATE_USAGE
        if (sampler == null)
        {
            throw new ArgumentNullException(nameof(sampler),
                "Sampler passed to PropertySet.SetSampler must be non-null.");
        }
#endif
    }
}
