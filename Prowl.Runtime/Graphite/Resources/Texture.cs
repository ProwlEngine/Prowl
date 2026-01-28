// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// A GPU texture for storing image data.
/// Textures are immutable after creation - use GraphiteDevice.UpdateTexture for updates.
/// </summary>
public abstract class Texture : GraphiteResource
{
    /// <summary>Texture dimension type.</summary>
    public TextureDimension Dimension { get; protected set; }

    /// <summary>Width in pixels.</summary>
    public uint Width { get; protected set; }

    /// <summary>Height in pixels.</summary>
    public uint Height { get; protected set; }

    /// <summary>Depth in pixels (for 3D textures).</summary>
    public uint Depth { get; protected set; }

    /// <summary>Number of mip levels.</summary>
    public uint MipLevels { get; protected set; }

    /// <summary>Number of array layers.</summary>
    public uint ArrayLayers { get; protected set; }

    /// <summary>Pixel format.</summary>
    public TextureFormat Format { get; protected set; }

    /// <summary>How the texture is used.</summary>
    public TextureUsage Usage { get; protected set; }

    /// <summary>Multisample count.</summary>
    public SampleCount SampleCount { get; protected set; }

    /// <summary>Whether this is a depth/stencil format.</summary>
    public bool IsDepthStencil => Format is TextureFormat.Depth16Unorm
        or TextureFormat.Depth24Plus
        or TextureFormat.Depth24PlusStencil8
        or TextureFormat.Depth32Float
        or TextureFormat.Depth32FloatStencil8;

    /// <summary>Whether this format includes a stencil component.</summary>
    public bool HasStencil => Format is TextureFormat.Depth24PlusStencil8
        or TextureFormat.Depth32FloatStencil8;
}
