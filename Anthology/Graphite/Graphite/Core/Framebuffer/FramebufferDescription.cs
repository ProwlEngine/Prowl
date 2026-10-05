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
    public FramebufferAttachment? DepthTarget;

    /// <summary>
    /// Color textures, need RenderTarget flag. Null or empty ok.
    /// </summary>
    public FramebufferAttachment[] ColorTargets;

    /// <summary>
    /// Creates new FramebufferDescription.
    /// </summary>
    /// <param name="depthTarget">Depth texture, needs DepthStencil flag. Null ok.</param>
    /// <param name="colorTargets">Color textures, need RenderTarget flag. Null or empty ok.</param>
    public FramebufferDescription(Texture? depthTarget, params Texture[] colorTargets)
    {
        if (depthTarget != null)
        {
            DepthTarget = new FramebufferAttachment(depthTarget, 0);
        }
        else
        {
            DepthTarget = null;
        }
        ColorTargets = new FramebufferAttachment[colorTargets.Length];
        for (int i = 0; i < colorTargets.Length; i++)
        {
            ColorTargets[i] = new FramebufferAttachment(colorTargets[i], 0);
        }
    }

    /// <summary>
    /// Creates new FramebufferDescription.
    /// </summary>
    /// <param name="depthTarget">Depth attachment; null if none.</param>
    /// <param name="colorTargets">Color attachments; empty if none.</param>
    public FramebufferDescription(
        FramebufferAttachment? depthTarget,
        FramebufferAttachment[] colorTargets)
    {
        DepthTarget = depthTarget;
        ColorTargets = colorTargets;
    }
}
