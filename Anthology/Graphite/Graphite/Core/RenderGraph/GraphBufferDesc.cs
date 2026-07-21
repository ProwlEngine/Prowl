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

    /// <summary>Element stride for structured buffers; 0 otherwise.</summary>
    public uint StructureByteStride;

    /// <summary>Structured (storage) buffer with given element count and stride.</summary>
    public static GraphBufferDesc Structured(uint elementCount, uint elementStride, bool readWrite = true) => new()
    {
        SizeInBytes = elementCount * elementStride,
        Usage = readWrite ? BufferUsage.StructuredBufferReadWrite : BufferUsage.StructuredBufferReadOnly,
        StructureByteStride = elementStride
    };

    /// <summary>Uniform buffer of given byte size.</summary>
    public static GraphBufferDesc Uniform(uint sizeInBytes) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.UniformBuffer,
        StructureByteStride = 0
    };

    /// <summary>Buffer with explicit size, usage, and optional structured stride.</summary>
    public static GraphBufferDesc Of(uint sizeInBytes, BufferUsage usage, uint structureByteStride = 0) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = usage,
        StructureByteStride = structureByteStride
    };

    internal readonly BufferDescription ToBufferDescription()
        => new(SizeInBytes, Usage, StructureByteStride);
}
