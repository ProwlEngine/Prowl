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

        return dst;
    }

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
