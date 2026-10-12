// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0_Preview5
{
    private static MigrationStep[] GraphiteLightAndCameraSteps =>
    [
        new("Directional, point and spot lights to one Light, MeshRenderer CastShadows to ShadowCasting", ctx =>
        {
            Dictionary<string, string> components = new(StringComparer.OrdinalIgnoreCase);
            ctx.RewriteEchoAssets(echo => { CollectLegacyComponents(echo, components); return null; });
            ctx.RewriteEchoAssets(echo => ConvertLightsAndShadows(echo, components));
        }),
    ];

    private const string LightTypeName = "Prowl.Runtime.Light";
    private const string MeshRendererType = "Prowl.Runtime.MeshRenderer";

    // The old light types and the value of the new Light.Type each becomes
    private static readonly (string Type, long Kind)[] s_legacyLights =
    [
        ("Prowl.Runtime.DirectionalLight", 0),
        ("Prowl.Runtime.PointLight", 1),
        ("Prowl.Runtime.SpotLight", 2),
    ];

    private static readonly (string From, string To)[] s_lightFieldRenames =
    [
        ("SpotAngle", "OuterAngle"),
        ("InnerSpotAngle", "InnerAngle"),
        ("ShadowQuality", "ShadowFilter"),
    ];

    /// <summary>The type name before the assembly part of a $type, or null.</summary>
    private static string? TypeName(EchoObject tag)
    {
        if (!tag.TryGet("$type", out EchoObject? type) || type!.TagType != EchoType.String) return null;
        string value = type.StringValue;
        int comma = value.IndexOf(',');
        return (comma < 0 ? value : value[..comma]).Trim();
    }

    /// <summary>Notes the identifiers of every old light and every MeshRenderer, so prefab overrides on them can be found.</summary>
    internal static void CollectLegacyComponents(EchoObject tag, Dictionary<string, string> components)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                CollectLegacyComponents(item, components);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        string? type = TypeName(tag);
        if (type == MeshRendererType || s_legacyLights.Any(l => l.Type == type))
            foreach (string id in new[] { "_identifier", "_prefabTemplateIdentity" })
                if (tag.TryGet(id, out EchoObject? guid) && guid!.TagType == EchoType.String && Guid.TryParse(guid.StringValue, out Guid parsed) && parsed != Guid.Empty)
                    components[guid.StringValue] = type!;

        foreach (string child in tag.GetNames().ToList())
            CollectLegacyComponents(tag[child], components);
    }

    /// <summary>Turns old light components into Light, MeshRenderer.CastShadows into ShadowCasting, and prefab overrides on either.</summary>
    internal static EchoObject? ConvertLightsAndShadows(EchoObject echo, IReadOnlyDictionary<string, string> components)
    {
        bool changed = false;
        ConvertLightsAndShadows(echo, components, ref changed);
        return changed ? echo : null;
    }

    private static void ConvertLightsAndShadows(EchoObject tag, IReadOnlyDictionary<string, string> components, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                ConvertLightsAndShadows(item, components, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        string? type = TypeName(tag);
        foreach ((string legacy, long lightType) in s_legacyLights)
        {
            if (type != legacy) continue;
            string full = tag["$type"].StringValue;
            tag["$type"] = new EchoObject(LightTypeName + full[legacy.Length..]);
            tag["Type"] = new EchoObject(lightType);
            foreach ((string from, string to) in s_lightFieldRenames)
                Rename(tag, from, to);
            changed = true;
        }

        if (type == MeshRendererType && tag.TryGet("CastShadows", out EchoObject? cast) && !tag.Contains("ShadowCasting"))
        {
            tag.Remove("CastShadows");
            tag["ShadowCasting"] = new EchoObject(CastsShadows(cast!) ? 1L : 0L);
            changed = true;
        }

        if (tag.TryGet("Path", out EchoObject? path) && path!.TagType == EchoType.String && tag.TryGet("Value", out EchoObject? value)
            && ConvertOverride(path.StringValue, value!, components) is { } converted)
        {
            tag["Path"] = new EchoObject(converted.Path);
            if (!ReferenceEquals(converted.Value, value)) tag["Value"] = converted.Value;
            changed = true;
        }

        foreach (string child in tag.GetNames().ToList())
            ConvertLightsAndShadows(tag[child], components, ref changed);
    }

    private static bool CastsShadows(EchoObject value) => value.TagType switch
    {
        EchoType.Bool => value.BoolValue,
        EchoType.String => bool.TryParse(value.StringValue, out bool parsed) && parsed,
        _ => value.LongValue != 0,
    };

    // An override path is {object}/{component}/{member.path}, the member renamed when its component was an old light or a MeshRenderer
    private static (string Path, EchoObject Value)? ConvertOverride(string path, EchoObject value, IReadOnlyDictionary<string, string> components)
    {
        string[] parts = path.Split('/', 3);
        if (parts.Length != 3 || !components.TryGetValue(parts[1], out string? type)) return null;

        int dot = parts[2].IndexOf('.');
        string field = dot < 0 ? parts[2] : parts[2][..dot];
        string rest = dot < 0 ? "" : parts[2][dot..];

        if (type == MeshRendererType)
        {
            if (field != "CastShadows") return null;
            return ($"{parts[0]}/{parts[1]}/ShadowCasting{rest}", new EchoObject(CastsShadows(value) ? 1L : 0L));
        }

        foreach ((string from, string to) in s_lightFieldRenames)
            if (field == from) return ($"{parts[0]}/{parts[1]}/{to}{rest}", value);
        return null;
    }

    private static void Rename(EchoObject tag, string from, string to)
    {
        if (!tag.TryGet(from, out EchoObject? value)) return;
        tag.Remove(from);
        if (!tag.Contains(to)) tag[to] = value!;
    }
}
