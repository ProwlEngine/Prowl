using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>Render target for color and depth textures.</summary>
public abstract class Framebuffer : GraphicsResource
{
    /// <summary>Depth attachment, null if none.</summary>
    public virtual FramebufferAttachment? DepthTarget { get; }

    /// <summary>Color attachments, may be empty.</summary>
    public virtual IReadOnlyList<FramebufferAttachment> ColorTargets { get; }

    /// <summary>Describes depth and color target formats.</summary>
    public virtual OutputDescription OutputDescription { get; }

    /// <summary>Width.</summary>
    public virtual uint Width { get; }

    /// <summary>Height.</summary>
    public virtual uint Height { get; }


    internal virtual Swapchain? OwningSwapchain => null;

    internal Framebuffer() { }


    internal Framebuffer(
        FramebufferAttachment? depthTarget,
        IReadOnlyList<FramebufferAttachment> colorTargets)
    {
        DepthTarget = depthTarget;
        FramebufferAttachment[] colorCopy = new FramebufferAttachment[colorTargets.Count];
        for (int i = 0; i < colorCopy.Length; i++)
            colorCopy[i] = colorTargets[i];

        ColorTargets = colorCopy;

        Texture dimTex;
        uint mipLevel;
        if (ColorTargets.Count > 0)
        {
            dimTex = ColorTargets[0].Target;
            mipLevel = ColorTargets[0].MipLevel;
        }
        else
        {
            Debug.Assert(DepthTarget != null);
            dimTex = DepthTarget.Value.Target;
            mipLevel = DepthTarget.Value.MipLevel;
        }

        Util.GetMipDimensions(dimTex, mipLevel, out uint mipWidth, out uint mipHeight, out _);
        Width = mipWidth;
        Height = mipHeight;


        OutputDescription = OutputDescription.CreateFromFramebuffer(this);
    }
}
