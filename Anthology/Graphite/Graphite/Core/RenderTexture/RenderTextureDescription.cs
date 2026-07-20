using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a transient render-texture bundle rented from the device pool.
/// <para>
/// A bundle is color attachments plus maybe a depth attachment, same size and sample count.
/// Equal descriptions share the same free-list.
/// </para>
/// </summary>
public readonly struct RenderTextureDescription : IEquatable<RenderTextureDescription>
{
    /// <summary>
    /// Width of every attachment, in texels.
    /// </summary>
    public uint Width { get; }

    /// <summary>
    /// Height of every attachment, in texels.
    /// </summary>
    public uint Height { get; }

    /// <summary>
    /// Format of each color attachment, in order. Never null; empty means depth-only bundle.
    /// </summary>
    public PixelFormat[] ColorFormats { get; }

    /// <summary>
    /// True if the bundle has a depth attachment.
    /// </summary>
    public bool Depth { get; }

    /// <summary>
    /// Sample count of every attachment in the bundle.
    /// </summary>
    public TextureSampleCount SampleCount { get; }

    /// <summary>
    /// Makes a new desc.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="colorFormats">Format of each color attachment, in order. Null/empty means depth-only.</param>
    /// <param name="depth">True if the bundle has a depth attachment.</param>
    /// <param name="sampleCount">Sample count of every attachment.</param>
    public RenderTextureDescription(
        uint width,
        uint height,
        PixelFormat[] colorFormats,
        bool depth,
        TextureSampleCount sampleCount = TextureSampleCount.Count1)
    {
        Width = width;
        Height = height;
        ColorFormats = colorFormats ?? Array.Empty<PixelFormat>();
        Depth = depth;
        SampleCount = sampleCount;
    }

    /// <summary>
    /// Makes a new single-color desc.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="colorFormat">Format of the single color attachment.</param>
    /// <param name="depth">True if the bundle has a depth attachment.</param>
    /// <param name="sampleCount">Sample count of every attachment.</param>
    public RenderTextureDescription(
        uint width,
        uint height,
        PixelFormat colorFormat,
        bool depth,
        TextureSampleCount sampleCount = TextureSampleCount.Count1)
        : this(width, height, new[] { colorFormat }, depth, sampleCount)
    {
    }

    /// <summary>
    /// Equal if dimensions, sample count, depth flag, and color formats all match.
    /// </summary>
    /// <param name="other">Instance to compare to.</param>
    /// <returns>True if equal.</returns>
    public bool Equals(RenderTextureDescription other)
    {
        if (Width != other.Width
            || Height != other.Height
            || Depth != other.Depth
            || SampleCount != other.SampleCount
            || ColorFormats.Length != other.ColorFormats.Length)
        {
            return false;
        }

        for (int i = 0; i < ColorFormats.Length; i++)
        {
            if (ColorFormats[i] != other.ColorFormats[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Equality against a boxed instance.
    /// </summary>
    /// <param name="obj">Instance to compare to.</param>
    /// <returns>True if equal.</returns>
    public override bool Equals(object? obj) => obj is RenderTextureDescription other && Equals(other);

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(Depth);
        hash.Add(SampleCount);
        foreach (PixelFormat format in ColorFormats)
            hash.Add((int)format);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(RenderTextureDescription left, RenderTextureDescription right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(RenderTextureDescription left, RenderTextureDescription right) => !left.Equals(right);
}
