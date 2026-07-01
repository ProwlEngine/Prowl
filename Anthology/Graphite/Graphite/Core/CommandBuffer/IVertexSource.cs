namespace Prowl.Graphite;

/// <summary>
/// A user-implemented provider that supplies per-draw vertex buffers, the optional
/// index buffer, and the primitive topology to the backend. Bound on a
/// <see cref="CommandBuffer"/> via <see cref="CommandBuffer.SetVertexSource"/>; the
/// backend queries the source on every subsequent draw call and does not cache the
/// results across draws.
/// </summary>
public interface IVertexSource
{
    /// <summary>
    /// The primitive topology for the next draw issued with this source bound.
    /// Queried by the backend on every draw call. Implementors may return a
    /// different value across calls; the backend does not cache.
    /// </summary>
    PrimitiveTopology Topology { get; }

    /// <summary>
    /// Resolve the vertex buffer + offset for the given layout slot.
    /// <paramref name="layoutSlot"/> is the index into the bound shader's
    /// <see cref="GraphicsProgram.VertexLayouts"/>. The full
    /// <see cref="VertexLayoutDescription"/> is passed so an implementation
    /// can dispatch on layout element identity (each element's
    /// <see cref="VertexElementDescription.Name"/> is a
    /// <see cref="VertexAttributeID"/>) rather than slot index.
    /// </summary>
    /// <remarks>
    /// Must always produce a non-null <see cref="VertexBinding.Buffer"/>.
    /// "No vertex buffer for this slot" is not a representable state; sources
    /// that want a no-vertex draw must return a zero-sized placeholder buffer.
    /// </remarks>
    /// <param name="layoutSlot">The index of the layout in the bound shader's vertex layouts array.</param>
    /// <param name="layout">The full layout description for the requested slot.</param>
    /// <param name="binding">The resolved vertex buffer binding for the slot.</param>
    void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding);

    /// <summary>
    /// Resolve the index buffer for the next indexed draw. Returns
    /// <c>false</c> if the source has no index buffer.
    /// </summary>
    /// <remarks>
    /// Called by the backend only on indexed draw paths
    /// (<see cref="CommandBuffer.DrawIndexed()"/>, <see cref="CommandBuffer.DrawIndexedIndirect"/>).
    /// Backends that already resolved this on a previous draw still re-query;
    /// no caching across draws.
    /// </remarks>
    /// <param name="buffer">The resolved index buffer, when the return value is <c>true</c>.</param>
    /// <param name="format">The resolved index format, when the return value is <c>true</c>.</param>
    /// <param name="indexCount">The index count of the buffer.</param>
    /// <returns><c>true</c> if an index buffer is available; otherwise <c>false</c>.</returns>
    bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount);
}
