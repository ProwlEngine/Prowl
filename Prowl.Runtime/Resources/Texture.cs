// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite;

using GraphiteTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime.Resources;

/// <summary>
/// This is the base class for all texture types and manages some of their internal workings.
/// <para>
/// Backed by Prowl.Graphite. Graphite keeps sampler state (filter/address modes) on a
/// separate <see cref="Graphite.Sampler"/> rather than on the texture, so changing a
/// filter or wrap mode rebuilds <see cref="Sampler"/>. The GPU texture itself is immutable
/// in size, so resizing recreates <see cref="Handle"/> (done by the derived types once the
/// dimensions are known).
/// </para>
/// </summary>
public abstract class Texture : EngineObject
{
    private protected const SamplerFilter DefaultFilter = SamplerFilter.MinPoint_MagPoint_MipPoint;
    private protected const SamplerFilter DefaultMipmapFilter = SamplerFilter.MinPoint_MagPoint_MipLinear;

    /// <summary>The backing Graphite GPU texture. Null until a derived type allocates storage.</summary>
    public GraphiteTexture Handle { get; private protected set; }

    /// <summary>The Graphite sampler describing how this texture is filtered and wrapped.</summary>
    public Sampler Sampler { get; private set; }

    /// <summary>The type of this <see cref="Texture"/>, such as 2D, 3D, CubeMap.</summary>
    public readonly TextureType Type;

    public SamplerFilter Filter { get; private set; }
    public SamplerAddressMode AddressModeU { get; private set; }
    public SamplerAddressMode AddressModeV { get; private set; }
    public SamplerAddressMode AddressModeW { get; private set; }

    /// <summary>The pixel format for this <see cref="Texture"/>'s image.</summary>
    public readonly PixelFormat ImageFormat;

    /// <summary>Gets whether this <see cref="Texture"/> is mipmapped.</summary>
    public bool IsMipmapped { get; private protected set; }

    /// <summary>False if this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
    private readonly bool isNotMipmappable;

    /// <summary>Gets whether this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
    public bool IsMipmappable => !isNotMipmappable;

    public bool IsCubemap { get; private protected set; }


    internal Texture(TextureType type, PixelFormat imageFormat, bool isCubemap = false) : base("New Texture")
    {
        if (!Enum.IsDefined(type))
            throw new FormatException("Invalid texture target");

        Type = type;
        ImageFormat = imageFormat;
        IsMipmapped = false;
        IsCubemap = isCubemap;
        isNotMipmappable = !IsTextureTypeMipmappable(imageFormat, TextureSampleCount.Count1);

        Filter = DefaultFilter;
        AddressModeU = SamplerAddressMode.Wrap;
        AddressModeV = SamplerAddressMode.Wrap;
        AddressModeW = SamplerAddressMode.Wrap;
        RebuildSampler();
    }

    /// <summary>Recreates <see cref="Sampler"/> from the current filter and address-mode state.</summary>
    private protected void RebuildSampler()
    {
        Sampler?.Dispose();
        Sampler = Graphics.Device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = AddressModeU,
            AddressModeV = AddressModeV,
            AddressModeW = AddressModeW,
            Filter = Filter,
            MinimumLod = 0,
            MaximumLod = uint.MaxValue,
        });
    }

    /// <summary>Sets this <see cref="Texture"/>'s sampling filter.</summary>
    public void SetTextureFilters(SamplerFilter filter)
    {
        Filter = filter;
        RebuildSampler();
    }

    /// <summary>
    /// Bridges the old (min, mag) GL-style filter pair onto a Graphite <see cref="SamplerFilter"/>.
    /// </summary>
    public void SetTextureFilters(TextureMin min, TextureMag mag)
        => SetTextureFilters(ToSamplerFilter(min, mag));

    private static SamplerFilter ToSamplerFilter(TextureMin min, TextureMag mag)
    {
        bool magLinear = mag == TextureMag.Linear;
        return min switch
        {
            TextureMin.Nearest => magLinear ? SamplerFilter.MinPoint_MagLinear_MipPoint : SamplerFilter.MinPoint_MagPoint_MipPoint,
            TextureMin.Linear => magLinear ? SamplerFilter.MinLinear_MagLinear_MipPoint : SamplerFilter.MinLinear_MagPoint_MipPoint,
            TextureMin.NearestMipmapNearest => magLinear ? SamplerFilter.MinPoint_MagLinear_MipPoint : SamplerFilter.MinPoint_MagPoint_MipPoint,
            TextureMin.LinearMipmapNearest => magLinear ? SamplerFilter.MinLinear_MagLinear_MipPoint : SamplerFilter.MinLinear_MagPoint_MipPoint,
            TextureMin.NearestMipmapLinear => magLinear ? SamplerFilter.MinPoint_MagLinear_MipLinear : SamplerFilter.MinPoint_MagPoint_MipLinear,
            TextureMin.LinearMipmapLinear => magLinear ? SamplerFilter.MinLinear_MagLinear_MipLinear : SamplerFilter.MinLinear_MagPoint_MipLinear,
            _ => SamplerFilter.MinLinear_MagLinear_MipPoint,
        };
    }

    /// <summary>
    /// No-op compatibility shim: shadow-map depth-compare sampling is a sampler feature that has not
    /// been ported to the Graphite sampler path yet.
    /// </summary>
    public void SetDepthCompareMode(bool enabled) { }

    /// <summary>
    /// Sets the texture coordinate wrapping modes for when a texture is sampled outside the [0, 1] range.
    /// </summary>
    public void SetWrapModes(SamplerAddressMode u, SamplerAddressMode v, SamplerAddressMode w = SamplerAddressMode.Wrap)
    {
        AddressModeU = u;
        AddressModeV = v;
        AddressModeW = w;
        RebuildSampler();
    }

    /// <summary>
    /// Generates mipmaps for this <see cref="Texture"/>. The actual mip generation is recorded
    /// onto a command buffer that the frame loop flushes, since Graphite requires it to run inside
    /// an open frame.
    /// </summary>
    /// <exception cref="InvalidOperationException"/>
    public void GenerateMipmaps()
    {
        if (isNotMipmappable)
            throw new InvalidOperationException(string.Concat("This texture type is not mipmappable! Type: ", Type.ToString()));

        Graphics.RequestMipmapGeneration(Handle);
        IsMipmapped = true;
        SetTextureFilters(DefaultMipmapFilter);
    }

    public override void OnDispose()
    {
        Handle?.Dispose();
        Sampler?.Dispose();
    }

    /// <summary>
    /// Gets whether the specified <see cref="TextureType"/> type is mipmappable.
    /// </summary>
    public static bool IsTextureTypeMipmappable(PixelFormat format, TextureSampleCount sampleCount)
    {
        return format == PixelFormat.D24_UNorm_S8_UInt || format == PixelFormat.D32_Float_S8_UInt
            || sampleCount != TextureSampleCount.Count1;
    }

    /// <summary>Number of mip levels in the full chain for a texture of the given dimensions.</summary>
    private protected static uint ComputeMipLevels(uint width, uint height)
    {
        uint levels = 1;
        uint size = Math.Max(width, height);
        while (size > 1) { size /= 2; levels++; }
        return levels;
    }
}
