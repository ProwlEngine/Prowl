namespace Prowl.Veldrid.Tests;

// Minimal IVertexSource for tests: holds one buffer per layout slot, an optional index
// buffer, and a fixed topology. Mirrors what Samples/Shared/Mesh does, without the loading.
internal sealed class TestVertexSource : IVertexSource
{
    private readonly DeviceBuffer[] _vertexBuffers;
    private readonly DeviceBuffer _indexBuffer;
    private readonly IndexFormat _indexFormat;
    private readonly uint _indexCount;

    public PrimitiveTopology Topology { get; }

    public TestVertexSource(
        PrimitiveTopology topology,
        DeviceBuffer[] vertexBuffers,
        DeviceBuffer indexBuffer = null,
        IndexFormat indexFormat = IndexFormat.UInt16,
        uint indexCount = 0)
    {
        Topology = topology;
        _vertexBuffers = vertexBuffers;
        _indexBuffer = indexBuffer;
        _indexFormat = indexFormat;
        _indexCount = indexCount;
    }

    public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        => binding = new VertexBinding(_vertexBuffers[layoutSlot]);

    public bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        buffer = _indexBuffer;
        format = _indexFormat;
        indexCount = _indexCount;
        return _indexBuffer != null;
    }
}
