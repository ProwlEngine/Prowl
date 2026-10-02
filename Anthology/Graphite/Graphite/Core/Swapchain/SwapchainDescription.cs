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
