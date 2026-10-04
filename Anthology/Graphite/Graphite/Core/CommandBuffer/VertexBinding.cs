using System;

namespace Prowl.Graphite;

/// <summary>
/// Resolved vertex buffer binding for one layout slot, from ResolveSlot. Stride lives on the bound
/// program's layout, not here.
/// </summary>
public readonly struct VertexBinding
{
    /// <summary>
    /// Buffer to bind. Must be non-null, created with VertexBuffer usage.
    /// </summary>
    public readonly DeviceBuffer Buffer;

    /// <summary>
    /// Byte offset into Buffer where vertex data starts.
    /// </summary>
    public readonly uint Offset;

    /// <summary>
    /// Makes a VertexBinding.
    /// </summary>
    /// <param name="buffer">Buffer to bind.</param>
    /// <param name="offset">Byte offset into buffer.</param>
    public VertexBinding(DeviceBuffer buffer, uint offset = 0)
    {
        Buffer = buffer;
        Offset = offset;
    }
}
