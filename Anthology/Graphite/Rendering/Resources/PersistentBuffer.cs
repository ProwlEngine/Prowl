namespace Prowl.Graphite.Rendering;

internal sealed class PersistentBuffer(RenderResourceID id, BufferUsage usage)
{
    public RenderResourceID Id { get; } = id;

    public DeviceBuffer? Buffer { get; private set; }

    public DeviceBuffer Resolve(GraphicsDevice device, RenderStore store, uint sizeInBytes)
    {
        if (Buffer != null && Buffer.SizeInBytes == sizeInBytes)
            return Buffer;
        DeviceBuffer created = device.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, usage));
        created.Name = RenderResourceID.ToString(Id);
        if (Buffer != null)
            store.Resized(Buffer, created);
        Buffer = created;
        return created;
    }

    public void Release(RenderStore store)
    {
        if (Buffer != null)
            store.Retire(Buffer);
        Buffer = null;
    }
}
