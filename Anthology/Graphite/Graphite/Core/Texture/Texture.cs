using System;

namespace Prowl.Graphite;

/// <summary>
/// Device resource holding image data in some format.
/// </summary>
public abstract class Texture : DeviceResource, MappableResource, IDisposable, BindableResource
{
    private readonly object _fullTextureViewLock = new();
    private TextureView _fullTextureView;

    /// <summary>
    /// Gets subresource index from mip level and array layer.
    /// </summary>
    /// <param name="mipLevel">Mip level, must be less than MipLevels.</param>
    /// <param name="arrayLayer">Array layer, must be less than ArrayLayers.</param>
    /// <returns>Subresource index.</returns>
    public uint CalculateSubresource(uint mipLevel, uint arrayLayer)
    {
        return arrayLayer * MipLevels + mipLevel;
    }

    /// <summary>
    /// Pixel format of texture elements.
    /// </summary>
    public abstract PixelFormat Format { get; }
    /// <summary>
    /// Width in texels.
    /// </summary>
    public abstract uint Width { get; }
    /// <summary>
    /// Height in texels.
    /// </summary>
    public abstract uint Height { get; }
    /// <summary>
    /// Depth in texels.
    /// </summary>
    public abstract uint Depth { get; }
    /// <summary>
    /// Mipmap level count.
    /// </summary>
    public abstract uint MipLevels { get; }
    /// <summary>
    /// Array layer count.
    /// </summary>
    public abstract uint ArrayLayers { get; }
    /// <summary>
    /// Usage flags from creation. Using outside these contexts is an error.
    /// </summary>
    public abstract TextureUsage Usage { get; }
    /// <summary>
    /// Texture type.
    /// </summary>
    public abstract TextureType Type { get; }
    /// <summary>
    /// Sample count. Anything other than 1 means multisample.
    /// </summary>
    public abstract TextureSampleCount SampleCount { get; }
    /// <summary>
    /// Debug name, shows up in graphics debuggers.
    /// </summary>
    public abstract string Name { get; set; }
    /// <summary>
    /// Whether this has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    internal TextureView GetFullTextureView(GraphicsDevice gd)
    {
        lock (_fullTextureViewLock)
        {
            if (_fullTextureView == null)
            {
                _fullTextureView = CreateFullTextureView(gd);
            }

            return _fullTextureView;
        }
    }

    private protected virtual TextureView CreateFullTextureView(GraphicsDevice gd)
    {
        return gd.ResourceFactory.CreateTextureView(this);
    }

    /// <summary>
    /// Frees unmanaged device resources.
    /// </summary>
    public virtual void Dispose()
    {
        lock (_fullTextureViewLock)
        {
            _fullTextureView?.Dispose();
            DisposeCore();
        }
    }

    private protected abstract void DisposeCore();
}
