namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private DeviceBuffer _nullStructuredReadWrite;

    internal Sampler LinearSampler { get; private set; }

    internal Texture NullTexture2D { get; private set; }

    internal Texture NullTextureRW2D { get; private set; }

    internal DeviceBuffer NullStructuredRW => _nullStructuredReadWrite;

    /// <summary>
    /// Creates and caches common device resources after creation.
    /// </summary>
    protected void PostDeviceCreated()
    {
        LinearSampler = ResourceFactory.CreateSampler(SamplerDescription.Linear);
        NullTexture2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        NullTextureRW2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage));

        _nullStructuredReadWrite = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
    }

    private void DisposeDefaultResources()
    {
        LinearSampler.Dispose();
        NullTexture2D.Dispose();
        NullTextureRW2D.Dispose();
        _nullStructuredReadWrite.Dispose();
    }
}
