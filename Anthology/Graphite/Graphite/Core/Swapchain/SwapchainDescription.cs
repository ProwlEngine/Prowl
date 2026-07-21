using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a Swapchain for creation via a ResourceFactory.
/// </summary>
public struct SwapchainDescription : IEquatable<SwapchainDescription>
{
    /// <summary>
    /// Rendering target, platform-specific window handle.
    /// </summary>
    public SwapchainSource Source;

    /// <summary>
    /// Initial surface width.
    /// </summary>
    public uint Width;
    /// <summary>
    /// Initial surface height.
    /// </summary>
    public uint Height;
    /// <summary>
    /// Optional depth target format. Null means no depth target.
    /// </summary>
    public PixelFormat? DepthFormat;
    /// <summary>
    /// Whether presentation syncs to vblank.
    /// </summary>
    public bool SyncToVerticalBlank;
    /// <summary>
    /// Whether the color target uses an sRGB format.
    /// </summary>
    public bool ColorSrgb;

    /// <summary>
    /// Makes a SwapchainDescription.
    /// </summary>
    /// <param name="source">Rendering target, platform-specific window handle.</param>
    /// <param name="width">Initial surface width.</param>
    /// <param name="height">Initial surface height.</param>
    /// <param name="depthFormat">Optional depth target format. Null means no depth target.</param>
    /// <param name="syncToVerticalBlank">Whether presentation syncs to vblank.</param>
    public SwapchainDescription(
        SwapchainSource source,
        uint width,
        uint height,
        PixelFormat? depthFormat,
        bool syncToVerticalBlank)
    {
        Source = source;
        Width = width;
        Height = height;
        DepthFormat = depthFormat;
        SyncToVerticalBlank = syncToVerticalBlank;
        ColorSrgb = false;
    }

    /// <summary>
    /// Makes a SwapchainDescription.
    /// </summary>
    /// <param name="source">Rendering target, platform-specific window handle.</param>
    /// <param name="width">Initial surface width.</param>
    /// <param name="height">Initial surface height.</param>
    /// <param name="depthFormat">Optional depth target format. Null means no depth target.</param>
    /// <param name="syncToVerticalBlank">Whether presentation syncs to vblank.</param>
    /// <param name="colorSrgb">Whether the color target uses an sRGB format.</param>
    public SwapchainDescription(
        SwapchainSource source,
        uint width,
        uint height,
        PixelFormat? depthFormat,
        bool syncToVerticalBlank,
        bool colorSrgb)
    {
        Source = source;
        Width = width;
        Height = height;
        DepthFormat = depthFormat;
        SyncToVerticalBlank = syncToVerticalBlank;
        ColorSrgb = colorSrgb;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if equal.</returns>
    public readonly bool Equals(SwapchainDescription other)
    {
        return Source.Equals(other.Source)
            && Width.Equals(other.Width)
            && Height.Equals(other.Height)
            && DepthFormat == other.DepthFormat
            && SyncToVerticalBlank.Equals(other.SyncToVerticalBlank)
            && ColorSrgb.Equals(other.ColorSrgb);
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>32-bit hash.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Source.GetHashCode(),
            Width.GetHashCode(),
            Height.GetHashCode(),
            DepthFormat.GetHashCode(),
            SyncToVerticalBlank.GetHashCode(),
            ColorSrgb.GetHashCode());
    }
}
