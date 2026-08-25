// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Maps glTF materials into <see cref="IntermediateMaterial"/> entries.
/// </summary>
/// <remarks>
/// Handles every Khronos PBR extension mapped to a typed surface on Material:
/// <list type="bullet">
///   <item><c>KHR_materials_unlit</c></item>
///   <item><c>KHR_texture_transform</c> (per slot)</item>
///   <item><c>KHR_materials_emissive_strength</c></item>
///   <item><c>KHR_materials_clearcoat</c></item>
///   <item><c>KHR_materials_sheen</c></item>
///   <item><c>KHR_materials_transmission</c></item>
///   <item><c>KHR_materials_volume</c></item>
///   <item><c>KHR_materials_ior</c></item>
///   <item><c>KHR_materials_specular</c></item>
///   <item><c>KHR_materials_pbrSpecularGlossiness</c></item>
/// </list>
/// Anything else (incl. VRMC_*, KHR_materials_iridescence, KHR_materials_anisotropy) lands in
/// <see cref="IntermediateMaterial.RawExtensions"/> as a cloned <see cref="JsonElement"/>.
/// </remarks>
internal static class GltfMaterialMapper
{
    public static void MapAll(GltfDom dom, IntermediateScene scene, ImportContext ctx)
    {
        if (dom.Materials is null)
            return;

        for (int m = 0; m < dom.Materials.Length; m++)
        {
            scene.Materials.Add(Map(dom.Materials[m], ctx));
        }
    }

    private static IntermediateMaterial Map(GltfMaterial src, ImportContext ctx)
    {
        var dst = new IntermediateMaterial
        {
            Name = src.Name ?? string.Empty,
            DoubleSided = src.DoubleSided,
            AlphaMode = src.AlphaMode switch
            {
                "MASK" => MaterialAlphaMode.Mask,
                "BLEND" => MaterialAlphaMode.Blend,
                _ => MaterialAlphaMode.Opaque,
            },
            AlphaCutoff = src.AlphaCutoff ?? 0.5f,
        };

        if (src.PbrMetallicRoughness is { } pbr)
        {
            if (pbr.BaseColorFactor is { Length: >= 4 } bc)
                dst.BaseColor = new Color(bc[0], bc[1], bc[2], bc[3]);
            dst.Metallic = pbr.MetallicFactor ?? 1f;
            dst.Roughness = pbr.RoughnessFactor ?? 1f;
            dst.BaseColorTexture = MapTextureInfo(pbr.BaseColorTexture);
            dst.MetallicRoughnessTexture = MapTextureInfo(pbr.MetallicRoughnessTexture);
        }

        dst.NormalTexture = MapTextureInfo(src.NormalTexture);
        dst.NormalScale = src.NormalTexture?.Scale ?? 1f;
        dst.OcclusionTexture = MapTextureInfo(src.OcclusionTexture);
        dst.OcclusionStrength = src.OcclusionTexture?.Strength ?? 1f;

        if (src.EmissiveFactor is { Length: >= 3 } e)
            dst.EmissiveFactor = new Color(e[0], e[1], e[2], 1f);
        dst.EmissiveTexture = MapTextureInfo(src.EmissiveTexture);

        if (src.Extensions is not null)
        {
            foreach (var kvp in src.Extensions)
            {
                if (TryConsume(kvp.Key, kvp.Value, dst, ctx))
                    continue;
                dst.RawExtensions[kvp.Key] = kvp.Value.Clone();
            }
        }

        if (dst.SpecularGlossiness is { } specGloss)
            ApplySpecularGlossiness(specGloss, src, dst, ctx);

        return dst;
    }

