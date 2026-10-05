using System;
using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>
/// Output attachment formats.
/// </summary>
public record struct OutputDescription
{
    /// <summary>
    /// Depth attachment format or null.
    /// </summary>
    public PixelFormat? DepthFormat;
    /// <summary>
    /// Color attachment formats (can be empty).
    /// </summary>
    public PixelFormat[] ColorFormats;
    /// <summary>
    /// Samples per target attachment.
    /// </summary>
    public TextureSampleCount SampleCount;

    /// <summary>
    /// New OutputDescription.
    /// </summary>
    /// <param name="depthFormat">Depth attachment format.</param>
    /// <param name="colorFormats">Color attachment formats.</param>
    public OutputDescription(PixelFormat? depthFormat, params PixelFormat[] colorFormats)
    {
        DepthFormat = depthFormat;
        ColorFormats = colorFormats ?? Array.Empty<PixelFormat>();
        SampleCount = TextureSampleCount.Count1;
    }

    /// <summary>
    /// New OutputDescription.
    /// </summary>
    /// <param name="depthFormat">Depth attachment format.</param>
    /// <param name="colorFormats">Color attachment formats.</param>
    /// <param name="sampleCount">Samples per target attachment.</param>
    public OutputDescription(
        PixelFormat? depthFormat,
        PixelFormat[] colorFormats,
        TextureSampleCount sampleCount)
    {
        DepthFormat = depthFormat;
        ColorFormats = colorFormats ?? Array.Empty<PixelFormat>();
        SampleCount = sampleCount;
    }

    internal static OutputDescription CreateFromFramebuffer(Framebuffer fb)
    {
        TextureSampleCount sampleCount = 0;
        PixelFormat? depthFormat = null;
        if (fb.DepthTarget != null)
        {
            depthFormat = fb.DepthTarget.Value.Target.Format;
            sampleCount = fb.DepthTarget.Value.Target.SampleCount;
        }
        PixelFormat[] colorFormats = new PixelFormat[fb.ColorTargets.Count];
        for (int i = 0; i < colorFormats.Length; i++)
        {
            colorFormats[i] = fb.ColorTargets[i].Target.Format;
            sampleCount = fb.ColorTargets[i].Target.SampleCount;
        }

        return new OutputDescription(depthFormat, colorFormats, sampleCount);
    }

    /// <inheritdoc/>
    public readonly bool Equals(OutputDescription other)
        => DepthFormat == other.DepthFormat
        && SampleCount == other.SampleCount
        && ColorFormats.AsSpan().SequenceEqual(other.ColorFormats);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
        => HashCode.Combine(DepthFormat, SampleCount, ColorFormats.ArrayHash());
}
