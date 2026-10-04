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

    /// <summary>Framebuffer for these attachments.</summary>
    public Framebuffer Framebuffer { get; }

    private readonly bool _owned = true;

    internal RenderTexture(Framebuffer framebuffer)
    {
        _owned = false;
        Framebuffer = framebuffer;

        ColorTextures = new Texture[framebuffer.ColorTargets.Count];
        PixelFormat[] formats = new PixelFormat[ColorTextures.Length];
        for (int i = 0; i < ColorTextures.Length; i++)
        {
            ColorTextures[i] = framebuffer.ColorTargets[i].Target;
            formats[i] = ColorTextures[i].Format;
        }

        DepthTexture = framebuffer.DepthTarget?.Target;
        Desc = DepthTexture != null
            ? new RenderTextureDescription(framebuffer.Width, framebuffer.Height, formats, DepthTexture.Format, DepthTexture.SampleCount)
            : new RenderTextureDescription(framebuffer.Width, framebuffer.Height, formats, false);
    }

    internal RenderTexture(GraphicsDevice device, in RenderTextureDescription desc)
    {
        if (desc.ColorFormats.Length == 0 && !desc.Depth)
            throw new RenderException("Cannot create a render texture with no color attachments and no depth attachment.");

        Desc = desc;

        ResourceFactory factory = device.ResourceFactory;

        TextureUsage colorUsage = TextureUsage.RenderTarget | TextureUsage.Sampled;
        if (desc.Storage)
            colorUsage |= TextureUsage.Storage;

        ColorTextures = new Texture[desc.ColorFormats.Length];
        for (int i = 0; i < ColorTextures.Length; i++)
        {
            ColorTextures[i] = factory.CreateTexture(TextureDescription.Texture2D(
                desc.Width, desc.Height, 1, 1,
                desc.ColorFormats[i],
                colorUsage,
                desc.SampleCount));
        }

        if (desc.Depth)
        {
            DepthTexture = factory.CreateTexture(TextureDescription.Texture2D(
                desc.Width, desc.Height, 1, 1,
                desc.DepthFormat ?? ResolveDepthFormat(device),
                TextureUsage.DepthStencil | TextureUsage.Sampled,
                desc.SampleCount));
        }

        Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(DepthTexture, ColorTextures));
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
        Framebuffer.Name = name;
        for (int i = 0; i < ColorTextures.Length; i++)
            ColorTextures[i].Name = $"{name} Color[{i}]";
        if (DepthTexture != null)
            DepthTexture.Name = $"{name} Depth";
    }

    private protected override void DisposeCore()
    {
        if (!_owned)
            return;

        Framebuffer.Dispose();
        foreach (Texture texture in ColorTextures)
            texture.Dispose();
        DepthTexture?.Dispose();
    }
}
