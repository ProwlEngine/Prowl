namespace Prowl.Graphite.Rendering;

/// <summary>
/// A buffer kept across frames that grows or shrinks to the size asked. A resize copies what fits into the new buffer at
/// the start of the frame, unless keepContents is false, when the old one is only retired, for buffers refilled whole
/// whenever their size changes.
/// </summary>
internal sealed class PersistentBuffer(RenderResourceID id, BufferUsage usage, bool keepContents = true)
{
    public RenderResourceID Id { get; } = id;

    public DeviceBuffer? Buffer { get; private set; }

    public DeviceBuffer Resolve(GraphicsDevice device, RenderStore store, uint sizeInBytes)
    {
        if (Buffer != null && Buffer.SizeInBytes == sizeInBytes)
            return Buffer;
        DeviceBuffer created = device.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, usage));
        created.Name = RenderResourceID.ToString(Id) ?? string.Empty;
        if (Buffer != null && keepContents)
            store.Resized(Buffer, created);
        else if (Buffer != null)
            store.Retire(Buffer);
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
