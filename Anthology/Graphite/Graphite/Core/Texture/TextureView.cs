namespace Prowl.Graphite;

/// <summary>
/// Bindable resource giving a shader sampled access to a texture.
/// </summary>
public abstract class TextureView : GraphicsResource, BindableResource
{
    /// <summary>
    /// The texture being sampled.
    /// </summary>
    public Texture Target { get; }
    /// <summary>
    /// First visible mip level.
    /// </summary>
    public uint BaseMipLevel { get; }
    /// <summary>
    /// Visible mip level count.
    /// </summary>
    public uint MipLevels { get; }
    /// <summary>
    /// First visible array layer.
    /// </summary>
    public uint BaseArrayLayer { get; }
    /// <summary>
    /// Visible array layer count.
    /// </summary>
    public uint ArrayLayers { get; }
    /// <summary>
    /// Format to read the target texture as. Can differ from the texture's real format, same size only.
    /// </summary>
    public PixelFormat Format { get; }

    internal TextureView(ref TextureViewDescription description)
    {
        Target = description.Target;
        BaseMipLevel = description.BaseMipLevel;
        MipLevels = description.MipLevels;
        BaseArrayLayer = description.BaseArrayLayer;
        ArrayLayers = description.ArrayLayers;
        Format = description.Format ?? description.Target.Format;
    }
}
