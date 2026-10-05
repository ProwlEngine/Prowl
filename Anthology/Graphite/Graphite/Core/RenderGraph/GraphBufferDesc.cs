namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Buffer a pass reads/writes as a graph resource. Usage flags count as part of the resource identity
/// since structured buffers aren't interchangeable with uniform/vertex/index buffers.
/// </summary>
public struct GraphBufferDesc
{
    /// <summary>Size in bytes.</summary>
    public uint SizeInBytes;

    /// <summary>Allowed buffer uses.</summary>
    public BufferUsage Usage;

    /// <summary>Structured (storage) buffer with given element count and stride.</summary>
    public static GraphBufferDesc Structured(uint elementCount, uint elementStride, bool readWrite = true) => new()
    {
        SizeInBytes = elementCount * elementStride,
        Usage = readWrite ? BufferUsage.StructuredBufferReadWrite : BufferUsage.StructuredBufferReadOnly
    };

    /// <summary>Uniform buffer of given byte size.</summary>
    public static GraphBufferDesc Uniform(uint sizeInBytes) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.UniformBuffer
    };

    /// <summary>Buffer with explicit size and usage.</summary>
    public static GraphBufferDesc Of(uint sizeInBytes, BufferUsage usage) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = usage
    };

    internal readonly BufferDescription ToBufferDescription()
        => new(SizeInBytes, Usage);
}
