using System;

namespace Prowl.Graphite;

/// <summary>
/// Framebuffer descriptor for ResourceFactory.
/// </summary>
public struct FramebufferDescription
{
    /// <summary>
    /// Depth texture, needs DepthStencil flag. Null ok.
    /// </summary>
    public FramebufferAttachmentDescription? DepthTarget;

    /// <summary>
    /// Color textures, need RenderTarget flag. Null or empty ok.
    /// </summary>
    public FramebufferAttachmentDescription[] ColorTargets;

    /// <summary>
    /// Creates new FramebufferDescription.
    /// </summary>
    /// <param name="depthTarget">Depth texture, needs DepthStencil flag. Null ok.</param>
    /// <param name="colorTargets">Color textures, need RenderTarget flag. Null or empty ok.</param>
    public FramebufferDescription(Texture? depthTarget, params Texture[] colorTargets)
    {
        if (depthTarget != null)
        {
            DepthTarget = new FramebufferAttachmentDescription(depthTarget, 0);
        }
        else
        {
            DepthTarget = null;
        }
        ColorTargets = new FramebufferAttachmentDescription[colorTargets.Length];
        for (int i = 0; i < colorTargets.Length; i++)
        {
            ColorTargets[i] = new FramebufferAttachmentDescription(colorTargets[i], 0);
        }
    }

    /// <summary>
    /// Creates new FramebufferDescription.
    /// </summary>
    /// <param name="depthTarget">Depth attachment; null if none.</param>
    /// <param name="colorTargets">Color attachments; empty if none.</param>
    public FramebufferDescription(
        FramebufferAttachmentDescription? depthTarget,
        FramebufferAttachmentDescription[] colorTargets)
    {
        DepthTarget = depthTarget;
        ColorTargets = colorTargets;
    }
}
