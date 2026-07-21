using System;

namespace Prowl.Graphite;

/// <summary>
/// Presents rendered images to a visible surface.
/// </summary>
public abstract class Swapchain : DeviceResource, IDisposable
{
    /// <summary>
    /// Framebuffer for this instance's render targets.
    /// </summary>
    public abstract Framebuffer Framebuffer { get; }
    /// <summary>
    /// Resizes the swapchain's textures.
    /// </summary>
    /// <param name="width">New width.</param>
    /// <param name="height">New height.</param>
    public abstract void Resize(uint width, uint height);
    /// <summary>
    /// Whether presentation syncs to vblank.
    /// </summary>
    public abstract bool SyncToVerticalBlank { get; set; }
    /// <summary>
    /// Debug name, shows up in graphics debuggers.
    /// </summary>
    public abstract string Name { get; set; }
    /// <summary>
    /// Whether this has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }
    /// <summary>
    /// Frees unmanaged device resources.
    /// </summary>
    public abstract void Dispose();
}
