using System;

namespace Prowl.Graphite;

/// <summary>
/// Color/depth attachments plus framebuffer, sized from a RenderTextureDescription.
/// <para>
/// Bind Framebuffer to render into it, or bind ColorTextures/DepthTexture to sample from it later.
/// </para>
/// <para>
/// Create via ResourceFactory.CreateRenderTexture.
/// </para>
/// </summary>
public sealed class RenderTexture : IDisposable
{
    private const PixelFormat DepthFormat = PixelFormat.D24_UNorm_S8_UInt;

    /// <summary>Desc this was built from.</summary>
    public RenderTextureDescription Desc { get; }

    /// <summary>Color attachments in order. Empty means depth-only.</summary>
    public Texture[] ColorTextures { get; }

    /// <summary>Depth attachment, null if none.</summary>
    public Texture? DepthTexture { get; }

    /// <summary>Framebuffer wrapping the attachments.</summary>
    public Framebuffer Framebuffer { get; }

    internal RenderTexture(GraphicsDevice device, in RenderTextureDescription desc)
    {
        if (desc.ColorFormats.Length == 0 && !desc.Depth)
            throw new RenderException("Cannot create a render texture with no color attachments and no depth attachment.");

        Desc = desc;

        ResourceFactory factory = device.ResourceFactory;

        ColorTextures = new Texture[desc.ColorFormats.Length];
        for (int i = 0; i < ColorTextures.Length; i++)
        {
            ColorTextures[i] = factory.CreateTexture(TextureDescription.Texture2D(
                desc.Width, desc.Height, 1, 1,
                desc.ColorFormats[i],
                TextureUsage.RenderTarget | TextureUsage.Sampled,
                desc.SampleCount));
        }

        if (desc.Depth)
        {
            DepthTexture = factory.CreateTexture(TextureDescription.Texture2D(
                desc.Width, desc.Height, 1, 1,
                DepthFormat,
                TextureUsage.DepthStencil,
                desc.SampleCount));
        }

        Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(DepthTexture, ColorTextures));
    }

    /// <summary>Sets debug name on framebuffer and all textures.</summary>
    public string Name
    {
        set
        {
            Framebuffer.Name = value;
            for (int i = 0; i < ColorTextures.Length; i++)
                ColorTextures[i].Name = $"{value} Color[{i}]";
            if (DepthTexture != null)
                DepthTexture.Name = $"{value} Depth";
        }
    }

    /// <summary>Disposes framebuffer and all textures.</summary>
    public void Dispose()
    {
        Framebuffer.Dispose();
        foreach (Texture texture in ColorTextures)
            texture.Dispose();
        DepthTexture?.Dispose();
    }
}
