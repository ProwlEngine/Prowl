namespace Prowl.Graphite;

/// <summary>
/// Bindable resource kind.
/// </summary>
public enum ResourceKind : byte
{
    /// <summary>
    /// Uniform buffer; subset via buffer range.
    /// </summary>
    UniformBuffer,

    /// <summary>
    /// Read-only storage buffer; subset via buffer range.
    /// </summary>
    StructuredBufferReadOnly,

    /// <summary>
    /// Read-write storage buffer; subset via buffer range.
    /// </summary>
    StructuredBufferReadWrite,

    /// <summary>
    /// Read-only texture (Texture or TextureView).
    /// <remarks>Binding Texture to ReadWrite slot = full-range TextureView.</remarks>
    /// </summary>
    TextureReadOnly,

    /// <summary>
    /// Read-write texture (Texture or TextureView).
    /// </summary>
    /// <remarks>Binding Texture to ReadWrite slot = full-range TextureView.</remarks>
    TextureReadWrite,

    /// <summary>
    /// Sampler.
    /// </summary>
    Sampler,
}
