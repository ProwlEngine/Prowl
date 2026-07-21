using System;

namespace Prowl.Graphite;

/// <summary>
/// Bitmask for how a texture can be used.
/// </summary>
[Flags]
public enum TextureUsage : byte
{
    /// <summary>
    /// Readable in shaders via read-only view.
    /// </summary>
    Sampled = 1 << 0,
    /// <summary>
    /// Readable/writable in shaders via read-write view.
    /// </summary>
    Storage = 1 << 1,
    /// <summary>
    /// Usable as framebuffer color target.
    /// </summary>
    RenderTarget = 1 << 2,
    /// <summary>
    /// Usable as framebuffer depth target.
    /// </summary>
    DepthStencil = 1 << 3,
    /// <summary>
    /// Is a 2D cubemap.
    /// </summary>
    Cubemap = 1 << 4,
    /// <summary>
    /// Read-write staging resource for uploads. Required to use Map.
    /// </summary>
    Staging = 1 << 5,
    /// <summary>
    /// Supports auto mipmap generation via GenerateMipmaps.
    /// </summary>
    GenerateMipmaps = 1 << 6,
}
