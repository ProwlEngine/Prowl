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
    /// Compute shader writable; requires UseTypedHlslBinding false.
    /// </summary>
    StructuredBufferReadOnly = 1 << 3,
    /// <summary>
    /// Compute shader writable; requires UseTypedHlslBinding false.
    /// </summary>
    StructuredBufferReadWrite = 1 << 4,
    /// <summary>
    /// Indirect draw source; cannot combine with Dynamic.
    /// </summary>
    IndirectBuffer = 1 << 5,
    /// <summary>
    /// Frequently updated; cannot combine with StructuredBufferReadWrite or IndirectBuffer.
    /// </summary>
    Dynamic = 1 << 6,
    /// <summary>
    /// Staging buffer for CPU transfers; cannot combine with other flags.
    /// </summary>
    Staging = 1 << 7,
}
