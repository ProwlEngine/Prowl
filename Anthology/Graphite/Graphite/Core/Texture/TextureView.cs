namespace Prowl.Graphite;

/// <summary>
/// Shader-bindable sampled view into a texture.
/// </summary>
public abstract class TextureView : GraphicsResource, BindableResource
{
    /// <summary>
    /// Texture being sampled.
    /// </summary>
    public Texture Target { get; }
    /// <summary>
    /// First visible mip.
    /// </summary>
    public uint BaseMipLevel { get; }
    /// <summary>
    /// Visible mip count.
    /// </summary>
    public uint MipLevels { get; }
    /// <summary>
    /// First visible layer.
    /// </summary>
    public uint BaseArrayLayer { get; }
    /// <summary>
    /// Visible layer count.
    /// </summary>
    public uint ArrayLayers { get; }
    /// <summary>
    /// Read format. Can differ from texture's real format, same size only.
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
