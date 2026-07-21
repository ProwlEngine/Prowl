using System;
using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>
/// A set of output attachments and their formats.
/// </summary>
public struct OutputDescription : IEquatable<OutputDescription>
{
    /// <summary>
    /// Depth attachment, or null if none.
    /// </summary>
    public OutputAttachmentDescription? DepthAttachment;
    /// <summary>
    /// Color attachment descriptions, one per color attachment. Can be empty.
    /// </summary>
    public OutputAttachmentDescription[] ColorAttachments;
    /// <summary>
    /// Samples per target attachment.
    /// </summary>
    public TextureSampleCount SampleCount;

    /// <summary>
    /// New OutputDescription.
    /// </summary>
    /// <param name="depthAttachment">Depth attachment.</param>
    /// <param name="colorAttachments">Color attachment descriptions.</param>
    public OutputDescription(OutputAttachmentDescription? depthAttachment, params OutputAttachmentDescription[] colorAttachments)
    {
        DepthAttachment = depthAttachment;
        ColorAttachments = colorAttachments ?? Array.Empty<OutputAttachmentDescription>();
        SampleCount = TextureSampleCount.Count1;
    }

    /// <summary>
    /// New OutputDescription.
    /// </summary>
    /// <param name="depthAttachment">Depth attachment.</param>
    /// <param name="colorAttachments">Color attachment descriptions.</param>
    /// <param name="sampleCount">Samples per target attachment.</param>
    public OutputDescription(
        OutputAttachmentDescription? depthAttachment,
        OutputAttachmentDescription[] colorAttachments,
        TextureSampleCount sampleCount)
    {
        DepthAttachment = depthAttachment;
        ColorAttachments = colorAttachments ?? Array.Empty<OutputAttachmentDescription>();
        SampleCount = sampleCount;
    }

    internal static OutputDescription CreateFromFramebuffer(Framebuffer fb)
    {
        TextureSampleCount sampleCount = 0;
        OutputAttachmentDescription? depthAttachment = null;
        if (fb.DepthTarget != null)
        {
            depthAttachment = new OutputAttachmentDescription(fb.DepthTarget.Value.Target.Format);
            sampleCount = fb.DepthTarget.Value.Target.SampleCount;
        }
        OutputAttachmentDescription[] colorAttachments = new OutputAttachmentDescription[fb.ColorTargets.Count];
        for (int i = 0; i < colorAttachments.Length; i++)
        {
            colorAttachments[i] = new OutputAttachmentDescription(fb.ColorTargets[i].Target.Format);
            sampleCount = fb.ColorTargets[i].Target.SampleCount;
        }

        return new OutputDescription(depthAttachment, colorAttachments, sampleCount);
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if everything matches.</returns>
    public readonly bool Equals(OutputDescription other)
    {
        return DepthAttachment.GetValueOrDefault().Equals(other.DepthAttachment.GetValueOrDefault())
            && Util.ArrayEqualsEquatable(ColorAttachments, other.ColorAttachments)
            && SampleCount == other.SampleCount;
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            DepthAttachment.GetHashCode(),
            ColorAttachments.ArrayHash(),
            (int)SampleCount);
    }
}
