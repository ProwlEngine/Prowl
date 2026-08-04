using System;

namespace Prowl.Graphite;

/// <summary>
/// Framebuffer attachment (color or depth).
/// </summary>
public partial struct FramebufferAttachmentDescription : IEquatable<FramebufferAttachmentDescription>
{
    /// <summary>
    /// Render target (RenderTarget for color, DepthStencil for depth).
    /// </summary>
    public Texture Target;
    /// <summary>
    /// Array layer for rendering.
    /// </summary>
    public uint ArrayLayer;
    /// <summary>
    /// Mip level for rendering.
    /// </summary>
    public uint MipLevel;

    /// <summary>
    /// New attachment.
    /// </summary>
    /// <param name="target">Render target.</param>
    /// <param name="arrayLayer">Array layer.</param>
    public FramebufferAttachmentDescription(Texture target, uint arrayLayer)
        : this(target, arrayLayer, 0)
    { }

    /// <summary>
    /// New attachment.
    /// </summary>
    /// <param name="target">Render target.</param>
    /// <param name="arrayLayer">Array layer.</param>
    /// <param name="mipLevel">Mip level.</param>
    public FramebufferAttachmentDescription(Texture target, uint arrayLayer, uint mipLevel)
    {
        FramebufferAttachmentDescription_CheckLayerAndMip(target, arrayLayer, mipLevel);
        Target = target;
        ArrayLayer = arrayLayer;
        MipLevel = mipLevel;
    }

    /// <summary>
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if all fields match.</returns>
    public readonly bool Equals(FramebufferAttachmentDescription other)
    {
        return Target.Equals(other.Target) && ArrayLayer.Equals(other.ArrayLayer) && MipLevel.Equals(other.MipLevel);
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Target.GetHashCode(), ArrayLayer.GetHashCode(), MipLevel.GetHashCode());
    }
}
