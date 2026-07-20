using System;

namespace Prowl.Graphite.RenderGraph;

/// <summary>How a graph resource's pixel size is decided at execution time.</summary>
public enum TextureSizeMode
{
    /// <summary>Sized as a fraction of the view's pixel size.</summary>
    ViewRelative,

    /// <summary>Fixed width x height in pixels.</summary>
    Explicit
}

/// <summary>
/// Describes a texture a pass reads or writes. Passes sharing the same resource ID share one physical
/// target; first declaration's description wins, so a writer should own it.
/// </summary>
public struct GraphTextureDesc
{
    /// <summary>How pixel size is decided at execution time.</summary>
    public TextureSizeMode SizeMode;

    /// <summary>Fraction of view's pixel size, used when SizeMode is view-relative.</summary>
    public float Scale;

    /// <summary>Fixed width in pixels, used when SizeMode is explicit.</summary>
    public int Width;

    /// <summary>Fixed height in pixels, used when SizeMode is explicit.</summary>
    public int Height;

    /// <summary>Color attachment formats, one per render target.</summary>
    public PixelFormat[] ColorFormats;

    /// <summary>Whether the resource has a depth attachment.</summary>
    public bool EnableDepth;

    private static PixelFormat[] DefaultFormats(PixelFormat[] formats)
        => formats is { Length: > 0 } ? formats : new[] { PixelFormat.R8_G8_B8_A8_UNorm };

    /// <summary>A resource sized as a scale of the view's pixel size.</summary>
    public static GraphTextureDesc ViewSized(bool depth = true, float scale = 1f, params PixelFormat[] formats) => new()
    {
        SizeMode = TextureSizeMode.ViewRelative,
        Scale = scale,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };

    /// <summary>A resource with fixed pixel size, independent of the view.</summary>
    public static GraphTextureDesc Sized(int width, int height, bool depth = true, params PixelFormat[] formats) => new()
    {
        SizeMode = TextureSizeMode.Explicit,
        Scale = 1f,
        Width = width,
        Height = height,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };

    /// <summary>Resolves concrete pixel size for a view of the given size.</summary>
    public readonly (int width, int height) Resolve(uint viewWidth, uint viewHeight)
    {
        if (SizeMode == TextureSizeMode.Explicit)
            return (Math.Max(1, Width), Math.Max(1, Height));

        float s = Scale <= 0f ? 1f : Scale;
        return (Math.Max(1, (int)(viewWidth * s)), Math.Max(1, (int)(viewHeight * s)));
    }
}
