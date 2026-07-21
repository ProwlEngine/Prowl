namespace Prowl.Graphite;

/// <summary>
/// Kind of a bindable resource.
/// </summary>
public enum ResourceKind : byte
{
    /// <summary>
    /// Buffer bound as a uniform buffer. Can bind a subset via a buffer range.
    /// </summary>
    UniformBuffer,

    /// <summary>
    /// Buffer bound as a read-only storage buffer. Can bind a subset via a buffer range.
    /// </summary>
    StructuredBufferReadOnly,

    /// <summary>
    /// Buffer bound as a read-write storage buffer. Can bind a subset via a buffer range.
    /// </summary>
    StructuredBufferReadWrite,

    /// <summary>
    /// Read-only texture, via a Texture or TextureView.
    /// <remarks>Binding a Texture to a ReadWrite slot is same as binding a full-range TextureView in the same format.</remarks>
    /// </summary>
    TextureReadOnly,

    /// <summary>
    /// Read-write texture, via a Texture or TextureView.
    /// </summary>
    /// <remarks>Binding a Texture to a ReadWrite slot is same as binding a full-range TextureView in the same format.</remarks>
    TextureReadWrite,

    /// <summary>
    /// A sampler.
    /// </summary>
    Sampler,
}
