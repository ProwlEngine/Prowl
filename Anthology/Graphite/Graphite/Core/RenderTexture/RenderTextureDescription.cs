using System;

namespace Prowl.Graphite;

/// <summary>
/// Transient render-texture bundle rented from the device pool.
/// <para>
/// Color attachments plus maybe depth, same size and sample count. Equal descs share a free-list.
/// </para>
/// </summary>
public readonly struct RenderTextureDescription : IEquatable<RenderTextureDescription>
{
    /// <summary>
    /// Width in texels.
    /// </summary>
    public uint Width { get; }

    /// <summary>
    /// Height in texels.
    /// </summary>
    public uint Height { get; }

    /// <summary>
    /// Format per color attachment, in order. Never null; empty = depth-only.
    /// </summary>
    public PixelFormat[] ColorFormats { get; }

    /// <summary>
    /// Has a depth attachment.
    /// </summary>
    public bool Depth { get; }

    /// <summary>
    /// Sample count for every attachment.
    /// </summary>
    public TextureSampleCount SampleCount { get; }

    /// <summary>
    /// New desc.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="colorFormats">Format per color attachment. Null/empty = depth-only.</param>
    /// <param name="depth">Has a depth attachment.</param>
    /// <param name="sampleCount">Sample count for every attachment.</param>
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
    /// New single-color desc.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="colorFormat">Format of the color attachment.</param>
    /// <param name="depth">Has a depth attachment.</param>
    /// <param name="sampleCount">Sample count for every attachment.</param>
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
    /// Equal if dims, sample count, depth flag, and color formats all match.
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
    /// Equality vs a boxed instance.
    /// </summary>
    /// <param name="obj">Instance to compare to.</param>
    /// <returns>True if equal.</returns>
    public override bool Equals(object? obj) => obj is RenderTextureDescription other && Equals(other);

    /// <summary>
    /// Hash code.
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
    /// Equal.
    /// </summary>
    public static bool operator ==(RenderTextureDescription left, RenderTextureDescription right) => left.Equals(right);

    /// <summary>
    /// Not equal.
    /// </summary>
    public static bool operator !=(RenderTextureDescription left, RenderTextureDescription right) => !left.Equals(right);
}
