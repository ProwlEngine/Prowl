using System;

namespace Prowl.Graphite;

/// <summary>
/// One attachment (color or depth) for a Framebuffer.
/// </summary>
public partial struct FramebufferAttachmentDescription : IEquatable<FramebufferAttachmentDescription>
{
    /// <summary>
    /// Texture to render into. Color needs RenderTarget usage flag, depth needs DepthStencil flag.
    /// </summary>
    public Texture Target;
    /// <summary>
    /// Array layer to render to. Must be less than the texture's array layer count.
    /// </summary>
    public uint ArrayLayer;
    /// <summary>
    /// Mip level to render to. Must be less than the texture's mip level count.
    /// </summary>
    public uint MipLevel;

    /// <summary>
    /// Makes a new attachment description.
    /// </summary>
    /// <param name="target">Texture to render into. Color needs RenderTarget flag, depth needs DepthStencil flag.</param>
    /// <param name="arrayLayer">Array layer to render to. Must be less than the texture's layer count.</param>
    public FramebufferAttachmentDescription(Texture target, uint arrayLayer)
        : this(target, arrayLayer, 0)
    { }

    /// <summary>
    /// Makes a new attachment description.
    /// </summary>
    /// <param name="target">Texture to render into. Color needs RenderTarget flag, depth needs DepthStencil flag.</param>
    /// <param name="arrayLayer">Array layer to render to. Must be less than the texture's layer count.</param>
    /// <param name="mipLevel">Mip level to render to. Must be less than the texture's mip count.</param>
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
    /// Hash code for this instance.
    /// </summary>
    /// <returns>32-bit hash code.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Target.GetHashCode(), ArrayLayer.GetHashCode(), MipLevel.GetHashCode());
    }
}
