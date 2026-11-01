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
    /// Lights, particle light modules and scene environments saved before light was physical, which carry no units
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
