using System;

namespace Prowl.Graphite;

/// <summary>
/// Bitmask of allowed uses for a buffer.
/// </summary>
[Flags]
public enum BufferUsage : byte
{
    /// <summary>
    /// Usable as vertex data source for draws. Enables returning it as a vertex buffer from ResolveSlot.
    /// </summary>
    VertexBuffer = 1 << 0,
    /// <summary>
    /// Usable as index data source for draws. Enables returning it as an index buffer from TryGetIndexBuffer.
    /// </summary>
    IndexBuffer = 1 << 1,
    /// <summary>
    /// Usable as a uniform buffer in a PropertySet.
    /// </summary>
    UniformBuffer = 1 << 2,
    /// <summary>
    /// Combinable with VertexBuffer, IndexBuffer, or IndirectBuffer so a compute shader can fill it. Requires UseTypedHlslBinding false (default).
    /// </summary>
    StructuredBufferReadOnly = 1 << 3,
    /// <summary>
    /// Combinable with VertexBuffer, IndexBuffer, or IndirectBuffer so a compute shader can fill it. Requires UseTypedHlslBinding false (default).
    /// </summary>
    StructuredBufferReadWrite = 1 << 4,
    /// <summary>
    /// Usable as indirect draw source for the *Indirect command methods. Cannot combine with Dynamic.
    /// </summary>
    IndirectBuffer = 1 << 5,
    /// <summary>
    /// Updated very frequently; can be mapped with MapMode.Write. Cannot combine with StructuredBufferReadWrite or IndirectBuffer.
    /// </summary>
    Dynamic = 1 << 6,
    /// <summary>
    /// Staging buffer for CPU transfer via GraphicsDevice.Map. Supports all MapMode values. Cannot combine with any other flag.
    /// </summary>
    Staging = 1 << 7,
}
