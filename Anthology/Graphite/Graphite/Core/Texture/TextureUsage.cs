using System;

namespace Prowl.Graphite;

/// <summary>
/// Texture usage bitmask.
/// </summary>
[Flags]
public enum TextureUsage : byte
{
    /// <summary>
    /// Readable in shaders via read-only view.
    /// </summary>
    Sampled = 1 << 0,
    /// <summary>
    /// Readable/writable in shaders.
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
    /// 2D cubemap.
    /// </summary>
    Cubemap = 1 << 4,
    /// <summary>
    /// Staging for uploads; required for Map.
    /// </summary>
    Staging = 1 << 5,
    /// <summary>
    /// Supports auto mipmap generation.
    /// </summary>
    GenerateMipmaps = 1 << 6,
}
