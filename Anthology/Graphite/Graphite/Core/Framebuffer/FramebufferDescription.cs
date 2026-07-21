using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a framebuffer for creation via ResourceFactory.
/// </summary>
public struct FramebufferDescription : IEquatable<FramebufferDescription>
{
    /// <summary>
    /// Depth texture, needs DepthStencil usage flag. Null allowed.
    /// </summary>
    public FramebufferAttachmentDescription? DepthTarget;

    /// <summary>
    /// Color textures, need RenderTarget usage flag. Null or empty allowed.
    /// </summary>
    public FramebufferAttachmentDescription[] ColorTargets;

    /// <summary>
    /// Makes a new FramebufferDescription.
    /// </summary>
    /// <param name="depthTarget">Depth texture, needs DepthStencil usage flag. Null allowed.</param>
    /// <param name="colorTargets">Color textures, need RenderTarget usage flag. Null or empty allowed.</param>
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
    /// Makes a new FramebufferDescription.
    /// </summary>
    /// <param name="depthTarget">Depth attachment. Null if none.</param>
    /// <param name="colorTargets">Color attachments. Empty if none.</param>
    public FramebufferDescription(
        FramebufferAttachmentDescription? depthTarget,
        FramebufferAttachmentDescription[] colorTargets)
    {
        DepthTarget = depthTarget;
        ColorTargets = colorTargets;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if everything matches.</returns>
    public readonly bool Equals(FramebufferDescription other)
    {
        return Util.NullableEquals(DepthTarget, other.DepthTarget) && Util.ArrayEqualsEquatable(ColorTargets, other.ColorTargets);
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>32-bit hash.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(DepthTarget.GetHashCode(), ColorTargets.ArrayHash());
    }
}
