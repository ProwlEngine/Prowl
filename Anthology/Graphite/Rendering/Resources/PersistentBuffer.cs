namespace Prowl.Graphite.Rendering;

/// <summary>A graph buffer resolved before the graph runs, so the CPU can queue writes to it. A new size keeps its contents.</summary>
internal sealed class PersistentBuffer(RenderResourceID id, BufferUsage usage, GraphInstanceKey key)
{
    public RenderResourceID Id { get; } = id;

    public GraphInstanceKey Key { get; } = key;

    public DeviceBuffer? Buffer { get; private set; }

    public GraphBufferDesc Description => GraphBufferDesc.Of(Buffer?.SizeInBytes ?? 0, usage);

    public DeviceBuffer Resolve(GraphicsDevice device, GraphStore store, uint sizeInBytes)
    {
        Buffer = store.ResolvePersistent(device, Id, GraphBufferDesc.Of(sizeInBytes, usage), Key);
        return Buffer;
    }
}
