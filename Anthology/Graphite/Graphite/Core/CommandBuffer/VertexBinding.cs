using System;

namespace Prowl.Graphite;

/// <summary>
/// Resolved vertex buffer binding for one layout slot, from ResolveSlot. Stride lives on the bound
/// program's layout, not here.
/// </summary>
public readonly struct VertexBinding : IEquatable<VertexBinding>
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

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public bool Equals(VertexBinding other)
        => ReferenceEquals(Buffer, other.Buffer) && Offset == other.Offset;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VertexBinding o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Buffer, Offset);

    /// <summary>Equality op.</summary>
    public static bool operator ==(VertexBinding a, VertexBinding b) => a.Equals(b);

    /// <summary>Inequality op.</summary>
    public static bool operator !=(VertexBinding a, VertexBinding b) => !a.Equals(b);
}
