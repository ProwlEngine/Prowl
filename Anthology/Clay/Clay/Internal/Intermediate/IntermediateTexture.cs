using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

internal sealed class IntermediateTexture
{
    public string? Name { get; set; }
    public string? SourcePath { get; set; }
    public byte[]? EncodedBytes { get; set; }
    public string? MimeType { get; set; }
    public IntermediateTextureSampler Sampler { get; set; } = new();

    // FBX-style UV transform on the texture itself (translation, uniform scale, rotation in
    // radians). Copied onto each IntermediateTextureSlot that references this texture so the
    // public Material's per-slot Offset/Scale/Rotation matches what the source authored.
    public Float2 UVOffset { get; set; } = Float2.Zero;
    public Float2 UVScale { get; set; } = Float2.One;
    public float UVRotation { get; set; }
}

internal sealed class IntermediateTextureSampler
{
    public TextureWrapMode WrapU { get; set; } = TextureWrapMode.Repeat;
    public TextureWrapMode WrapV { get; set; } = TextureWrapMode.Repeat;
    public TextureFilterMode MinFilter { get; set; } = TextureFilterMode.Trilinear;
    public TextureFilterMode MagFilter { get; set; } = TextureFilterMode.Bilinear;
    public bool GenerateMipmaps { get; set; } = true;
}
