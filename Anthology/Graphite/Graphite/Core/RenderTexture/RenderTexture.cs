using System;

namespace Prowl.Graphite;

/// <summary>Color/depth attachments and framebuffer from RenderTextureDescription. Use Framebuffer to render or ColorTextures/DepthTexture to sample.
/// Disposing frees the attachments unless this wraps an existing framebuffer.</summary>
public sealed class RenderTexture : GraphicsResource
{
    /// <summary>Description this was built from.</summary>
    public RenderTextureDescription Desc { get; }

    /// <summary>Color attachments in order, empty means depth-only.</summary>
    public Texture[] ColorTextures { get; }

    /// <summary>Depth attachment or null.</summary>
    public Texture? DepthTexture { get; }

    /// <summary>Framebuffer for these attachments. Throws for a wrapped texture that cannot be drawn into.</summary>
    public Framebuffer Framebuffer => _framebuffer
        ?? throw new InvalidOperationException("This render texture wraps a texture without a framebuffer, so it cannot be drawn into.");

    private readonly Framebuffer? _framebuffer;
    private readonly ResourceFactory? _factory;
    private Framebuffer?[]? _mips;
    private readonly bool _owned = true;

    /// <summary>Wraps an existing texture, so a graph can read or copy it. The caller keeps owning the texture.</summary>
    public RenderTexture(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _owned = false;

        bool depth = (texture.Usage & TextureUsage.DepthStencil) != 0;
        ColorTextures = depth ? Array.Empty<Texture>() : [texture];
        DepthTexture = depth ? texture : null;
        Desc = (depth
            ? new RenderTextureDescription(texture.Width, texture.Height, Array.Empty<PixelFormat>(), texture.Format, texture.SampleCount)
            : new RenderTextureDescription(texture.Width, texture.Height, [texture.Format], false, texture.SampleCount)) with { ArrayLayers = texture.ArrayLayers };
    }

    /// <summary>Wraps an existing framebuffer, so a graph can import it. The caller keeps owning the framebuffer.</summary>
    public RenderTexture(Framebuffer framebuffer)
    {
        _owned = false;
        _framebuffer = framebuffer;

        ColorTextures = new Texture[framebuffer.ColorTargets.Count];
        PixelFormat[] formats = new PixelFormat[ColorTextures.Length];
        for (int i = 0; i < ColorTextures.Length; i++)
        {
            ColorTextures[i] = framebuffer.ColorTargets[i].Target;
            formats[i] = ColorTextures[i].Format;
        }

        DepthTexture = framebuffer.DepthTarget?.Target;
        Desc = (DepthTexture != null
            ? new RenderTextureDescription(framebuffer.Width, framebuffer.Height, formats, DepthTexture.Format, DepthTexture.SampleCount)
            : new RenderTextureDescription(framebuffer.Width, framebuffer.Height, formats, false)) with { ArrayLayers = framebuffer.ViewCount };
    }

    internal RenderTexture(GraphicsDevice device, in RenderTextureDescription desc)
    {
        if (desc.ColorFormats.Length == 0 && !desc.Depth)
            throw new RenderException("Cannot create a render texture with no color attachments and no depth attachment.");
        if (desc.Cube && (desc.Depth || desc.SampleCount != TextureSampleCount.Count1 || desc.Width != desc.Height))
            throw new RenderException("A cube render texture is square, single sampled and has no depth attachment.");

        Desc = desc;

        ResourceFactory factory = _factory = device.ResourceFactory;

        TextureUsage colorUsage = TextureUsage.RenderTarget | TextureUsage.Sampled;
        if (desc.Storage)
            colorUsage |= TextureUsage.Storage;

        uint layers = desc.Cube ? 6 : Math.Max(1, desc.ArrayLayers);
        uint mips = Math.Max(1, desc.MipLevels);
        ColorTextures = new Texture[desc.ColorFormats.Length];
        for (int i = 0; i < ColorTextures.Length; i++)
        {
            ColorTextures[i] = factory.CreateTexture(desc.Cube
                ? TextureDescription.CreateCube(desc.Width, mips, 1, desc.ColorFormats[i], colorUsage)
                : TextureDescription.Texture2D(desc.Width, desc.Height, mips, layers, desc.ColorFormats[i], colorUsage, desc.SampleCount));
        }

        if (desc.Depth)
        {
            DepthTexture = factory.CreateTexture(TextureDescription.Texture2D(
                desc.Width, desc.Height, 1, layers,
                desc.DepthFormat ?? ResolveDepthFormat(device),
                TextureUsage.DepthStencil | TextureUsage.Sampled,
                desc.SampleCount));
        }

        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(DepthTexture, ColorTextures) { ViewCount = layers > 1 ? layers : 0 });
    }

    /// <summary>A framebuffer onto one mip level of the color attachments, made on first use. Level 0 is <see cref="Framebuffer"/>.</summary>
    public Framebuffer MipFramebuffer(uint mip)
    {
        if (mip == 0)
            return Framebuffer;
        if (!_owned || DepthTexture != null || mip >= ColorTextures[0].MipLevels)
            throw new ArgumentOutOfRangeException(nameof(mip), "Only the mips of an owned color render texture can be drawn into.");

        _mips ??= new Framebuffer?[ColorTextures[0].MipLevels];
        FramebufferAttachment[] attachments = new FramebufferAttachment[ColorTextures.Length];
        for (int i = 0; i < attachments.Length; i++)
            attachments[i] = new FramebufferAttachment(ColorTextures[i], 0, mip);
        return _mips[mip] ??= _factory!.CreateFramebuffer(new FramebufferDescription(null, attachments) { ViewCount = Desc.Cube ? 6u : Math.Max(Desc.ArrayLayers, 1) > 1 ? Desc.ArrayLayers : 0 });
    }

    /// <summary>Depth-stencil format render textures use: D24_UNorm_S8_UInt, or D32_Float_S8_UInt when the device lacks it.</summary>
    public static PixelFormat ResolveDepthFormat(GraphicsDevice device)
    {
        if (device.ResolvedDepthFormat is { } cached)
            return cached;

        const TextureUsage usage = TextureUsage.DepthStencil | TextureUsage.Sampled;
        PixelFormat format = device.GetPixelFormatSupport(PixelFormat.D24_UNorm_S8_UInt, TextureType.Texture2D, usage)
            ? PixelFormat.D24_UNorm_S8_UInt
            : PixelFormat.D32_Float_S8_UInt;
        device.ResolvedDepthFormat = format;
        return format;
    }

    private protected override void NameChanged(string name)
    {
        if (_framebuffer != null)
            _framebuffer.Name = name;
        for (int i = 0; i < ColorTextures.Length; i++)
            ColorTextures[i].Name = $"{name} Color[{i}]";
        if (DepthTexture != null)
            DepthTexture.Name = $"{name} Depth";
    }

    private protected override void DisposeCore()
    {
        if (!_owned)
            return;

        if (_mips != null)
        {
            foreach (Framebuffer? mip in _mips)
                mip?.Dispose(DisposeReason);
        }
        _framebuffer?.Dispose(DisposeReason);
        foreach (Texture texture in ColorTextures)
            texture.Dispose(DisposeReason);
        DepthTexture?.Dispose(DisposeReason);
    }
}
