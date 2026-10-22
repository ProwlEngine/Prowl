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
    /// <summary>
    /// The host turns its drawing to the display itself, as Android prefers over its compositor doing it. The images
    /// keep the display's natural orientation and <see cref="Swapchain.Transform"/> says how the display turns them.
    /// </summary>
    public bool PreRotate;
}
