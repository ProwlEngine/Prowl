namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private readonly ExecutionPool<BufferDescription, DeviceBuffer> _transientBufferPool;
    private readonly ExecutionPool<RenderTextureDescription, RenderTexture> _transientTexturePool;

    internal GraphicsDevice()
    {
        _transientBufferPool = new(this, CreateTransientBuffer, buffer => buffer.Dispose(), "transient buffer");
        _transientTexturePool = new(this, desc => ResourceFactory.CreateRenderTexture(desc), texture => texture.Dispose(), "transient texture");
    }

    /// <summary>
    /// Rents a buffer that returns to a description-keyed pool once the task finishes on the GPU.
    /// Never reused while still in flight.
    /// </summary>
    public DeviceBuffer RentTransientBuffer(ExecutionTask task, in BufferDescription desc)
    {
        ValidationHelpers.RequireNotNull(this, task, nameof(task), nameof(RentTransientBuffer));
        if (desc.SizeInBytes == 0)
            throw new RenderException("Cannot rent a transient buffer with a zero size.");

        return _transientBufferPool.Rent(desc, task.Id);
    }

    internal RenderTexture RentGraphTransientRenderTexture(ExecutionTask task, in RenderTextureDescription desc)
    {
        ValidationHelpers.RequireNotNull(this, task, nameof(task), nameof(RentGraphTransientRenderTexture));
        if (desc.Width == 0 || desc.Height == 0)
            throw new RenderException("Cannot rent a transient texture with a zero width or height.");
        if (desc.ColorFormats.Length == 0 && !desc.Depth)
            throw new RenderException("Cannot rent a transient texture bundle with no color attachments and no depth attachment.");

        return _transientTexturePool.Rent(desc, task.Id);
    }

    private DeviceBuffer CreateTransientBuffer(BufferDescription desc)
    {
        DeviceBuffer buffer = ResourceFactory.CreateBuffer(desc);
        buffer.SetTransientWrites(true);
        return buffer;
    }
}
