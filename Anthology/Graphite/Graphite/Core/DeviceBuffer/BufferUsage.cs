using System;

namespace Prowl.Graphite;

/// <summary>
/// Bitmask of buffer uses.
/// </summary>
[Flags]
public enum BufferUsage : byte
{
    /// <summary>
    /// Usable as vertex data source.
    /// </summary>
    VertexBuffer = 1 << 0,
    /// <summary>
    /// Usable as index data source.
    /// </summary>
    IndexBuffer = 1 << 1,
    /// <summary>
    /// Usable as a uniform buffer in a PropertySet.
    /// </summary>
    UniformBuffer = 1 << 2,
    /// <summary>
    /// Shader read-only structured buffer.
    /// </summary>
    StructuredBufferReadOnly = 1 << 3,
    /// <summary>
    /// Compute shader writable.
    /// </summary>
    StructuredBufferReadWrite = 1 << 4,
    /// <summary>
    /// Indirect draw source; cannot combine with Dynamic.
    /// </summary>
    IndirectBuffer = 1 << 5,
    /// <summary>
    /// Host-visible, persistently mapped memory for frequent CPU writes. Cannot combine with StructuredBufferReadWrite or IndirectBuffer.
    /// </summary>
    Dynamic = 1 << 6,
    /// <summary>
    /// Host-visible memory, cached when available, for CPU upload and readback. Cannot combine with other flags.
    /// </summary>
    Staging = 1 << 7,
}
