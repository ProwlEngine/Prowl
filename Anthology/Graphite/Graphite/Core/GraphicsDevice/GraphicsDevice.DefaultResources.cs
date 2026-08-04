using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private Sampler _aniso4xSampler;
    private DeviceBuffer _nullStructuredRead;
    private DeviceBuffer _nullStructuredReadWrite;

    /// <summary>
    /// Point-filtered sampler owned by this device.
    /// </summary>
    public Sampler PointSampler { get; private set; }

    /// <summary>
    /// Linear-filtered sampler owned by this device.
    /// </summary>
    public Sampler LinearSampler { get; private set; }

    /// <summary>
    /// 1x1 black transparent texture, fallback for an unmatched read-only texture slot.
    /// </summary>
    public Texture NullTexture2D { get; private set; }

    /// <summary>
    /// 1x1 black transparent RW texture, fallback for an unmatched read-write texture slot.
    /// </summary>
    public Texture NullTextureRW2D { get; private set; }

    /// <summary>
    /// 16-byte buffer, fallback for an unmatched uniform buffer slot.
    /// </summary>
    public DeviceBuffer NullUniform { get; private set; }

    /// <summary>
    /// 4x anisotropic sampler owned by this device. Needs SamplerAnisotropy support.
    /// </summary>
    public Sampler Aniso4xSampler => RequireFeature(_aniso4xSampler, Features.SamplerAnisotropy, nameof(Aniso4xSampler), nameof(GraphicsDeviceFeatures.SamplerAnisotropy));

    /// <summary>
    /// 16-byte buffer, fallback for an unmatched structured read-only buffer slot.
    /// </summary>
    public DeviceBuffer NullStructured => RequireFeature(_nullStructuredRead, Features.StructuredBuffer, nameof(NullStructured), nameof(GraphicsDeviceFeatures.StructuredBuffer));

    /// <summary>
    /// 16-byte buffer, fallback for an unmatched structured read-write buffer slot.
    /// </summary>
    public DeviceBuffer NullStructuredRW => RequireFeature(_nullStructuredReadWrite, Features.StructuredBuffer, nameof(NullStructuredRW), nameof(GraphicsDeviceFeatures.StructuredBuffer));

    /// <summary>
    /// Creates and caches common device resources after creation.
    /// </summary>
    protected void PostDeviceCreated()
    {
        PointSampler = ResourceFactory.CreateSampler(SamplerDescription.Point);
        LinearSampler = ResourceFactory.CreateSampler(SamplerDescription.Linear);
        NullUniform = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        NullTexture2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        NullTextureRW2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage));

        if (Features.SamplerAnisotropy)
        {
            _aniso4xSampler = ResourceFactory.CreateSampler(SamplerDescription.Aniso4x);
        }

        if (Features.StructuredBuffer)
        {
            _nullStructuredRead = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly, 16));
            _nullStructuredReadWrite = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite, 16));
        }
    }

    private void DisposeDefaultResources()
    {
        PointSampler.Dispose();
        LinearSampler.Dispose();
        NullTexture2D.Dispose();
        NullTextureRW2D.Dispose();
        NullUniform.Dispose();
        _aniso4xSampler?.Dispose();
        _nullStructuredRead?.Dispose();
        _nullStructuredReadWrite?.Dispose();
    }

    private static T RequireFeature<T>(T resource, bool supported, string propertyName, string featureName) where T : class
    {
        if (!supported)
        {
            throw new RenderException(
                $"GraphicsDevice.{propertyName} cannot be used unless GraphicsDeviceFeatures.{featureName} is supported.");
        }

        Debug.Assert(resource != null);
        return resource;
    }
}
