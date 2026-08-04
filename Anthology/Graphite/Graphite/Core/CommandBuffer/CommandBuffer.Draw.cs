namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>Draws with current bound state, no index buffer.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    public void Draw(uint vertexCount) => Draw(vertexCount, 1, 0, 0);

    /// <summary>Draws with current bound state, no index buffer.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    /// <param name="instanceCount">Instance count.</param>
    /// <param name="vertexStart">First vertex.</param>
    /// <param name="instanceStart">First instance.</param>
    public void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        Draw_PreDrawValidation();
        DrawCore(vertexCount, instanceCount, vertexStart, instanceStart);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.Draw, vertexCount, instanceCount, drawCount: 1, isIndirect: false, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }

    private protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart);

    /// <summary>Draws indexed primitives with current bound state.</summary>
    public void DrawIndexed() => DrawIndexed(1, 0, 0, 0);

    /// <summary>Draws indexed primitives with current bound state.</summary>
    /// <param name="instanceCount">Instance count.</param>
    /// <param name="indexStart">Indices to skip in index buffer.</param>
    /// <param name="vertexOffset">Added to each index read.</param>
    /// <param name="instanceStart">First instance.</param>
    public void DrawIndexed(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        DrawIndexed_CheckIndexBuffer();
        Draw_PreDrawValidation();
        DrawIndexed_CheckBaseVertexInstance(vertexOffset, instanceStart);

        DrawIndexedCore(instanceCount, indexStart, vertexOffset, instanceStart);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndexed, _currentIndexCount, instanceCount, drawCount: 1, isIndirect: false, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }

    private protected abstract void DrawIndexedCore(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart);

    /// <summary>Issues indirect draws from buffer. Data must match IndirectDrawArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read. Needs IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset to start reading. Multiple of 4.</param>
    /// <param name="drawCount">Draw commands to issue.</param>
    /// <param name="stride">Byte stride between commands. Multiple of 4, bigger than IndirectDrawArguments.</param>
    public unsafe void DrawIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        DrawIndirect_CheckSupport();
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DrawIndirect_CheckStride(stride, sizeof(IndirectDrawArguments));
        Draw_PreDrawValidation();

        DrawIndirectCore(indirectBuffer, offset, drawCount, stride);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndirect, vertexOrIndexCount: 0, instanceCount: 0, drawCount, isIndirect: true, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }


    /// <summary>Backend indirect draw.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="drawCount">Draw count.</param>
    /// <param name="stride">Byte stride.</param>
    private protected abstract void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

    /// <summary>Issues indirect indexed draws from buffer. Data must match IndirectDrawIndexedArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read. Needs IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset to start reading. Multiple of 4.</param>
    /// <param name="drawCount">Draw commands to issue.</param>
    /// <param name="stride">Byte stride between commands. Multiple of 4, bigger than IndirectDrawIndexedArguments.</param>
    public unsafe void DrawIndexedIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        DrawIndirect_CheckSupport();
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DrawIndirect_CheckStride(stride, sizeof(IndirectDrawIndexedArguments));
        Draw_PreDrawValidation();

        DrawIndexedIndirectCore(indirectBuffer, offset, drawCount, stride);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndexedIndirect, vertexOrIndexCount: 0, instanceCount: 0, drawCount, isIndirect: true, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }


    /// <summary>Backend indirect indexed draw.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="drawCount">Draw count.</param>
    /// <param name="stride">Byte stride.</param>
    private protected abstract void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

    /// <summary>Dispatches compute with current bound state.</summary>
    /// <param name="groupCountX">Thread group count X.</param>
    /// <param name="groupCountY">Thread group count Y.</param>
    /// <param name="groupCountZ">Thread group count Z.</param>
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        DispatchCore(groupCountX, groupCountY, groupCountZ);

        Execution?.Device.Profiler?.RecordDispatch(
            ProfilerInfo, new DispatchCallInfo(groupCountX, groupCountY, groupCountZ, isIndirect: false));
    }

    private protected abstract void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ);

    /// <summary>Issues indirect compute dispatch from buffer. Data must match IndirectDispatchArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read. Needs IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset to start reading. Multiple of 4.</param>
    public void DispatchIndirect(DeviceBuffer indirectBuffer, uint offset)
    {
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DispatchIndirectCore(indirectBuffer, offset);

        Execution?.Device.Profiler?.RecordDispatch(
            ProfilerInfo, new DispatchCallInfo(0, 0, 0, isIndirect: true));
    }


    /// <summary>Backend indirect dispatch.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    private protected abstract void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset);
}
