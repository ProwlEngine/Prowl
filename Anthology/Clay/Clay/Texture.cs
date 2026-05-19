namespace Prowl.Clay;

/// <summary>
/// CPU-side description of a texture referenced by a <see cref="Material"/>.
/// </summary>
/// <remarks>
/// Image bytes are NOT decoded. External references expose <see cref="SourcePath"/>; embedded
/// images expose <see cref="EncodedBytes"/> holding the original encoded file (PNG/JPG/KTX2/etc.).
/// The engine performs decoding and GPU upload.
/// </remarks>
public sealed class Texture
{
    /// <summary>Texture name, or <c>null</c> when the source did not name it.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Resolved absolute path to the source image file when the texture references an external file,
    /// or <c>null</c> when the texture is only available via <see cref="EncodedBytes"/>.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Encoded image bytes (PNG/JPG/KTX2/etc.) when the texture is embedded in the source file
    /// (GLB chunk, data URI) or when the <c>EmbedTextures</c> post-process step was run, otherwise <c>null</c>.
    /// </summary>
    public byte[]? EncodedBytes { get; init; }

    /// <summary>MIME type of the image data (e.g. <c>image/png</c>), when known.</summary>
    public string? MimeType { get; init; }

    /// <summary>Sampler state: wrap modes, filter modes, mipmap generation hint.</summary>
    public TextureSampler Sampler { get; init; } = TextureSampler.Default;
}
