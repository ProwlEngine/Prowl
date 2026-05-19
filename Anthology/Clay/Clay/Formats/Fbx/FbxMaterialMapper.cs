using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Maps FBX <c>Material</c> objects (Phong / Lambert) onto our PBR intermediate. Conversion is
/// heuristic: DiffuseColor -&gt; BaseColor, Shininess/SpecularColor -&gt; Roughness, Emissive -&gt;
/// EmissiveFactor. Textures are wired up via OP connections from <c>Texture</c> objects to the
/// material's named property ("DiffuseColor", "NormalMap", etc.).
/// </summary>
internal static class FbxMaterialMapper
{
    public sealed class MaterialMapping
    {
        /// <summary>FBX material id -&gt; index in scene.Materials.</summary>
        public Dictionary<long, int> MaterialIndex { get; } = new();
    }

    public static MaterialMapping MapAll(FbxDocument doc, IntermediateScene scene, FbxTextureMapper.TextureMapping textureMapping, ImportContext ctx)
    {
        var result = new MaterialMapping();
        foreach (var obj in doc.Objects.Values)
        {
            if (obj.ObjectType != "Material") continue;
            int idx = scene.Materials.Count;
            scene.Materials.Add(BuildMaterial(obj, doc, scene, textureMapping, ctx));
            result.MaterialIndex[obj.Id] = idx;
        }
        return result;
    }

    private static IntermediateMaterial BuildMaterial(FbxObject obj, FbxDocument doc, IntermediateScene scene, FbxTextureMapper.TextureMapping textureMapping, ImportContext ctx)
    {
        var p = obj.Properties;
        var mat = new IntermediateMaterial
        {
            Name = string.IsNullOrEmpty(obj.Name) ? $"Material_{obj.Id}" : obj.Name,
        };

        if (p.TryGetVec3("DiffuseColor", out double dr, out double dg, out double db))
            mat.BaseColor = new Color((float)dr, (float)dg, (float)db, mat.BaseColor.A);

        if (p.TryGetDouble("Opacity", out double op))
        {
            mat.BaseColor = new Color(mat.BaseColor.R, mat.BaseColor.G, mat.BaseColor.B, (float)op);
            mat.AlphaMode = op < 0.999 ? MaterialAlphaMode.Blend : MaterialAlphaMode.Opaque;
        }

        if (p.TryGetVec3("Emissive", out double er, out double eg, out double eb))
            mat.EmissiveFactor = new Color((float)er, (float)eg, (float)eb, 1f);
        else if (p.TryGetVec3("EmissiveColor", out er, out eg, out eb))
            mat.EmissiveFactor = new Color((float)er, (float)eg, (float)eb, 1f);

        if (p.TryGetDouble("EmissiveFactor", out double ef))
            mat.EmissiveStrength = (float)ef;

        // Phong shininess -> roughness (Lengyel-style: roughness ~ sqrt(2 / (s + 2))).
        if (p.TryGetDouble("ShininessExponent", out double se) || p.TryGetDouble("Shininess", out se))
            mat.Roughness = MathF.Min(1f, MathF.Sqrt(2f / (float)(MathF.Max(2f, (float)se) + 2f)));

        if (p.TryGetDouble("ReflectionFactor", out double rf))
            mat.Metallic = (float)rf;

        // Texture slot wiring: walk every OP connection from a Texture to this Material and
        // route by FBX property name. The recognized set is a union of classic FBX, Maya base
        // shader, Maya Stingray, and 3ds Max PBR conventions.
        if (doc.ConnectionsByDestination.TryGetValue(obj.Id, out var conns))
        {
            foreach (var c in conns)
            {
                if (c.Type != "OP") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var srcObj)) continue;
                if (srcObj.ObjectType != "Texture") continue;
                if (!textureMapping.TextureIndex.TryGetValue(srcObj.Id, out int texIdx)) continue;

                // Carry the texture's UV transform onto the slot: every material slot using a
                // given Texture inherits that Texture's UV transform.
                var iTex = scene.Textures[texIdx];
                var slot = new IntermediateTextureSlot
                {
                    TextureIndex = texIdx,
                    Offset = iTex.UVOffset,
                    Scale = iTex.UVScale,
                    Rotation = iTex.UVRotation,
                };
                AssignSlot(mat, c.Property, slot);
            }
        }
        _ = ctx;
        return mat;
    }

    private static void AssignSlot(IntermediateMaterial mat, string property, IntermediateTextureSlot slot)
    {
        switch (property)
        {
            // ------ Base color / diffuse ------
            case "DiffuseColor":
            case "Maya|baseColor":
            case "Maya|DiffuseTexture":
            case "Maya|TEX_color_map":
            case "3dsMax|Parameters|base_color_map":
            case "3dsMax|main|base_color_map":
                mat.BaseColorTexture ??= slot;
                return;

            // ------ Normal / bump ------
            case "NormalMap":
            case "Bump":
            case "Maya|normalCamera":
            case "Maya|NormalTexture":
            case "Maya|TEX_normal_map":
            case "3dsMax|Parameters|bump_map":
            case "3dsMax|main|norm_map":
                mat.NormalTexture ??= slot;
                return;

            // ------ Metallic / roughness (packed in our model) ------
            case "Maya|metalness":
            case "Maya|TEX_metallic_map":
            case "Maya|TEX_roughness_map":
            case "Maya|diffuseRoughness":
            case "Maya|specularRoughness":
            case "3dsMax|Parameters|metalness_map":
            case "3dsMax|Parameters|roughness_map":
            case "3dsMax|main|metalness_map":
            case "3dsMax|main|roughness_map":
            case "3dsMax|main|glossiness_map":
            case "ReflectionFactor":
            case "ShininessExponent":
                mat.MetallicRoughnessTexture ??= slot;
                return;

            // ------ Specular / reflection - feed the same packed slot if it's still free ------
            case "SpecularColor":
            case "SpecularFactor":
            case "ReflectionColor":
            case "Maya|specularColor":
            case "Maya|SpecularTexture":
            case "Maya|ReflectionMapTexture":
            case "3dsMax|main|specular_map":
                mat.MetallicRoughnessTexture ??= slot;
                return;

            // ------ Emissive ------
            case "EmissiveColor":
            case "EmissiveFactor":
            case "Maya|emissionColor":
            case "Maya|TEX_emissive_map":
            case "3dsMax|Parameters|emission_map":
            case "3dsMax|main|emit_color_map":
                mat.EmissiveTexture ??= slot;
                return;

            // ------ Occlusion ------
            case "AmbientColor":
            case "AmbientOcclusion":
            case "Maya|TEX_ao_map":
            case "3dsMax|main|ao_map":
                mat.OcclusionTexture ??= slot;
                return;

            // ------ Opacity - fold into base color slot when there's nothing else ------
            case "TransparentColor":
            case "TransparencyFactor":
            case "Maya|FalloffTexture":
            case "3dsMax|main|opacity_map":
                mat.BaseColorTexture ??= slot;
                return;
        }
    }
}
