using System;

namespace Prowl.Graphite;

/// <summary>
/// Swapchain creation params for ResourceFactory.
/// </summary>
public struct SwapchainDescription : IEquatable<SwapchainDescription>
{
    /// <summary>
    /// Platform-specific window handle target.
    /// </summary>
    public SwapchainSource Source;

    /// <summary>
    /// Initial width.
    /// </summary>
    public uint Width;
    /// <summary>
    /// Initial height.
    /// </summary>
    public uint Height;
    /// <summary>
    /// Depth target format, null = none.
    /// </summary>
    public PixelFormat? DepthFormat;
    /// <summary>
    /// Sync presentation to vblank.
    /// </summary>
    public bool SyncToVerticalBlank;
    /// <summary>
    /// Color target uses sRGB.
    /// </summary>
    public bool ColorSrgb;

    /// <summary>
    /// Makes a swapchain desc.
    /// </summary>
    /// <param name="source">Window handle target.</param>
    /// <param name="width">Initial width.</param>
    /// <param name="height">Initial height.</param>
    /// <param name="depthFormat">Depth format, null = none.</param>
    /// <param name="syncToVerticalBlank">Sync to vblank.</param>
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
    /// Makes a swapchain desc.
    /// </summary>
    /// <param name="source">Window handle target.</param>
    /// <param name="width">Initial width.</param>
    /// <param name="height">Initial height.</param>
    /// <param name="depthFormat">Depth format, null = none.</param>
    /// <param name="syncToVerticalBlank">Sync to vblank.</param>
    /// <param name="colorSrgb">Color target uses sRGB.</param>
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
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Instance to compare.</param>
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
    /// Hash of this instance.
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
