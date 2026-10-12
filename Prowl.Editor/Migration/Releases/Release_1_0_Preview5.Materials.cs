// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0_Preview5
{
    private static MigrationStep[] GraphiteMaterialSteps =>
    [
        new("Standard and Unlit shader families to Graphite Lit and Unlit", ctx => ctx.RewriteEchoAssets(ConvertLegacyMaterials)),
    ];

    private enum LegacySurface : long { Opaque, Cutout, Transparent }

    private enum LegacyCull : long { Back = 0, Off = 2 }

    private static readonly Dictionary<string, (string Shader, LegacySurface Surface, LegacyCull Cull, bool Anisotropic)> s_legacyShaders = BuildLegacyShaders();

    private static Dictionary<string, (string, LegacySurface, LegacyCull, bool)> BuildLegacyShaders()
    {
        Dictionary<string, (string, LegacySurface, LegacyCull, bool)> map = new(StringComparer.OrdinalIgnoreCase);
        string lit = BuiltInAssets.DeterministicGuid("$Default:Shader/Lit").ToString();
        string unlit = BuiltInAssets.DeterministicGuid("$Default:Shader/Unlit").ToString();
        foreach (string family in new[] { "Standard", "StandardAnisotropic", "Unlit" })
            foreach ((string mode, LegacySurface surface) in new[] { ("", LegacySurface.Opaque), ("Cutout", LegacySurface.Cutout), ("Transparent", LegacySurface.Transparent) })
                foreach ((string sides, LegacyCull cull) in new[] { ("", LegacyCull.Back), ("DoubleSided", LegacyCull.Off) })
                {
                    string name = family + mode + sides;
                    map[BuiltInAssets.DeterministicGuid($"$Default:Shader/{name}").ToString()] = (family == "Unlit" ? unlit : lit, surface, cull, family == "StandardAnisotropic");
                }
        return map;
    }

    // White, gray, flat normal, the old surface (0, 1, 0) and black emission stand ins, which a slot now treats as empty
    private static readonly HashSet<string> s_placeholderTextures = new(
        new[] { "White", "Gray18", "Normal", "Surface", "Emission" }.Select(n => BuiltInAssets.DeterministicGuid($"$Default:Texture/{n}").ToString()),
        StringComparer.OrdinalIgnoreCase);

    internal static EchoObject? ConvertLegacyMaterials(EchoObject echo)
    {
        bool changed = false;
        ConvertLegacyMaterials(echo, ref changed);
        return changed ? echo : null;
    }

    private static void ConvertLegacyMaterials(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                ConvertLegacyMaterials(item, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (tag.TryGet("_shader", out EchoObject? shader) && shader!.TagType == EchoType.Compound
            && shader.TryGet("$asset", out EchoObject? guid) && guid!.TagType == EchoType.String
            && s_legacyShaders.TryGetValue(guid.StringValue, out var legacy))
        {
            ConvertMaterial(tag, shader, legacy);
            changed = true;
        }

        foreach (string child in tag.GetNames().ToList())
            ConvertLegacyMaterials(tag[child], ref changed);
    }

    private static void ConvertMaterial(EchoObject material, EchoObject shader, (string Shader, LegacySurface Surface, LegacyCull Cull, bool Anisotropic) legacy)
    {
        shader["$asset"] = new EchoObject(legacy.Shader);

        if (!material.Contains("_surface"))
        {
            EchoObject surface = EchoObject.NewCompound();
            surface.Add("Type", new EchoObject((long)legacy.Surface));
            surface.Add("Blend", new EchoObject(0L));
            surface.Add("Cull", new EchoObject((long)legacy.Cull));
            material["_surface"] = surface;
        }

        if (!material.TryGet("_properties", out EchoObject? properties) || properties!.TagType != EchoType.Compound) return;

        // Old materials list no overrides and count every stored value as set, which removing values would change
        List<EchoObject> overrides = OverrideList(material);
        if (overrides.Count == 0)
            foreach (string group in properties.GetNames())
                if (properties[group].TagType == EchoType.Compound)
                    foreach (string name in properties[group].GetNames())
                        if (!name.StartsWith('$')) overrides.Add(new EchoObject(name));

        bool RealTexture(string slot) => properties.TryGet("_textures", out EchoObject? textures) && textures!.TryGet(slot, out EchoObject? texture)
            && texture!.TryGet("$asset", out EchoObject? id) && !s_placeholderTextures.Contains(id!.StringValue);
        bool hadSurfaceMap = RealTexture("_SurfaceTex");
        bool hadEmissionMap = RealTexture("_EmissionTex");

        if (properties.TryGet("_textures", out EchoObject? slots) && slots!.TagType == EchoType.Compound)
        {
            foreach (string slot in slots.GetNames().ToList())
                if (slots[slot].TryGet("$asset", out EchoObject? id) && s_placeholderTextures.Contains(id!.StringValue))
                    Forget(properties, overrides, slot);
        }

        if (legacy.Shader != BuiltInAssets.DeterministicGuid("$Default:Shader/Lit").ToString()) return;

        if (!hadSurfaceMap) Forget(properties, overrides, "_Metallic");
        if (hadEmissionMap)
        {
            Group(properties, "_ints")["_Emission"] = new EchoObject(1);
            overrides.Add(new EchoObject("_Emission"));
        }
        if (!legacy.Anisotropic) Forget(properties, overrides, "_Anisotropy");
        else if (!Group(properties, "_floats").Contains("_Anisotropy"))
        {
            Group(properties, "_floats")["_Anisotropy"] = new EchoObject(0.5f);
            overrides.Add(new EchoObject("_Anisotropy"));
        }
    }

    private static EchoObject Group(EchoObject properties, string group)
    {
        if (!properties.TryGet(group, out EchoObject? values) || values!.TagType != EchoType.Compound)
        {
            values = EchoObject.NewCompound();
            properties[group] = values;
        }
        return values;
    }

    // A HashSet saves as a list, or as {"$id", "$values"} when referenced
    private static List<EchoObject> OverrideList(EchoObject material)
    {
        if (material.TryGet("_overrides", out EchoObject? overrides))
        {
            if (overrides!.TagType == EchoType.List) return overrides.List;
            if (overrides.TagType == EchoType.Compound && overrides.TryGet("$values", out EchoObject? values) && values!.TagType == EchoType.List) return values.List;
        }
        EchoObject list = EchoObject.NewList();
        material["_overrides"] = list;
        return list.List;
    }

    private static void Forget(EchoObject properties, List<EchoObject> overrides, string name)
    {
        foreach (string group in properties.GetNames())
            if (properties[group].TagType == EchoType.Compound && properties[group].Contains(name))
                properties[group].Remove(name);
        overrides.RemoveAll(o => o.TagType == EchoType.String && o.StringValue == name);
    }
}
