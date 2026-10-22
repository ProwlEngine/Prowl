// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed class Release_1_0 : MigrationRelease
{
    public override string Name => "1.0";

    public override MigrationStep[] Steps =>
    [
        new("Mesh, sprite and text renderer settings saved behind properties", ctx => RenameSettings(ctx, s_rendererRenames)),
        new("Light, fog light, decal and reflection probe settings saved behind properties", ctx => RenameSettings(ctx, s_lightRenames)),
        new("Texture import types: masks become linear Default textures, sprites need the Sprite type, sizes kept", ctx => ctx.RewriteImporterSettings("TextureImporter", TextureImportTypes)),
    ];

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
}
