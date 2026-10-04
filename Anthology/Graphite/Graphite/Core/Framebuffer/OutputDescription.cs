using System;
using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>
/// Output attachments and formats.
/// </summary>
public record struct OutputDescription
{
    /// <summary>
    /// Depth attachment or null.
    /// </summary>
    public OutputAttachmentDescription? DepthAttachment;
    /// <summary>
    /// Color attachment descriptions (can be empty).
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

    /// <inheritdoc/>
    public readonly bool Equals(OutputDescription other)
        => DepthAttachment == other.DepthAttachment
        && SampleCount == other.SampleCount
        && Util.ArrayEqualsEquatable(ColorAttachments, other.ColorAttachments);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
        => HashCode.Combine(DepthAttachment, SampleCount, ColorAttachments.ArrayHash());
}
