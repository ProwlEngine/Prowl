using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>
/// Device resource controlling which color and depth textures get rendered to.
/// </summary>
public abstract class Framebuffer : DeviceResource, IDisposable
{
    /// <summary>
    /// Depth attachment. Null if no depth texture.
    /// </summary>
    public virtual FramebufferAttachment? DepthTarget { get; }

    /// <summary>
    /// Color attachments. May be empty.
    /// </summary>
    public virtual IReadOnlyList<FramebufferAttachment> ColorTargets { get; }

    /// <summary>
    /// Number and formats of the depth and color targets.
    /// </summary>
    public virtual OutputDescription OutputDescription { get; }

    /// <summary>
    /// Width.
    /// </summary>
    public virtual uint Width { get; }

    /// <summary>
    /// Height.
    /// </summary>
    public virtual uint Height { get; }


    internal Framebuffer() { }


    internal Framebuffer(
        FramebufferAttachmentDescription? depthTargetDesc,
        IReadOnlyList<FramebufferAttachmentDescription> colorTargetDescs)
    {
        if (depthTargetDesc != null)
        {
            FramebufferAttachmentDescription depthAttachment = depthTargetDesc.Value;
            DepthTarget = new FramebufferAttachment(
                depthAttachment.Target,
                depthAttachment.ArrayLayer,
                depthAttachment.MipLevel);
        }
        FramebufferAttachment[] colorTargets = new FramebufferAttachment[colorTargetDescs.Count];
        for (int i = 0; i < colorTargets.Length; i++)
        {
            colorTargets[i] = new FramebufferAttachment(
                colorTargetDescs[i].Target,
                colorTargetDescs[i].ArrayLayer,
                colorTargetDescs[i].MipLevel);
        }

        ColorTargets = colorTargets;

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

    /// <summary>
    /// Name for identifying this in graphics debuggers.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// Whether this has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources.
    /// </summary>
    public abstract void Dispose();
}
