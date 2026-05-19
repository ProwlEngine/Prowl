namespace Prowl.Clay;

/// <summary>Texture wrap mode for U and V axes.</summary>
public enum TextureWrapMode
{
    /// <summary>Tile the texture (default).</summary>
    Repeat,
    /// <summary>Clamp UV to [0,1].</summary>
    Clamp,
    /// <summary>Mirror the texture on every integer crossing.</summary>
    Mirror,
}

/// <summary>Texture sampling filter mode.</summary>
public enum TextureFilterMode
{
    /// <summary>Nearest-neighbor sampling.</summary>
    Point,
    /// <summary>Bilinear sampling (linear within a single mip level).</summary>
    Bilinear,
    /// <summary>Trilinear sampling (linear across mip levels too).</summary>
    Trilinear,
}

/// <summary>
/// Sampler state for a <see cref="Texture"/>: wrap modes, filter modes, and a mipmap hint.
/// </summary>
public sealed class TextureSampler
{
    /// <summary>Wrap mode along the U axis.</summary>
    public TextureWrapMode WrapU { get; init; } = TextureWrapMode.Repeat;

    /// <summary>Wrap mode along the V axis.</summary>
    public TextureWrapMode WrapV { get; init; } = TextureWrapMode.Repeat;

    /// <summary>Filter used when the texture is minified.</summary>
    public TextureFilterMode MinFilter { get; init; } = TextureFilterMode.Trilinear;

    /// <summary>Filter used when the texture is magnified.</summary>
    public TextureFilterMode MagFilter { get; init; } = TextureFilterMode.Bilinear;

    /// <summary>True when the source declared a mipmapped sampler (engine should build a mip chain).</summary>
    public bool GenerateMipmaps { get; init; } = true;

    /// <summary>Default sampler (repeat, trilinear, mipmaps).</summary>
    public static TextureSampler Default { get; } = new();
}