    /// <summary>
    /// Fills the metallic-roughness core from a KHR_materials_pbrSpecularGlossiness material, so a
    /// consumer that only understands metal/rough still gets a usable surface instead of the
    /// all-defaults white metal it would otherwise see. The typed extension is left in place for
    /// consumers that would rather shade the original.
    /// </summary>
    /// <remarks>
    /// An authored <c>pbrMetallicRoughness</c> block wins when one is present. The extension
    /// nominally takes precedence, but that block is the exporter's own fallback for clients
    /// without the extension, produced with access to the source images. It can carry a properly
    /// baked metallic-roughness texture, which this conversion cannot synthesise: Clay never
    /// decodes image data, so only the factors and the diffuse texture reference can move across.
    /// </remarks>
    private static void ApplySpecularGlossiness(
        SpecularGlossinessExtension src, GltfMaterial srcJson, IntermediateMaterial dst, ImportContext ctx)
    {
        if (srcJson.PbrMetallicRoughness is not null)
        {
            ctx.Log.Info(
                $"Material '{dst.Name}' carries both pbrSpecularGlossiness and pbrMetallicRoughness; " +
                "using the authored metallic-roughness block, which the exporter wrote as the fallback.",
                "GltfMaterialMapper");
            return;
        }

        Color diffuse = src.DiffuseFactor;
        Color specular = src.SpecularFactor;

        float oneMinusSpecularStrength = 1f - MathF.Max(specular.R, MathF.Max(specular.G, specular.B));
        float metallic = SolveMetallic(
            PerceivedBrightness(diffuse),
            PerceivedBrightness(specular),
            oneMinusSpecularStrength);

        // Recover the base colour the metal/rough model would need to land on the same appearance,
        // blending the dielectric and metallic reconstructions by how metallic the solve came out.
        Color fromDiffuse = Scale(diffuse,
            oneMinusSpecularStrength / (1f - DielectricSpecular) / MathF.Max(1f - metallic, Epsilon));
        Color fromSpecular = Scale(
            Subtract(specular, DielectricSpecular * (1f - metallic)),
            1f / MathF.Max(metallic, Epsilon));

        float t = metallic * metallic;
        dst.BaseColor = new Color(
            Saturate(Lerp(fromDiffuse.R, fromSpecular.R, t)),
            Saturate(Lerp(fromDiffuse.G, fromSpecular.G, t)),
            Saturate(Lerp(fromDiffuse.B, fromSpecular.B, t)),
            Saturate(diffuse.A));

        dst.Metallic = Saturate(metallic);
        dst.Roughness = Saturate(1f - src.GlossinessFactor);

        // The diffuse map is the base colour map closely enough to be worth carrying; without it the
        // material imports untextured, which is the single worst part of the unconverted result.
        if (src.DiffuseTexture is { } diffuseTex)
            dst.BaseColorTexture = ToIntermediateSlot(diffuseTex);

        if (src.SpecularGlossinessTexture is not null)
        {
            ctx.Log.Warning(
                $"Material '{dst.Name}': the specular-glossiness texture cannot be converted to a " +
                "metallic-roughness texture without resampling the image, which the importer does not do. " +
                "Its metal and roughness come from the converted factors only.",
                "GltfMaterialMapper");
        }
    }

    /// <summary>Reflectance of a dielectric surface at normal incidence, the metal/rough model's constant.</summary>
    private const float DielectricSpecular = 0.04f;
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Solves the metallic value whose dielectric/metal blend reproduces the given diffuse and
    /// specular brightness. The positive root of the quadratic the two models agree on.
    /// </summary>
    private static float SolveMetallic(float diffuse, float specular, float oneMinusSpecularStrength)
    {
        if (specular < DielectricSpecular)
            return 0f;

        float a = DielectricSpecular;
        float b = diffuse * oneMinusSpecularStrength / (1f - DielectricSpecular) + specular - 2f * DielectricSpecular;
        float c = DielectricSpecular - specular;
        float d = MathF.Max(b * b - 4f * a * c, 0f);

        return Saturate((-b + MathF.Sqrt(d)) / (2f * a));
    }

    private static float PerceivedBrightness(Color c) =>
        MathF.Sqrt(0.299f * c.R * c.R + 0.587f * c.G * c.G + 0.114f * c.B * c.B);

