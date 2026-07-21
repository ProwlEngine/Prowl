namespace Prowl.Graphite;

/// <summary>
/// One framebuffer output. Color or depth.
/// </summary>
public readonly struct FramebufferAttachment
{
    /// <summary>
    /// Texture being rendered to.
    /// </summary>
    public Texture Target { get; }
    /// <summary>
    /// Target array layer.
    /// </summary>
    public uint ArrayLayer { get; }
    /// <summary>
    /// Target mip level.
    /// </summary>
    public uint MipLevel { get; }

    /// <summary>
    /// New attachment, mip 0.
    /// </summary>
    /// <param name="target">Texture to render to.</param>
    /// <param name="arrayLayer">Target array layer.</param>
    public FramebufferAttachment(Texture target, uint arrayLayer)
    {
        Target = target;
        ArrayLayer = arrayLayer;
        MipLevel = 0;
    }

    /// <summary>
    /// New attachment.
    /// </summary>
    /// <param name="target">Texture to render to.</param>
    /// <param name="arrayLayer">Target array layer.</param>
    /// <param name="mipLevel">Target mip level.</param>
    public FramebufferAttachment(Texture target, uint arrayLayer, uint mipLevel)
    {
        Target = target;
        ArrayLayer = arrayLayer;
        MipLevel = mipLevel;
    }
}
