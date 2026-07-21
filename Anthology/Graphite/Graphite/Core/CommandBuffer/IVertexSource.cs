namespace Prowl.Graphite;

/// <summary>
/// User-supplied vertex buffers, index buffer, and topology for the backend. Bound via SetVertexSource; queried on every draw, never cached.
/// </summary>
public interface IVertexSource
{
    /// <summary>
    /// Topology for the next draw. Queried every draw call, can change between calls.
    /// </summary>
    PrimitiveTopology Topology { get; }

    /// <summary>
    /// Gets the vertex buffer + offset for a layout slot. layoutSlot indexes the shader's vertex layouts; full layout passed so you can dispatch on element identity instead of slot index.
    /// </summary>
    /// <remarks>
    /// Buffer must never be null. No such thing as "no buffer for this slot" - use a zero-sized placeholder for no-vertex draws.
    /// </remarks>
    /// <param name="layoutSlot">Index into the shader's vertex layouts array.</param>
    /// <param name="layout">Layout description for the slot.</param>
    /// <param name="binding">Resolved vertex buffer binding.</param>
    void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding);

    /// <summary>
    /// Gets the index buffer for the next indexed draw. False if none.
    /// </summary>
    /// <remarks>
    /// Only called on indexed draw paths. Re-queried every draw even if unchanged - no caching.
    /// </remarks>
    /// <param name="buffer">Index buffer, if true returned.</param>
    /// <param name="format">Index format, if true returned.</param>
    /// <param name="indexCount">Index count.</param>
    /// <returns>True if an index buffer exists.</returns>
    bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount);
}
