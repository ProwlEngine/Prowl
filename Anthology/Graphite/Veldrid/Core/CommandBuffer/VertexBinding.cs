using System;

namespace Prowl.Veldrid;

/// <summary>
/// The resolved vertex buffer binding for a single layout slot returned from
/// <see cref="IVertexSource.ResolveSlot"/>. The stride is owned by the bound
/// <see cref="GraphicsProgram"/>'s <see cref="VertexLayoutDescription"/> and is not
/// carried on this struct.
/// </summary>
public readonly struct VertexBinding : IEquatable<VertexBinding>
{
    /// <summary>
    /// The <see cref="DeviceBuffer"/> to bind for the slot. Must be non-null and
    /// must have been created with <see cref="BufferUsage.VertexBuffer"/>.
    /// </summary>
    public readonly DeviceBuffer Buffer;

    /// <summary>
    /// The byte offset from the start of <see cref="Buffer"/> at which vertex data begins.
    /// </summary>
    public readonly uint Offset;

    /// <summary>
    /// Constructs a new <see cref="VertexBinding"/>.
    /// </summary>
    /// <param name="buffer">The <see cref="DeviceBuffer"/> to bind for the slot.</param>
    /// <param name="offset">The byte offset from the start of <paramref name="buffer"/>.</param>
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
    public override bool Equals(object obj) => obj is VertexBinding o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Buffer, Offset);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(VertexBinding a, VertexBinding b) => a.Equals(b);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(VertexBinding a, VertexBinding b) => !a.Equals(b);
}
