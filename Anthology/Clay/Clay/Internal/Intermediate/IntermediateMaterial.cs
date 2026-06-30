using System.Text.Json;
using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Writable material form. Mirrors the public <see cref="Material"/> shape but with mutable
/// fields so format readers and post-process steps can fill it in piecewise.
/// </summary>
internal sealed class IntermediateMaterial
{
    public string Name { get; set; } = string.Empty;
    public MaterialAlphaMode AlphaMode { get; set; } = MaterialAlphaMode.Opaque;
    public float AlphaCutoff { get; set; } = 0.5f;
    public bool DoubleSided { get; set; }
    public bool Unlit { get; set; }

    public Color BaseColor { get; set; } = new Color(1f, 1f, 1f, 1f);
    public IntermediateTextureSlot? BaseColorTexture { get; set; }
    public float Metallic { get; set; } = 1f;
    public float Roughness { get; set; } = 1f;
    public IntermediateTextureSlot? MetallicRoughnessTexture { get; set; }

    public IntermediateTextureSlot? NormalTexture { get; set; }
    public float NormalScale { get; set; } = 1f;
    public IntermediateTextureSlot? OcclusionTexture { get; set; }
    public float OcclusionStrength { get; set; } = 1f;
    public Color EmissiveFactor { get; set; } = new Color(0f, 0f, 0f, 1f);
    public IntermediateTextureSlot? EmissiveTexture { get; set; }
    public float EmissiveStrength { get; set; } = 1f;

    public Dictionary<string, JsonElement> RawExtensions { get; } = new();

    public ClearcoatExtension? Clearcoat { get; set; }
    public SheenExtension? Sheen { get; set; }
    public TransmissionExtension? Transmission { get; set; }
    public VolumeExtension? Volume { get; set; }
    public IorExtension? Ior { get; set; }
    public SpecularExtension? Specular { get; set; }
    public SpecularGlossinessExtension? SpecularGlossiness { get; set; }
}

internal sealed class IntermediateTextureSlot
{
    public int TextureIndex { get; set; }
    public int UVChannel { get; set; }
    public Float2 Offset { get; set; } = Float2.Zero;
    public Float2 Scale { get; set; } = Float2.One;
    public float Rotation { get; set; }
}