    private static Color Scale(Color c, float s) => new(c.R * s, c.G * s, c.B * s, c.A);
    private static Color Subtract(Color c, float s) => new(c.R - s, c.G - s, c.B - s, c.A);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Saturate(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    private static IntermediateTextureSlot ToIntermediateSlot(MaterialTextureSlot s) => new()
    {
        TextureIndex = s.TextureIndex,
        UVChannel = s.UVChannel,
        Offset = s.Offset,
        Scale = s.Scale,
        Rotation = s.Rotation,
    };

    private static bool TryConsume(string name, JsonElement value, IntermediateMaterial dst, ImportContext ctx)
    {
        switch (name)
        {
            case "KHR_materials_unlit":
                dst.Unlit = true;
                return true;

            case "KHR_materials_emissive_strength":
                if (value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty("emissiveStrength", out var es) &&
                    es.ValueKind == JsonValueKind.Number)
                {
                    dst.EmissiveStrength = es.GetSingle();
                }
                return true;

            case "KHR_materials_clearcoat":
                dst.Clearcoat = ReadClearcoat(value);
                return true;

            case "KHR_materials_sheen":
                dst.Sheen = ReadSheen(value);
                return true;

            case "KHR_materials_transmission":
                dst.Transmission = ReadTransmission(value);
                return true;

            case "KHR_materials_volume":
                dst.Volume = ReadVolume(value);
                return true;

            case "KHR_materials_ior":
                dst.Ior = ReadIor(value);
                return true;

            case "KHR_materials_specular":
                dst.Specular = ReadSpecular(value);
                return true;

            case "KHR_materials_pbrSpecularGlossiness":
                dst.SpecularGlossiness = ReadSpecularGlossiness(value);
                return true;
        }

        _ = ctx;
        return false;
    }

    private static IntermediateTextureSlot? MapTextureInfo(GltfTextureInfo? info)
    {
        if (info is null)
            return null;

        var slot = new IntermediateTextureSlot
        {
            TextureIndex = info.Index,
            UVChannel = info.TexCoord,
        };

        if (info.Extensions is not null &&
            info.Extensions.TryGetValue("KHR_texture_transform", out JsonElement xform))
        {
            ApplyTextureTransform(xform, slot);
        }

        return slot;
    }

    private static MaterialTextureSlot? MapSlotJson(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty("index", out var idx))
            return null;
        var slot = new IntermediateTextureSlot
        {
            TextureIndex = idx.GetInt32(),
            UVChannel = obj.TryGetProperty("texCoord", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0,
        };
        if (obj.TryGetProperty("extensions", out var exts) &&
            exts.ValueKind == JsonValueKind.Object &&
            exts.TryGetProperty("KHR_texture_transform", out var xform))
        {
            ApplyTextureTransform(xform, slot);
        }
        return CopyToPublic(slot);
    }

    private static MaterialTextureSlot CopyToPublic(IntermediateTextureSlot s) => new()
    {
        TextureIndex = s.TextureIndex,
        UVChannel = s.UVChannel,
        Offset = s.Offset,
        Scale = s.Scale,
        Rotation = s.Rotation,
    };

    private static void ApplyTextureTransform(JsonElement xform, IntermediateTextureSlot slot)
    {
        if (xform.ValueKind != JsonValueKind.Object)
            return;

        if (xform.TryGetProperty("offset", out var off) && off.ValueKind == JsonValueKind.Array && off.GetArrayLength() == 2)
            slot.Offset = new Float2(off[0].GetSingle(), off[1].GetSingle());

        if (xform.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Array && sc.GetArrayLength() == 2)
            slot.Scale = new Float2(sc[0].GetSingle(), sc[1].GetSingle());

        if (xform.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number)
            slot.Rotation = r.GetSingle();

        if (xform.TryGetProperty("texCoord", out var tc) && tc.ValueKind == JsonValueKind.Number)
            slot.UVChannel = tc.GetInt32();
    }

    private static ClearcoatExtension? ReadClearcoat(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        return new ClearcoatExtension
        {
            Factor = GetFloat(v, "clearcoatFactor", 0f),
            Roughness = GetFloat(v, "clearcoatRoughnessFactor", 0f),
            FactorTexture = GetSlot(v, "clearcoatTexture"),
            RoughnessTexture = GetSlot(v, "clearcoatRoughnessTexture"),
            NormalTexture = GetSlot(v, "clearcoatNormalTexture"),
        };
    }

    private static SheenExtension? ReadSheen(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        Color color = new(0f, 0f, 0f, 1f);
        if (v.TryGetProperty("sheenColorFactor", out var cc) && cc.ValueKind == JsonValueKind.Array && cc.GetArrayLength() >= 3)
            color = new Color(cc[0].GetSingle(), cc[1].GetSingle(), cc[2].GetSingle(), 1f);
        return new SheenExtension
        {
            ColorFactor = color,
            RoughnessFactor = GetFloat(v, "sheenRoughnessFactor", 0f),
            ColorTexture = GetSlot(v, "sheenColorTexture"),
            RoughnessTexture = GetSlot(v, "sheenRoughnessTexture"),
        };
    }

    private static TransmissionExtension? ReadTransmission(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        return new TransmissionExtension
        {
            Factor = GetFloat(v, "transmissionFactor", 0f),
            FactorTexture = GetSlot(v, "transmissionTexture"),
        };
    }

    private static VolumeExtension? ReadVolume(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        Color attColor = new(1f, 1f, 1f, 1f);
        if (v.TryGetProperty("attenuationColor", out var cc) && cc.ValueKind == JsonValueKind.Array && cc.GetArrayLength() >= 3)
            attColor = new Color(cc[0].GetSingle(), cc[1].GetSingle(), cc[2].GetSingle(), 1f);
        return new VolumeExtension
        {
            ThicknessFactor = GetFloat(v, "thicknessFactor", 0f),
            ThicknessTexture = GetSlot(v, "thicknessTexture"),
            AttenuationDistance = GetFloat(v, "attenuationDistance", float.PositiveInfinity),
            AttenuationColor = attColor,
        };
    }

    private static IorExtension? ReadIor(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        return new IorExtension { Ior = GetFloat(v, "ior", 1.5f) };
    }

    private static SpecularExtension? ReadSpecular(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        Color color = new(1f, 1f, 1f, 1f);
        if (v.TryGetProperty("specularColorFactor", out var cc) && cc.ValueKind == JsonValueKind.Array && cc.GetArrayLength() >= 3)
            color = new Color(cc[0].GetSingle(), cc[1].GetSingle(), cc[2].GetSingle(), 1f);
        return new SpecularExtension
        {
            Factor = GetFloat(v, "specularFactor", 1f),
            ColorFactor = color,
            FactorTexture = GetSlot(v, "specularTexture"),
            ColorTexture = GetSlot(v, "specularColorTexture"),
        };
    }

    private static SpecularGlossinessExtension? ReadSpecularGlossiness(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        Color diffuse = new(1f, 1f, 1f, 1f);
        if (v.TryGetProperty("diffuseFactor", out var df) && df.ValueKind == JsonValueKind.Array && df.GetArrayLength() >= 4)
            diffuse = new Color(df[0].GetSingle(), df[1].GetSingle(), df[2].GetSingle(), df[3].GetSingle());

        Color spec = new(1f, 1f, 1f, 1f);
        if (v.TryGetProperty("specularFactor", out var sf) && sf.ValueKind == JsonValueKind.Array && sf.GetArrayLength() >= 3)
            spec = new Color(sf[0].GetSingle(), sf[1].GetSingle(), sf[2].GetSingle(), 1f);

        return new SpecularGlossinessExtension
        {
            DiffuseFactor = diffuse,
            SpecularFactor = spec,
            GlossinessFactor = GetFloat(v, "glossinessFactor", 1f),
            DiffuseTexture = GetSlot(v, "diffuseTexture"),
            SpecularGlossinessTexture = GetSlot(v, "specularGlossinessTexture"),
        };
    }

    private static float GetFloat(JsonElement obj, string name, float fallback) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static MaterialTextureSlot? GetSlot(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? MapSlotJson(v) : null;
}
