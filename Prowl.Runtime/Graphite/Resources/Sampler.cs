// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// A sampler describing how to sample textures in shaders.
/// Samplers are immutable after creation.
/// </summary>
public abstract class Sampler : GraphiteResource
{
    /// <summary>Minification filter.</summary>
    public TextureFilter MinFilter { get; protected set; }

    /// <summary>Magnification filter.</summary>
    public TextureFilter MagFilter { get; protected set; }

    /// <summary>Mipmap filter.</summary>
    public TextureFilter MipmapFilter { get; protected set; }

    /// <summary>Address mode for U coordinate.</summary>
    public TextureAddressMode AddressModeU { get; protected set; }

    /// <summary>Address mode for V coordinate.</summary>
    public TextureAddressMode AddressModeV { get; protected set; }

    /// <summary>Address mode for W coordinate.</summary>
    public TextureAddressMode AddressModeW { get; protected set; }

    /// <summary>Maximum anisotropy.</summary>
    public float MaxAnisotropy { get; protected set; }

    /// <summary>Comparison function (for shadow samplers).</summary>
    public CompareFunction? CompareFunction { get; protected set; }
}
