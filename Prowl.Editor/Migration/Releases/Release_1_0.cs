// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0 : MigrationRelease
{
    public override string Name => "1.0";

    public override MigrationStep[] Steps =>
    [
        new("Mesh, sprite and text renderer settings saved behind properties", ctx => RenameSettings(ctx, s_rendererRenames)),
        new("Light, fog light, decal and reflection probe settings saved behind properties", ctx => RenameSettings(ctx, s_lightRenames)),
        new("Texture import types: masks become linear Default textures, sprites need the Sprite type, sizes kept", ctx => ctx.RewriteImporterSettings("TextureImporter", TextureImportTypes)),
        new("Camera tonemapping moved into the camera's base profile as a Tonemapping volume component", CameraTonemappingToProfile),
        new("Materials of the standard shaders from before Graphite moved onto Lit and Unlit", ctx => ctx.RewriteEcho(echo => ConvertMaterials(echo, AdoptLegacyShader))),
        new("Lit, Unlit, sky, grass and terrain materials moved onto Graphite's OpenPBR property names", ctx => ctx.RewriteEcho(echo => ConvertMaterials(echo, AdoptOpenPbr))),
        new("Light, particle light, ambient and fog intensities carried onto physical units", ctx => ctx.RewriteEchoAssets(PhysicalLightUnits)),
        new("Custom shaders using the old Lit property names are listed for hand edits", WarnOldShaderNames),
        new("Light spot cones saved as an angle and softness, ranges made automatic, colours kept off colour temperature", LightConesAndTemperature),
        new("Scenes baked before baking used physical light units are listed for a rebake", WarnUnscaledBakes),
        new("Lights split into a component for each kind: directional, point, spot, rect, disk and tube", SplitLights),
    ];

    private const string CameraType = "Prowl.Runtime.Camera";
    private const string VolumeProfileType = "Prowl.Runtime.Rendering.VolumeProfile";
    private const string TonemappingType = "Prowl.Runtime.Rendering.Tonemapping";

    // TonemappingMode.AgXPunchy, the camera's old default. The volume enum puts None first, one before the camera's values.
    private const long AgXPunchy = 3;

    /// <summary>
    /// An HDR camera's Tonemapping mode becomes a Tonemapping component in its base profile: added to an inline profile,
    /// to a profile asset it references, or in a new inline profile. A profile that has one already keeps it. Prefab
    /// overrides of the old field are dropped, as a profile component has no override path.
    /// </summary>
    internal static void CameraTonemappingToProfile(MigrationContext ctx)
    {
        Dictionary<Guid, long> sharedProfiles = [];
        HashSet<string> cameras = new(StringComparer.OrdinalIgnoreCase);
        ctx.RewriteEchoAssets(echo => MoveCameraTonemapping(echo, sharedProfiles, cameras));
        ctx.RewriteEchoAssets((guid, echo) =>
            sharedProfiles.TryGetValue(guid, out long mode) && TypeName(echo) == VolumeProfileType && AddTonemapping(echo, mode) ? echo : null);
        ctx.RewriteEchoAssets(echo => DropTonemappingOverrides(echo, cameras));
    }

    private static EchoObject? MoveCameraTonemapping(EchoObject echo, Dictionary<Guid, long> sharedProfiles, HashSet<string> cameras)
    {
        bool changed = false;
        MoveCameraTonemapping(echo, sharedProfiles, cameras, ref changed);
        return changed ? echo : null;
    }

    private static void MoveCameraTonemapping(EchoObject tag, Dictionary<Guid, long> sharedProfiles, HashSet<string> cameras, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                MoveCameraTonemapping(item, sharedProfiles, cameras, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (TypeName(tag) == CameraType)
        {
            foreach (string id in new[] { "_identifier", "_prefabTemplateIdentity" })
            {
                if (tag.TryGet(id, out EchoObject? guid) && guid!.TagType == EchoType.String)
                    cameras.Add(guid.StringValue);
            }

            long mode = AgXPunchy;
            if (tag.TryGet("Tonemapping", out EchoObject? old))
            {
                if (old!.TagType is not (EchoType.String or EchoType.Null) && old.LongValue is >= 0 and <= 5) mode = old.LongValue + 1;
                tag.Remove("Tonemapping");
                changed = true;
            }

            bool hdr = !tag.TryGet("HDR", out EchoObject? hdrTag) || hdrTag!.TagType != EchoType.Bool || hdrTag.BoolValue;
            if (hdr) changed |= EnsureTonemapping(tag, mode, sharedProfiles);
        }

        foreach (string child in tag.GetNames().ToList())
            MoveCameraTonemapping(tag[child], sharedProfiles, cameras, ref changed);
    }

    private static bool EnsureTonemapping(EchoObject camera, long mode, Dictionary<Guid, long> sharedProfiles)
    {
        if (!camera.TryGet("Profile", out EchoObject? profile) || profile!.TagType != EchoType.Compound)
        {
            var created = EchoObject.NewCompound();
            created["Name"] = new EchoObject("Camera Profile");
            AddTonemapping(created, mode);
            camera["Profile"] = created;
            return true;
        }

        if (profile.TryGet("$asset", out EchoObject? asset))
        {
            if (asset!.TagType == EchoType.String && Guid.TryParse(asset.StringValue, out Guid guid)) sharedProfiles.TryAdd(guid, mode);
            return false;
        }
        return !profile.TryGet("$ref", out _) && AddTonemapping(profile, mode);
    }

    private static bool AddTonemapping(EchoObject profile, long mode)
    {
        if (!profile.TryGet("Components", out EchoObject? components) || components!.TagType is not (EchoType.Compound or EchoType.List))
        {
            components = EchoObject.NewCompound();
            components["$values"] = EchoObject.NewList();
            profile["Components"] = components;
        }

        EchoObject list = components.TagType == EchoType.List ? components : components.TryGet("$values", out EchoObject? values) ? values! : components["$values"] = EchoObject.NewList();
        if (list.List.Any(c => c.TagType == EchoType.Compound && TypeName(c) == TonemappingType)) return false;

        var parameter = EchoObject.NewCompound();
        parameter["Value"] = new EchoObject(mode);
        parameter["Override"] = new EchoObject(true);
        var tonemapping = EchoObject.NewCompound();
        tonemapping["$type"] = new EchoObject(TonemappingType + ", Prowl.Runtime");
        tonemapping["Mode"] = parameter;
        tonemapping["Active"] = new EchoObject(true);
        list.ListAdd(tonemapping);
        return true;
    }

    // Prefab override lists hold compounds with a Path of {object}/{component}/{member}
    private static EchoObject? DropTonemappingOverrides(EchoObject echo, HashSet<string> cameras)
    {
        bool changed = false;
        DropTonemappingOverrides(echo, cameras, ref changed);
        return changed ? echo : null;
    }

    private static void DropTonemappingOverrides(EchoObject tag, HashSet<string> cameras, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            int removed = tag.List.RemoveAll(item => IsTonemappingOverride(item, cameras));
            changed |= removed > 0;
            foreach (EchoObject item in tag.List)
                DropTonemappingOverrides(item, cameras, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        foreach (string child in tag.GetNames().ToList())
            DropTonemappingOverrides(tag[child], cameras, ref changed);
    }

    private static bool IsTonemappingOverride(EchoObject item, HashSet<string> cameras)
    {
        if (item.TagType != EchoType.Compound || !item.TryGet("Path", out EchoObject? path) || path!.TagType != EchoType.String) return false;
        string[] parts = path.StringValue.Split('/', 3);
        return parts.Length == 3 && cameras.Contains(parts[1]) && (parts[2] == "Tonemapping" || parts[2].StartsWith("Tonemapping.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Mask textures (type 2) become Default with sRGB off, any texture slicing sprites becomes a Sprite (type 3), baked
    /// lightmap pages become Lightmaps (type 7), and an existing texture keeps its full size under the new 2048 default.
    /// </summary>
    internal static EchoObject? TextureImportTypes(string assetPath, EchoObject settings)
    {
        settings.TryGet("textureType", out EchoObject? type);
        settings.TryGet("sprite", out EchoObject? sprite);
        bool changed = false;

        if (type == null && IsLightmapPage(assetPath))
        {
            settings["textureType"] = new EchoObject(7);
            changed = true;
        }
        if (type != null && type.TagType != EchoType.String && type.IntValue == 2)
        {
            settings["textureType"] = new EchoObject(0);
            if (!settings.TryGet("srgb", out _)) settings["srgb"] = new EchoObject(false);
            changed = true;
        }

        bool slices = sprite?.TagType == EchoType.Compound && sprite.TryGet("Mode", out EchoObject? mode) && mode!.TagType != EchoType.String && mode.IntValue != 0;
        if (slices && (!settings.TryGet("textureType", out EchoObject? now) || now!.IntValue != 3))
        {
            settings["textureType"] = new EchoObject(3);
            changed = true;
        }

        if (!settings.TryGet("maxSize", out _))
        {
            settings["maxSize"] = new EchoObject(16384);
            changed = true;
        }

        return changed ? settings : null;
    }

    /// <summary>A page the lightmapper wrote, Lightmap-N.png inside a scene's _lightmaps folder.</summary>
    private static bool IsLightmapPage(string assetPath)
        => Path.GetFileName(Path.GetDirectoryName(assetPath) ?? "").EndsWith("_lightmaps", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(assetPath).StartsWith("Lightmap-", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string From, string To)[]> s_rendererRenames = new()
    {
        ["Prowl.Runtime.MeshRenderer"] = Camel("Mesh", "Materials", "ShadowCasting", "ReceiveShadows"),
        ["Prowl.Runtime.SpriteRenderer"] = Camel("Sprite", "Color", "FlipX", "FlipY", "Material", "SortingOrder", "ShadowCasting"),
        ["Prowl.Runtime.TextMeshComponent"] = [("ShadowCasting", "_shadowCasting")],
    };

    private static readonly Dictionary<string, (string From, string To)[]> s_lightRenames = new()
    {
        ["Prowl.Runtime.Light"] = Camel(
            "Type", "Color", "Intensity", "Range", "InnerAngle", "OuterAngle", "Width", "Height", "Length", "SourceRadius",
            "AngularRadius", "Cookie", "IesProfile", "LayerMask", "CastShadows", "ShadowStrength", "ShadowResolution",
            "ShadowFilter", "DepthBias", "NormalBias", "Cascades", "ShadowDistance", "CascadeBlend", "BakeMode"),
        ["Prowl.Runtime.FogLight"] = Camel("IntensityMultiplier", "CastFogShadows", "UseOverrideColor", "OverrideColor", "ScatteringBias"),
        ["Prowl.Runtime.Decal"] = Camel("Size", "Albedo", "Normal", "Mask", "Color", "NormalStrength", "AngleFade", "Priority", "LayerMask"),
        ["Prowl.Runtime.ReflectionProbe"] = Camel(
            "Mode", "RefreshMode", "TimeSlicing", "Importance", "Intensity", "BoxSize", "BoxOffset", "BlendDistance",
            "BoxProjection", "Resolution", "NearClip", "FarClip", "CullingMask"),
    };

    private static (string, string)[] Camel(params string[] names) => [.. names.Select(n => (n, char.ToLowerInvariant(n[0]) + n[1..]))];

    // Every component of the renamed types is found first, so prefab overrides on them in other files can be renamed too
    internal static void RenameSettings(MigrationContext ctx, IReadOnlyDictionary<string, (string From, string To)[]> renames)
    {
        Dictionary<string, string> components = new(StringComparer.OrdinalIgnoreCase);
        ctx.RewriteEchoAssets(echo => { CollectComponents(echo, renames, components); return null; });
        ctx.RewriteEchoAssets(echo => RenameSettings(echo, renames, components));
    }

    /// <summary>The type name before the assembly part of a $type, or null.</summary>
    private static string? TypeName(EchoObject tag)
    {
        if (!tag.TryGet("$type", out EchoObject? type) || type!.TagType != EchoType.String) return null;
        string value = type.StringValue;
        int comma = value.IndexOf(',');
        return (comma < 0 ? value : value[..comma]).Trim();
    }

    /// <summary>Notes the identifiers of every component of a renamed type, so prefab overrides on them can be found.</summary>
    internal static void CollectComponents(EchoObject tag, IReadOnlyDictionary<string, (string From, string To)[]> renames, Dictionary<string, string> components)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                CollectComponents(item, renames, components);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (TypeName(tag) is { } type && renames.ContainsKey(type))
        {
            foreach (string id in new[] { "_identifier", "_prefabTemplateIdentity" })
            {
                if (tag.TryGet(id, out EchoObject? guid) && guid!.TagType == EchoType.String && Guid.TryParse(guid.StringValue, out Guid parsed) && parsed != Guid.Empty)
                    components[guid.StringValue] = type;
            }
        }

        foreach (string child in tag.GetNames().ToList())
            CollectComponents(tag[child], renames, components);
    }

    /// <summary>Renames the saved settings of the renamed types, and prefab override paths naming them.</summary>
    internal static EchoObject? RenameSettings(EchoObject echo, IReadOnlyDictionary<string, (string From, string To)[]> renames, IReadOnlyDictionary<string, string> components)
    {
        bool changed = false;
        RenameSettings(echo, renames, components, ref changed);
        return changed ? echo : null;
    }

    private static void RenameSettings(EchoObject tag, IReadOnlyDictionary<string, (string From, string To)[]> renames, IReadOnlyDictionary<string, string> components, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                RenameSettings(item, renames, components, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (TypeName(tag) is { } type && renames.TryGetValue(type, out (string From, string To)[]? fields))
        {
            foreach ((string from, string to) in fields)
            {
                if (!tag.TryGet(from, out EchoObject? value)) continue;
                tag.Remove(from);
                if (!tag.Contains(to)) tag[to] = value!;
                changed = true;
            }
        }

        if (tag.TryGet("Path", out EchoObject? path) && path!.TagType == EchoType.String && RenamedPath(path.StringValue, renames, components) is { } renamed)
        {
            tag["Path"] = new EchoObject(renamed);
            changed = true;
        }

        foreach (string child in tag.GetNames().ToList())
            RenameSettings(tag[child], renames, components, ref changed);
    }

    // An override path is {object}/{component}/{member.path}, its first member renamed when its component is of a renamed type
    private static string? RenamedPath(string path, IReadOnlyDictionary<string, (string From, string To)[]> renames, IReadOnlyDictionary<string, string> components)
    {
        string[] parts = path.Split('/', 3);
        if (parts.Length != 3 || !components.TryGetValue(parts[1], out string? type)) return null;

        int dot = parts[2].IndexOf('.');
        string field = dot < 0 ? parts[2] : parts[2][..dot];
        string rest = dot < 0 ? "" : parts[2][dot..];
        foreach ((string from, string to) in renames[type])
        {
            if (field == from) return $"{parts[0]}/{parts[1]}/{to}{rest}";
        }
        return null;
    }

    private const string LightType = "Prowl.Runtime.Light";
    private const string ParticleSystemType = "Prowl.Runtime.ParticleSystem.ParticleSystemComponent";
    private const string SceneType = "Prowl.Runtime.Resources.Scene";

    // LegacyLightUnits: old emission, ambient and fog brightness to nits, and old light intensities to lux, candela or nits
    private const float LegacyLuminance = 1.2f * 16384f;
    private const float LegacyLight = 8f * LegacyLuminance;

    // AmbientMode.Sky
    private const long AmbientSky = 2;

    /// <summary>
    /// Lights, particle light modules and scene environments and bakes saved before light was physical, which carry no units
    /// marker, have their intensities scaled and get the marker, so the step can run again. Prefab overrides of a
    /// light's intensity are scaled when the instance's light in the same file was old, and a particle system's light
    /// module override converts by its own marker.
    /// </summary>
    internal static EchoObject? PhysicalLightUnits(EchoObject echo)
    {
        bool changed = false;
        HashSet<string> oldLights = [], systems = [];
        ForEachCompound(echo, tag =>
        {
            switch (TypeName(tag))
            {
                case LightType when IsOldUnits(tag, "units"):
                    Scale(tag, "intensity", LegacyLight);
                    tag["units"] = new EchoObject(1);
                    AddIdentities(tag, oldLights);
                    changed = true;
                    break;
                case ParticleSystemType:
                    AddIdentities(tag, systems);
                    if (tag.TryGet("Light", out EchoObject? module)) changed |= LightModuleUnits(module!);
                    break;
                case SceneType when IsOldUnits(tag, "_lightUnits"):
                    EnvironmentUnits(tag);
                    BakeUnits(tag);
                    tag["_lightUnits"] = new EchoObject(1);
                    changed = true;
                    break;
            }
        });

        ForEachCompound(echo, tag =>
        {
            if (!tag.TryGet("Path", out EchoObject? path) || path!.TagType != EchoType.String || !tag.TryGet("Value", out EchoObject? value)) return;
            string[] parts = path.StringValue.Split('/', 3);
            if (parts.Length != 3) return;
            if (parts[2] == "intensity" && oldLights.Contains(parts[1]) && Scale(tag, "Value", LegacyLight)) changed = true;
            else if (parts[2] == "Light" && systems.Contains(parts[1])) changed |= LightModuleUnits(value!);
        });
        return changed ? echo : null;
    }

    private static bool LightModuleUnits(EchoObject module)
    {
        if (module.TagType != EchoType.Compound || !IsOldUnits(module, "units")) return false;
        if (module.TryGet("Intensity", out EchoObject? intensity) && intensity!.TagType == EchoType.Compound)
        {
            foreach (string field in new[] { "ConstantValue", "MinValue", "MaxValue", "CurveMultiplier" })
                Scale(intensity, field, LegacyLight);
        }
        module["units"] = new EchoObject(1);
        return true;
    }

    // Uniform and hemisphere ambient scaled onto nits, sky ambient follows the sky, fog set to the luminance of its old look
    private static void EnvironmentUnits(EchoObject scene)
    {
        EchoObject environment = Child(scene, "Environment");
        if (environment.TryGet("Ambient", out EchoObject? ambient) && ambient!.TagType == EchoType.Compound
            && ambient.TryGet("Mode", out EchoObject? mode) && IsNumber(mode!) && mode.LongValue != AmbientSky)
            Scale(ambient, "Intensity", LegacyLuminance);
        Child(environment, "Fog")["Intensity"] = new EchoObject(LegacyLuminance);
    }

    // A bake was drawn at an exposure of 1 too, so its lightmaps and probes are scaled onto nits the same way
    private static void BakeUnits(EchoObject scene)
    {
        if (!scene.TryGet("BakedLighting", out EchoObject? baked) || baked!.TagType != EchoType.Compound) return;
        baked["LightmapScale"] = new EchoObject((Number(baked, "LightmapScale") ?? 1f) * LegacyLuminance);
        if (baked.TryGet("ProbeSH", out EchoObject? probes)) ScaleNumbers(probes!, LegacyLuminance);
    }

    private static void ScaleNumbers(EchoObject tag, float factor)
    {
        if (tag.TagType == EchoType.List)
        {
            for (int i = 0; i < tag.List.Count; i++)
            {
                if (IsNumber(tag.List[i])) tag.List[i] = new EchoObject(tag.List[i].FloatValue * factor);
                else ScaleNumbers(tag.List[i], factor);
            }
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        foreach (string name in tag.GetNames().Where(n => !n.StartsWith('$')).ToList())
        {
            if (!Scale(tag, name, factor)) ScaleNumbers(tag[name], factor);
        }
    }

    /// <summary>Logs a warning for each scene holding a bake made while baking was not yet physical. Changes no files.</summary>
    internal static void WarnUnscaledBakes(MigrationContext ctx)
    {
        foreach (string path in ctx.AssetFiles(".scene"))
        {
            bool unscaled = false;
            ForEachCompound(EchoObject.ReadFromString(File.ReadAllText(path)), tag => unscaled |= TypeName(tag) == SceneType && NeedsRebake(tag));
            if (unscaled)
                Runtime.Debug.LogWarning($"{Path.GetRelativePath(ctx.Project.AssetsPath, path)} has baked lighting from before baking used physical light units, rebake it.");
        }
    }

    /// <summary>A scene with lightmaps or probes but no lightmap scale, which every physical bake and converted old bake saves.</summary>
    internal static bool NeedsRebake(EchoObject scene)
        => scene.TryGet("BakedLighting", out EchoObject? baked) && baked!.TagType == EchoType.Compound && !baked.Contains("LightmapScale")
            && (HasItems(baked, "Lightmaps") || HasItems(baked, "ProbeSH"));

    private static bool HasItems(EchoObject parent, string name)
    {
        if (!parent.TryGet(name, out EchoObject? items)) return false;
        if (items!.TagType == EchoType.Compound && (items.TryGet("$values", out EchoObject? values) || items.TryGet("array", out values))) items = values!;
        return items.TagType == EchoType.List && items.List.Count > 0;
    }

    private static EchoObject Child(EchoObject parent, string name)
    {
        if (parent.TryGet(name, out EchoObject? child) && child!.TagType == EchoType.Compound) return child;
        return parent[name] = EchoObject.NewCompound();
    }

    // A units marker of 1 says the values are physical already, anything else that they are from before
    private static bool IsOldUnits(EchoObject tag, string marker)
        => !tag.TryGet(marker, out EchoObject? units) || !IsNumber(units!) || units.LongValue == 0;

    private static bool IsNumber(EchoObject tag) => tag.TagType is >= EchoType.Byte and <= EchoType.Decimal;

    private static bool Scale(EchoObject tag, string name, float factor)
    {
        if (!tag.TryGet(name, out EchoObject? value) || !IsNumber(value!)) return false;
        tag[name] = new EchoObject(value.FloatValue * factor);
        return true;
    }

    private static void AddIdentities(EchoObject component, HashSet<string> ids)
    {
        foreach (string id in new[] { "_identifier", "_prefabTemplateIdentity" })
        {
            if (component.TryGet(id, out EchoObject? guid) && guid!.TagType == EchoType.String)
                ids.Add(guid.StringValue);
        }
    }

    /// <summary>Visits every compound in a tree, each before its children.</summary>
    private static void ForEachCompound(EchoObject tag, Action<EchoObject> visit)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List.ToList())
                ForEachCompound(item, visit);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        visit(tag);
        foreach (string child in tag.GetNames().ToList())
            ForEachCompound(tag[child], visit);
    }

    // The inner and outer half angles a light had before they were saved
    private const float OldInnerAngle = 30f, OldOuterAngle = 45f;

    /// <summary>
    /// Lights saved before colour temperature, which carry no useTemperature, have their inner and outer half angles
    /// turned into a whole spotAngle and a spotSoftness, their range dropped so it is worked out from the intensity, and
    /// useTemperature written off so their colour stays as it was. Prefab overrides of the old angles become overrides of
    /// spotAngle and spotSoftness, worked out against the angles of the light they override. Range overrides are kept,
    /// unused while the instance leaves overrideRange off.
    /// </summary>
    internal static void LightConesAndTemperature(MigrationContext ctx)
    {
        Dictionary<string, (float Inner, float Outer)> cones = [];
        ctx.RewriteEchoAssets(echo => { ForEachCompound(echo, tag => CollectCone(tag, cones)); return null; });
        ctx.RewriteEchoAssets(echo => ConvertLightCones(echo, cones));
    }

    // Every light's half angles by its identities, its own identifier winning over the prefab source it came from
    private static void CollectCone(EchoObject tag, Dictionary<string, (float Inner, float Outer)> cones)
    {
        if (TypeName(tag) != LightType) return;
        (float Inner, float Outer) cone = OldCone(tag);
        if (tag.TryGet("_identifier", out EchoObject? id) && id!.TagType == EchoType.String) cones[id.StringValue] = cone;
        if (tag.TryGet("_prefabTemplateIdentity", out EchoObject? source) && source!.TagType == EchoType.String) cones.TryAdd(source.StringValue, cone);
    }

    // The half angles of a light in either format, the old defaults filling what is missing
    private static (float Inner, float Outer) OldCone(EchoObject light)
    {
        if (!light.Contains("innerAngle") && !light.Contains("outerAngle") && Number(light, "spotAngle") is float angle)
        {
            float half = angle * 0.5f;
            return (half * (1f - Math.Clamp(Number(light, "spotSoftness") ?? 0f, 0f, 1f)), half);
        }
        return (Number(light, "innerAngle") ?? OldInnerAngle, Number(light, "outerAngle") ?? OldOuterAngle);
    }

    private static float? Number(EchoObject tag, string name)
        => tag.TryGet(name, out EchoObject? value) && IsNumber(value!) ? value.FloatValue : null;

    private static float Softness(float inner, float outer) => outer > 0f ? Math.Clamp(1f - inner / outer, 0f, 1f) : 0f;

    private static EchoObject? ConvertLightCones(EchoObject echo, Dictionary<string, (float Inner, float Outer)> cones)
    {
        bool changed = false;
        ForEachCompound(echo, tag =>
        {
            if (TypeName(tag) != LightType || tag.Contains("useTemperature")) return;
            if (tag.Contains("innerAngle") || tag.Contains("outerAngle"))
            {
                (float inner, float outer) = OldCone(tag);
                tag.Remove("innerAngle");
                tag.Remove("outerAngle");
                tag["spotAngle"] = new EchoObject(2f * outer);
                tag["spotSoftness"] = new EchoObject(Softness(inner, outer));
            }
            tag.Remove("range");
            tag["useTemperature"] = new EchoObject(false);
            changed = true;
        });
        ConvertConeOverrides(echo, cones, ref changed);
        return changed ? echo : null;
    }

    // Prefab override lists hold compounds with a Path of {object}/{component}/{member}
    private static void ConvertConeOverrides(EchoObject tag, Dictionary<string, (float Inner, float Outer)> cones, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            changed |= ConvertConeOverrideList(tag, cones);
            foreach (EchoObject item in tag.List.ToList())
                ConvertConeOverrides(item, cones, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        foreach (string child in tag.GetNames().ToList())
            ConvertConeOverrides(tag[child], cones, ref changed);
    }

    private static bool ConvertConeOverrideList(EchoObject list, Dictionary<string, (float Inner, float Outer)> cones)
    {
        Dictionary<string, (EchoObject? Inner, EchoObject? Outer)> instances = [];
        foreach (EchoObject item in list.List)
        {
            if (item.TagType != EchoType.Compound || !item.TryGet("Path", out EchoObject? path) || path!.TagType != EchoType.String) continue;
            string[] parts = path.StringValue.Split('/', 3);
            if (parts.Length != 3 || !cones.ContainsKey(parts[1]) || parts[2] is not ("innerAngle" or "outerAngle")) continue;

            string prefix = $"{parts[0]}/{parts[1]}";
            instances.TryGetValue(prefix, out (EchoObject? Inner, EchoObject? Outer) found);
            instances[prefix] = parts[2] == "innerAngle" ? (item, found.Outer) : (found.Inner, item);
        }

        foreach ((string prefix, (EchoObject? innerItem, EchoObject? outerItem)) in instances)
        {
            (float inner, float outer) = cones[prefix[(prefix.IndexOf('/') + 1)..]];
            if (innerItem != null && innerItem.TryGet("Value", out EchoObject? innerValue) && IsNumber(innerValue!)) inner = innerValue.FloatValue;
            if (outerItem != null && outerItem.TryGet("Value", out EchoObject? outerValue) && IsNumber(outerValue!)) outer = outerValue.FloatValue;

            EchoObject softnessItem = innerItem ?? outerItem!.Clone();
            softnessItem["Path"] = new EchoObject($"{prefix}/spotSoftness");
            softnessItem["Value"] = new EchoObject(Softness(inner, outer));
            if (outerItem == null) continue;

            outerItem["Path"] = new EchoObject($"{prefix}/spotAngle");
            outerItem["Value"] = new EchoObject(2f * outer);
            if (innerItem == null) list.ListInsert(list.List.IndexOf(outerItem) + 1, softnessItem);
        }
        return instances.Count > 0;
    }

    // The old Light's type field: Directional, Point, Spot, Rect, Disk, Tube
    private static readonly string[] s_lightKinds = ["Directional", "Point", "Spot", "Rect", "Disk", "Tube"];
    private const long OldDefaultKind = 1;

    // The old Light's defaults that the new components start differently, so a value left unsaved keeps its old meaning
    private const float OldKelvin = 2700f, OldIntensity = 800f / (4f * MathF.PI);

    // What each kind keeps of the old fields, as {old name, new name, old default}
    private static readonly Dictionary<string, (string Old, string New, float? Default)[]> s_lightKindFields = new()
    {
        ["Directional"] = [("angularRadius", "angularRadius", null), ("cascadeBlend", "cascadeBlend", null)],
        ["Point"] = [("overrideRange", "overrideRange", null), ("range", "range", null), ("sourceRadius", "sourceRadius", 0f), ("cookie", "cookie", null), ("iesProfile", "iesProfile", null)],
        ["Spot"] = [("overrideRange", "overrideRange", null), ("range", "range", null), ("sourceRadius", "sourceRadius", 0f), ("cookie", "cookie", null), ("iesProfile", "iesProfile", null),
            ("spotAngle", "spotAngle", 90f), ("spotSoftness", "spotSoftness", 1f / 3f)],
        ["Rect"] = [("overrideRange", "overrideRange", null), ("range", "range", null), ("width", "width", 1f), ("height", "height", 1f)],
        ["Disk"] = [("overrideRange", "overrideRange", null), ("range", "range", null), ("width", "diameter", 1f)],
        ["Tube"] = [("overrideRange", "overrideRange", null), ("range", "range", null), ("length", "length", 1f), ("sourceRadius", "radius", 0f)],
    };

    // Fields of the old Light that only some kinds have, dropped from the kinds that do not
    private static readonly string[] s_kindOnlyFields =
        ["type", "units", "overrideRange", "range", "spotAngle", "spotSoftness", "width", "height", "length", "sourceRadius", "angularRadius", "cookie", "iesProfile", "cascadeBlend"];

    /// <summary>
    /// Each Light becomes the component of its type, DirectionalLight to TubeLight, keeping the fields that kind has under
    /// its names and writing the old defaults the new component starts differently. Prefab overrides of a light follow the
    /// same renames, overrides of fields its kind lacks are dropped, and an override of the type is dropped with a warning.
    /// </summary>
    internal static void SplitLights(MigrationContext ctx)
    {
        Dictionary<string, string> kinds = [];
        ctx.RewriteEchoAssets(echo => { ForEachCompound(echo, tag => CollectLightKind(tag, kinds)); return null; });
        ctx.RewriteEchoAssets(echo => SplitLights(echo, kinds));
    }

    private static void CollectLightKind(EchoObject tag, Dictionary<string, string> kinds)
    {
        if (TypeName(tag) != LightType) return;
        string kind = LightKind(tag);
        if (tag.TryGet("_identifier", out EchoObject? id) && id!.TagType == EchoType.String) kinds[id.StringValue] = kind;
        if (tag.TryGet("_prefabTemplateIdentity", out EchoObject? source) && source!.TagType == EchoType.String) kinds.TryAdd(source.StringValue, kind);
    }

    private static string LightKind(EchoObject light)
    {
        long kind = light.TryGet("type", out EchoObject? type) && IsNumber(type!) ? type.LongValue : OldDefaultKind;
        return s_lightKinds[kind is >= 0 and < 6 ? kind : OldDefaultKind];
    }

    internal static EchoObject? SplitLights(EchoObject echo, IReadOnlyDictionary<string, string> kinds)
    {
        bool changed = false;
        ForEachCompound(echo, tag =>
        {
            if (TypeName(tag) != LightType) return;
            string kind = LightKind(tag);
            (string Old, string New, float? Default)[] fields = s_lightKindFields[kind];
            Dictionary<string, EchoObject> kept = [];
            foreach ((string old, string name, float? fallback) in fields)
            {
                if (tag.TryGet(old, out EchoObject? value)) kept[name] = value!;
                else if (fallback is float f) kept[name] = new EchoObject(f);
            }
            foreach (string field in s_kindOnlyFields) tag.Remove(field);
            foreach ((string name, EchoObject value) in kept) tag[name] = value;
            if (!tag.Contains("temperature")) tag["temperature"] = new EchoObject(OldKelvin);
            if (!tag.Contains("intensity")) tag["intensity"] = new EchoObject(OldIntensity);
            tag["$type"] = new EchoObject($"Prowl.Runtime.{kind}Light, Prowl.Runtime");
            changed = true;
        });
        SplitLightOverrides(echo, kinds, ref changed);
        return changed ? echo : null;
    }

    // Prefab override lists hold compounds with a Path of {object}/{component}/{member.path}
    private static void SplitLightOverrides(EchoObject tag, IReadOnlyDictionary<string, string> kinds, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            for (int i = tag.List.Count - 1; i >= 0; i--)
            {
                EchoObject item = tag.List[i];
                if (item.TagType != EchoType.Compound || !item.TryGet("Path", out EchoObject? path) || path!.TagType != EchoType.String) continue;
                string[] parts = path.StringValue.Split('/', 3);
                if (parts.Length != 3 || !kinds.TryGetValue(parts[1], out string? kind)) continue;

                int dot = parts[2].IndexOf('.');
                string field = dot < 0 ? parts[2] : parts[2][..dot];
                if (!s_kindOnlyFields.Contains(field)) continue;

                string? renamed = s_lightKindFields[kind].FirstOrDefault(f => f.Old == field).New;
                if (renamed == field) continue;
                changed = true;
                if (renamed != null)
                {
                    item["Path"] = new EchoObject($"{parts[0]}/{parts[1]}/{renamed}{(dot < 0 ? "" : parts[2][dot..])}");
                    continue;
                }
                if (field == "type" && item.TryGet("Value", out EchoObject? value) && IsNumber(value!) && s_lightKinds.ElementAtOrDefault((int)value.LongValue) != kind)
                    Runtime.Debug.LogWarning($"A prefab instance changed light {parts[1]} from {kind} to {s_lightKinds.ElementAtOrDefault((int)value.LongValue)}. Lights are separate components now, so it stays {kind}: replace it by hand.");
                tag.List.RemoveAt(i);
            }
            foreach (EchoObject item in tag.List.ToList())
                SplitLightOverrides(item, kinds, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        foreach (string child in tag.GetNames().ToList())
            SplitLightOverrides(tag[child], kinds, ref changed);
    }

    private static readonly (string Old, string New)[] s_oldShaderNames =
    [
        ("_MainColor", "base_color"), ("_MainTex", "base_color_map"), ("_AlphaCutoff", "alpha_cutoff"),
        ("_Metallic", "base_metalness"), ("_Roughness", "specular_roughness"),
        ("_SurfaceTex", "specular_roughness_map from G and base_metalness_map from B"), ("_NormalTex", "geometry_normal_map"),
        ("_NormalScale", "normal_scale"), ("_OcclusionTex", "ambient_occlusion_map"), ("_ParallaxMap", "geometry_height_map from G"),
        ("_Parallax", "height_scale"), ("_Emission", "emission_luminance"), ("_EmissiveColor", "emission_color"),
        ("_EmissionIntensity", "emission_luminance"), ("_EmissionTex", "emission_color_map"), ("_Exposure", "intensity on the skies"),
    ];

    /// <summary>Logs a warning for each .shader and .glsl asset using the old Lit include or property names. Changes no files.</summary>
    internal static void WarnOldShaderNames(MigrationContext ctx)
    {
        foreach (string path in ctx.AssetFiles(".shader", ".glsl"))
        {
            List<string> found = OldShaderNames(File.ReadAllText(path));
            if (found.Count > 0)
                Runtime.Debug.LogWarning($"{Path.GetRelativePath(ctx.Project.AssetsPath, path)} may read Lit's properties by their names from before OpenPBR, change by hand where it does: {string.Join(", ", found)}");
        }
    }

    internal static List<string> OldShaderNames(string source)
    {
        List<string> found = [];
        if (source.Contains("Prowl/Lit.glsl", StringComparison.Ordinal)) found.Add("the Prowl/Lit.glsl include");
        foreach ((string old, string now) in s_oldShaderNames)
        {
            if (Regex.IsMatch(source, $@"(?<![A-Za-z0-9_]){old}(?:_ST|UV)?(?![A-Za-z0-9_])"))
                found.Add($"{old} is now {now}");
        }
        return found;
    }
}
