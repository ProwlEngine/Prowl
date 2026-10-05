using System;

namespace Prowl.Graphite;

/// <summary>
/// Swapchain creation params for ResourceFactory.
/// </summary>
public struct SwapchainDescription
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
    /// Sync presentation to vblank.
    /// </summary>
    public bool SyncToVerticalBlank;
    /// <summary>
    /// Color target uses sRGB.
    /// </summary>
    public bool ColorSrgb;
}
