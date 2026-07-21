using System;

namespace Prowl.Graphite.RenderGraph;

/// <summary>How a graph resource's pixel size gets decided at execution time.</summary>
public enum TextureSizeMode
{
    /// <summary>Fraction of the view's pixel size.</summary>
    ViewRelative,

    /// <summary>Fixed width x height.</summary>
    Explicit
}

/// <summary>
/// A texture a pass reads or writes. Same resource ID shares one physical target; first declaration wins, writer should own it.
/// </summary>
public struct GraphTextureDesc
{
    /// <summary>How size gets decided at execution.</summary>
    public TextureSizeMode SizeMode;

    /// <summary>Scale of view size, used when view-relative.</summary>
    public float Scale;

    /// <summary>Fixed width, used when explicit.</summary>
    public int Width;

    /// <summary>Fixed height, used when explicit.</summary>
    public int Height;

    /// <summary>Color formats, one per target.</summary>
    public PixelFormat[] ColorFormats;

    /// <summary>Has a depth attachment.</summary>
    public bool EnableDepth;

    private static PixelFormat[] DefaultFormats(PixelFormat[] formats)
        => formats is { Length: > 0 } ? formats : new[] { PixelFormat.R8_G8_B8_A8_UNorm };

    /// <summary>Resource sized as a scale of the view.</summary>
    public static GraphTextureDesc ViewSized(bool depth = true, float scale = 1f, params PixelFormat[] formats) => new()
    {
        SizeMode = TextureSizeMode.ViewRelative,
        Scale = scale,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };

    /// <summary>Resource with fixed pixel size, view-independent.</summary>
    public static GraphTextureDesc Sized(int width, int height, bool depth = true, params PixelFormat[] formats) => new()
    {
        SizeMode = TextureSizeMode.Explicit,
        Scale = 1f,
        Width = width,
        Height = height,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };

    /// <summary>Resolves concrete pixel size for a view.</summary>
    public readonly (int width, int height) Resolve(uint viewWidth, uint viewHeight)
    {
        if (SizeMode == TextureSizeMode.Explicit)
            return (Math.Max(1, Width), Math.Max(1, Height));

        float s = Scale <= 0f ? 1f : Scale;
        return (Math.Max(1, (int)(viewWidth * s)), Math.Max(1, (int)(viewHeight * s)));
    }
}
