namespace Prowl.Graphite;

/// <summary>How a swapchain's images are turned to reach the display, clockwise.</summary>
public enum SwapchainTransform
{
    /// <summary>Shown as drawn.</summary>
    Identity,
    /// <summary>Turned a quarter clockwise.</summary>
    Rotate90,
    /// <summary>Turned upside down.</summary>
    Rotate180,
    /// <summary>Turned three quarters clockwise.</summary>
    Rotate270,
}

/// <summary>Presents rendered images to a surface.</summary>
public abstract class Swapchain : GraphicsResource
{
    /// <summary>Framebuffer for render targets. Holds no images while <see cref="HasSurface"/> is false.</summary>
    public abstract Framebuffer Framebuffer { get; }
    /// <summary>Resizes the swapchain textures.</summary>
    /// <param name="width">New width.</param>
    /// <param name="height">New height.</param>
    public abstract void Resize(uint width, uint height);
    /// <summary>Whether presentation syncs to vblank.</summary>
    public abstract bool SyncToVerticalBlank { get; set; }

    /// <summary>
    /// How the display turns the images when <see cref="SwapchainDescription.PreRotate"/> is set, so the host draws
    /// rotated the other way. Identity otherwise.
    /// </summary>
    public abstract SwapchainTransform Transform { get; }

    /// <summary>
    /// Whether the swapchain has a surface to present to. A lost surface, as when an Android app goes to the background,
    /// lets it go and frames present nothing until <see cref="AttachSurface"/>. Do not draw to the framebuffer meanwhile.
    /// </summary>
    public abstract bool HasSurface { get; }

    /// <summary>Lets the surface and its images go, before the window that owns it is destroyed. The device stays.</summary>
    public abstract void DetachSurface();

    /// <summary>Presents to a new surface from now on, replacing the current one if any.</summary>
    /// <param name="source">The new surface.</param>
    /// <param name="width">Its width, used when the surface does not report its own size.</param>
    /// <param name="height">Its height, used when the surface does not report its own size.</param>
    public abstract void AttachSurface(SwapchainSource source, uint width, uint height);

    internal abstract void RequireDepth(PixelFormat format);
}
