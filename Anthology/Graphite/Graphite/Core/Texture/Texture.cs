namespace Prowl.Graphite;

/// <summary>
/// Image data holder.
/// </summary>
public abstract class Texture : GraphicsResource, MappableResource, BindableResource
{
    private readonly object _fullTextureViewLock = new();
    private TextureView _fullTextureView;

    private protected TextureDescription _description;

    private protected Texture(in TextureDescription description)
    {
        _description = description;
    }

    /// <summary>
    /// Pixel format of texture elements.
    /// </summary>
    public PixelFormat Format => _description.Format;
    /// <summary>
    /// Width in texels.
    /// </summary>
    public uint Width => _description.Width;
    /// <summary>
    /// Height in texels.
    /// </summary>
    public uint Height => _description.Height;
    /// <summary>
    /// Depth in texels.
    /// </summary>
    public uint Depth => _description.Depth;
    /// <summary>
    /// Mipmap level count.
    /// </summary>
    public uint MipLevels => _description.MipLevels;
    /// <summary>
    /// Array layer count.
    /// </summary>
    public uint ArrayLayers => _description.ArrayLayers;
    /// <summary>
    /// Usage flags from creation.
    /// </summary>
    public TextureUsage Usage => _description.Usage;
    /// <summary>
    /// Texture type.
    /// </summary>
    public TextureType Type => _description.Type;
    /// <summary>
    /// Sample count (>1 for multisample).
    /// </summary>
    public TextureSampleCount SampleCount => _description.SampleCount;

    /// <summary>
    /// Get subresource index from mip and layer.
    /// </summary>
    /// <param name="mipLevel">Mip level.</param>
    /// <param name="arrayLayer">Array layer.</param>
    /// <returns>Subresource index.</returns>
    public uint CalculateSubresource(uint mipLevel, uint arrayLayer)
    {
        return arrayLayer * MipLevels + mipLevel;
    }

    internal TextureView GetFullTextureView(GraphicsDevice gd)
    {
        lock (_fullTextureViewLock)
        {
            _fullTextureView ??= gd.ResourceFactory.CreateTextureView(this);
            return _fullTextureView;
        }
    }

    private protected override void OnDisposing()
    {
        lock (_fullTextureViewLock)
        {
            _fullTextureView?.Dispose();
        }
    }
}
