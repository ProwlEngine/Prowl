namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Describes a buffer a pass reads or writes as a graph resource. Both size and usage are significant:
/// a structured (storage) buffer is not interchangeable with a uniform, vertex, or index buffer, so the
/// usage flags are part of the resource's identity for allocation.
/// </summary>
public struct GraphBufferDesc
{
    /// <summary>Capacity in bytes.</summary>
    public uint SizeInBytes;

    /// <summary>Permitted uses of the buffer.</summary>
    public BufferUsage Usage;

    /// <summary>Element stride in bytes for a structured buffer; zero for other buffer types.</summary>
    public uint StructureByteStride;

    /// <summary>A structured (storage) buffer of the given element count and per-element stride.</summary>
    public static GraphBufferDesc Structured(uint elementCount, uint elementStride, bool readWrite = true) => new()
    {
        SizeInBytes = elementCount * elementStride,
        Usage = readWrite ? BufferUsage.StructuredBufferReadWrite : BufferUsage.StructuredBufferReadOnly,
        StructureByteStride = elementStride
    };

    /// <summary>A uniform buffer of the given byte size.</summary>
    public static GraphBufferDesc Uniform(uint sizeInBytes) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.UniformBuffer,
        StructureByteStride = 0
    };

    /// <summary>A buffer with explicit size, usage flags, and optional structured element stride.</summary>
    public static GraphBufferDesc Of(uint sizeInBytes, BufferUsage usage, uint structureByteStride = 0) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = usage,
        StructureByteStride = structureByteStride
    };

    internal readonly BufferDescription ToBufferDescription()
        => new(SizeInBytes, Usage, StructureByteStride);
}
