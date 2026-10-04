namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
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
    /// 16-byte buffer, fallback for an unmatched structured read-write buffer slot.
    /// </summary>
    public DeviceBuffer NullStructuredRW => _nullStructuredReadWrite;

    /// <summary>
    /// Creates and caches common device resources after creation.
    /// </summary>
    protected void PostDeviceCreated()
    {
        PointSampler = ResourceFactory.CreateSampler(SamplerDescription.Point);
        LinearSampler = ResourceFactory.CreateSampler(SamplerDescription.Linear);
        NullTexture2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        NullTextureRW2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage));

        _nullStructuredRead = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly, 16));
        _nullStructuredReadWrite = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite, 16));
    }

    private void DisposeDefaultResources()
    {
        PointSampler.Dispose();
        LinearSampler.Dispose();
        NullTexture2D.Dispose();
        NullTextureRW2D.Dispose();
        _nullStructuredRead.Dispose();
        _nullStructuredReadWrite.Dispose();
    }
}
