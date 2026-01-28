// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes how to create a sampler.
/// </summary>
public struct SamplerDescriptor
{
    /// <summary>Minification filter.</summary>
    public TextureFilter MinFilter;

    /// <summary>Magnification filter.</summary>
    public TextureFilter MagFilter;

    /// <summary>Mipmap filter.</summary>
    public TextureFilter MipmapFilter;

    /// <summary>Address mode for U coordinate.</summary>
    public TextureAddressMode AddressModeU;

    /// <summary>Address mode for V coordinate.</summary>
    public TextureAddressMode AddressModeV;

    /// <summary>Address mode for W coordinate.</summary>
    public TextureAddressMode AddressModeW;

    /// <summary>Mip LOD bias.</summary>
    public float MipLodBias;

    /// <summary>Maximum anisotropy (1.0 = disabled).</summary>
    public float MaxAnisotropy;

    /// <summary>Comparison function for shadow samplers (null = disabled).</summary>
    public CompareFunction? CompareFunction;

    /// <summary>Minimum LOD clamp.</summary>
    public float MinLod;

    /// <summary>Maximum LOD clamp.</summary>
    public float MaxLod;

    /// <summary>Border color for ClampToBorder address mode.</summary>
    public BorderColor BorderColor;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public SamplerDescriptor()
    {
        MinFilter = TextureFilter.Linear;
        MagFilter = TextureFilter.Linear;
        MipmapFilter = TextureFilter.Linear;
        AddressModeU = TextureAddressMode.Repeat;
        AddressModeV = TextureAddressMode.Repeat;
        AddressModeW = TextureAddressMode.Repeat;
        MipLodBias = 0;
        MaxAnisotropy = 1;
        CompareFunction = null;
        MinLod = 0;
        MaxLod = 1000;
        BorderColor = BorderColor.TransparentBlack;
        DebugName = null;
    }

    /// <summary>
    /// Creates a linear filtering sampler with repeating wrap mode.
    /// </summary>
    public static SamplerDescriptor LinearRepeat => new()
    {
        MinFilter = TextureFilter.Linear,
        MagFilter = TextureFilter.Linear,
        MipmapFilter = TextureFilter.Linear,
        AddressModeU = TextureAddressMode.Repeat,
        AddressModeV = TextureAddressMode.Repeat,
        AddressModeW = TextureAddressMode.Repeat,
    };

    /// <summary>
    /// Creates a linear filtering sampler with clamped wrap mode.
    /// </summary>
    public static SamplerDescriptor LinearClamp => new()
    {
        MinFilter = TextureFilter.Linear,
        MagFilter = TextureFilter.Linear,
        MipmapFilter = TextureFilter.Linear,
        AddressModeU = TextureAddressMode.ClampToEdge,
        AddressModeV = TextureAddressMode.ClampToEdge,
        AddressModeW = TextureAddressMode.ClampToEdge,
    };

    /// <summary>
    /// Creates a nearest/point filtering sampler with repeating wrap mode.
    /// </summary>
    public static SamplerDescriptor PointRepeat => new()
    {
        MinFilter = TextureFilter.Nearest,
        MagFilter = TextureFilter.Nearest,
        MipmapFilter = TextureFilter.Nearest,
        AddressModeU = TextureAddressMode.Repeat,
        AddressModeV = TextureAddressMode.Repeat,
        AddressModeW = TextureAddressMode.Repeat,
    };

    /// <summary>
    /// Creates a nearest/point filtering sampler with clamped wrap mode.
    /// </summary>
    public static SamplerDescriptor PointClamp => new()
    {
        MinFilter = TextureFilter.Nearest,
        MagFilter = TextureFilter.Nearest,
        MipmapFilter = TextureFilter.Nearest,
        AddressModeU = TextureAddressMode.ClampToEdge,
        AddressModeV = TextureAddressMode.ClampToEdge,
        AddressModeW = TextureAddressMode.ClampToEdge,
    };

    /// <summary>
    /// Creates an anisotropic filtering sampler.
    /// </summary>
    public static SamplerDescriptor Anisotropic(float maxAnisotropy = 16) => new()
    {
        MinFilter = TextureFilter.Linear,
        MagFilter = TextureFilter.Linear,
        MipmapFilter = TextureFilter.Linear,
        AddressModeU = TextureAddressMode.Repeat,
        AddressModeV = TextureAddressMode.Repeat,
        AddressModeW = TextureAddressMode.Repeat,
        MaxAnisotropy = maxAnisotropy,
    };

    /// <summary>
    /// Creates a shadow map comparison sampler.
    /// </summary>
    public static SamplerDescriptor Shadow => new()
    {
        MinFilter = TextureFilter.Linear,
        MagFilter = TextureFilter.Linear,
        MipmapFilter = TextureFilter.Nearest,
        AddressModeU = TextureAddressMode.ClampToBorder,
        AddressModeV = TextureAddressMode.ClampToBorder,
        AddressModeW = TextureAddressMode.ClampToBorder,
        CompareFunction = Graphite.CompareFunction.LessEqual,
        BorderColor = BorderColor.OpaqueWhite,
    };
}
