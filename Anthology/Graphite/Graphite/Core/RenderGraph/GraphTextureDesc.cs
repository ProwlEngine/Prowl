using System;

namespace Prowl.Graphite;

/// <summary>Size, formats and layout of a texture the graph creates.</summary>
public struct GraphTextureDesc
{
    /// <summary>Width in pixels.</summary>
    public int Width;

    /// <summary>Height in pixels.</summary>
    public int Height;

    /// <summary>Color formats, one per target.</summary>
    public PixelFormat[] ColorFormats;

    /// <summary>Has a depth attachment.</summary>
    public bool EnableDepth;

    /// <summary>Explicit depth-stencil format, null for the device default. Needs EnableDepth.</summary>
    public PixelFormat? DepthFormat;

    /// <summary>
    /// Array layers, drawn together through multiview: 2 for both eyes of a stereo view, 6 for a cube capture. 0 or 1 for one layer.
    /// </summary>
    public uint ArrayLayers;

    /// <summary>Mip levels, one by default. A pass draws into a level through <see cref="RenderTexture.MipFramebuffer"/>.</summary>
    public uint MipLevels;

    /// <summary>A cube Width by Width, drawn through six views. Needs one sample and no depth.</summary>
    public bool Cube;

    /// <summary>
    /// Samples per pixel of every attachment, for MSAA, one by default. Resolve into a single sample texture with
    /// <see cref="CommandBuffer.ResolveTexture"/>.
    /// </summary>
    public TextureSampleCount SampleCount;

    private static PixelFormat[] DefaultFormats(PixelFormat[] formats)
        => formats is { Length: > 0 } ? formats : new[] { PixelFormat.R8_G8_B8_A8_UNorm };

    /// <summary>Resource of one color format.</summary>
    public static GraphTextureDesc Sized(int width, int height, PixelFormat format, bool depth = false) => new()
    {
        Width = width,
        Height = height,
        ColorFormats = new[] { format },
        EnableDepth = depth
    };

    /// <summary>Resource with any number of color formats.</summary>
    public static GraphTextureDesc Sized(int width, int height, bool depth = false, params PixelFormat[] formats) => new()
    {
        Width = width,
        Height = height,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };
}
