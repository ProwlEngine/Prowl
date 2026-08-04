using System;

namespace Prowl.Graphite;

/// <summary>
/// Rented render-texture bundle. Colors plus maybe depth, same size/samples. Equal descs share a free-list.
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
    /// Color format per attachment. Empty = depth-only.
    /// </summary>
    public PixelFormat[] ColorFormats { get; }

    /// <summary>
    /// Has depth attachment.
    /// </summary>
    public bool Depth { get; }

    /// <summary>
    /// Sample count, all attachments.
    /// </summary>
    public TextureSampleCount SampleCount { get; }

    /// <summary>
    /// New desc.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="colorFormats">Color format per attachment. Null/empty = depth-only.</param>
    /// <param name="depth">Has depth attachment.</param>
    /// <param name="sampleCount">Sample count, all attachments.</param>
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
    /// Single-color desc.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="colorFormat">Color attachment format.</param>
    /// <param name="depth">Has depth attachment.</param>
    /// <param name="sampleCount">Sample count, all attachments.</param>
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
    /// Equal if dims, samples, depth flag, and color formats all match.
    /// </summary>
    /// <param name="other">Other instance.</param>
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
    /// Equality vs boxed object.
    /// </summary>
    /// <param name="obj">Other instance.</param>
    /// <returns>True if equal.</returns>
    public override bool Equals(object? obj) => obj is RenderTextureDescription other && Equals(other);

    /// <summary>
    /// Hash of all fields.
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
