namespace Prowl.Graphite;

/// <summary>
/// Framebuffer attachment (color or depth).
/// </summary>
public readonly partial struct FramebufferAttachment
{
    /// <summary>
    /// Render target (RenderTarget for color, DepthStencil for depth).
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
    /// New attachment at mip 0.
    /// </summary>
    /// <param name="target">Texture to render to.</param>
    /// <param name="arrayLayer">Target array layer.</param>
    public FramebufferAttachment(Texture target, uint arrayLayer)
        : this(target, arrayLayer, 0)
    { }

    /// <summary>
    /// New attachment.
    /// </summary>
    /// <param name="target">Texture to render to.</param>
    /// <param name="arrayLayer">Target array layer.</param>
    /// <param name="mipLevel">Target mip level.</param>
    public FramebufferAttachment(Texture target, uint arrayLayer, uint mipLevel)
    {
        FramebufferAttachment_CheckLayerAndMip(target, arrayLayer, mipLevel);
        Target = target;
        ArrayLayer = arrayLayer;
        MipLevel = mipLevel;
    }
}
