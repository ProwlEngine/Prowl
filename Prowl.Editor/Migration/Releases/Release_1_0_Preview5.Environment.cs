// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0_Preview5
{
    private static MigrationStep[] GraphiteEnvironmentSteps =>
    [
        new("Scene Skybox, Fog and Ambient to Environment", ctx => ctx.RewriteEchoAssets(ConvertSceneEnvironments)),
    ];

    private enum LegacySkyboxMode : long { Procedural, SolidColor, Gradient, Material }

    internal static EchoObject? ConvertSceneEnvironments(EchoObject echo)
    {
        bool changed = false;
        ConvertSceneEnvironments(echo, ref changed);
        return changed ? echo : null;
    }

    private static void ConvertSceneEnvironments(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                ConvertSceneEnvironments(item, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (IsOldScene(tag))
        {
            ConvertSceneEnvironment(tag);
            changed = true;
            return;
        }

        foreach (string child in tag.GetNames().ToList())
            ConvertSceneEnvironments(tag[child], ref changed);
    }

    private static bool IsOldScene(EchoObject tag)
    {
        if (!tag.Contains("Skybox") && !tag.Contains("Fog") && !tag.Contains("Ambient")) return false;
        if (tag.Contains("Skybox")) return true;
        return tag.TryGet("$type", out EchoObject? type) && type!.TagType == EchoType.String
            && type.StringValue.StartsWith("Prowl.Runtime.Resources.Scene,", StringComparison.Ordinal);
    }

    // The old SkyboxMode, Fog and Ambient fields of a scene become its Environment: a procedural or gradient skybox an
    // embedded material of Graphite's sky with the same values, a solid colour no sky with that colour behind it, and a
    // custom material the sky material itself.
    private static void ConvertSceneEnvironment(EchoObject scene)
    {
        EchoObject environment = scene.TryGet("Environment", out EchoObject? existing) && existing!.TagType == EchoType.Compound ? existing : EchoObject.NewCompound();

        if (scene.TryGet("Skybox", out EchoObject? skybox) && skybox!.TagType == EchoType.Compound)
        {
            LegacySkyboxMode mode = skybox.TryGet("Mode", out EchoObject? m) ? (LegacySkyboxMode)m!.LongValue : LegacySkyboxMode.Procedural;
            environment["SkyMaterial"] = mode switch
            {
                LegacySkyboxMode.SolidColor => new EchoObject(EchoType.Null, null),
                LegacySkyboxMode.Gradient => GradientSky(skybox),
                LegacySkyboxMode.Material => skybox.TryGet("CustomMaterial", out EchoObject? custom) ? custom!.Clone() : new EchoObject(EchoType.Null, null),
                _ => SkyMaterial("Procedural", EchoObject.NewCompound(), EchoObject.NewCompound()),
            };
            if (skybox.TryGet("SolidColor", out EchoObject? solid)) environment["SkyColor"] = solid!.Clone();
            scene.Remove("Skybox");
        }

        if (scene.TryGet("Ambient", out EchoObject? ambient) && ambient!.TagType == EchoType.Compound)
        {
            EchoObject converted = EchoObject.NewCompound();
            if (ambient.TryGet("Mode", out EchoObject? mode)) converted["Mode"] = mode!.Clone();
            if (ambient.TryGet("Strength", out EchoObject? strength)) converted["Intensity"] = strength!.Clone();
            foreach (string color in new[] { "Color", "SkyColor", "GroundColor" })
                if (ambient.TryGet(color, out EchoObject? value)) converted[color] = ToColor(value!);
            environment["Ambient"] = converted;
            scene.Remove("Ambient");
        }

        if (scene.TryGet("Fog", out EchoObject? fog) && fog!.TagType == EchoType.Compound)
        {
            EchoObject converted = EchoObject.NewCompound();
            foreach (string field in new[] { "Mode", "Color", "Density", "Start", "End", "UseSky" })
                if (fog.TryGet(field, out EchoObject? value)) converted[field] = value!.Clone();
            environment["Fog"] = converted;
            scene.Remove("Fog");
        }

        scene["Environment"] = environment;
    }

    private static EchoObject GradientSky(EchoObject skybox)
    {
        EchoObject colors = EchoObject.NewCompound();
        colors["_TopColor"] = skybox.TryGet("GradientTop", out EchoObject? top) ? top!.Clone() : Rgba(0.4f, 0.6f, 0.9f, 1f);
        colors["_BottomColor"] = skybox.TryGet("GradientBottom", out EchoObject? bottom) ? bottom!.Clone() : Rgba(0.8f, 0.8f, 0.7f, 1f);
        EchoObject floats = EchoObject.NewCompound();
        floats["_Exponent"] = skybox.TryGet("GradientExponent", out EchoObject? exponent) ? exponent!.Clone() : new EchoObject(1f);
        return SkyMaterial("Gradient", colors, floats);
    }

    // An embedded material of one of Graphite's skies, every value given counted as set on it
    private static EchoObject SkyMaterial(string sky, EchoObject colors, EchoObject floats)
    {
        EchoObject shader = EchoObject.NewCompound();
        shader["$asset"] = new EchoObject(BuiltInAssets.DeterministicGuid($"$Default:Shader/{sky}Sky").ToString());

        EchoObject properties = EchoObject.NewCompound();
        properties["_colors"] = colors;
        properties["_floats"] = floats;

        EchoObject overrides = EchoObject.NewList();
        foreach (string name in colors.GetNames().Concat(floats.GetNames()))
            overrides.ListAdd(new EchoObject(name));

        EchoObject material = EchoObject.NewCompound();
        material["_shader"] = shader;
        material["_properties"] = properties;
        material["_overrides"] = overrides;
        material["Name"] = new EchoObject($"{sky} Sky");
        return material;
    }

    // Ambient colours were Float4 (X, Y, Z, W) and are now Colors (R, G, B, A)
    private static EchoObject ToColor(EchoObject value)
    {
        if (value.TagType != EchoType.Compound || !value.Contains("X")) return value.Clone();
        EchoObject color = EchoObject.NewCompound();
        color["R"] = value["X"].Clone();
        color["G"] = value["Y"].Clone();
        color["B"] = value["Z"].Clone();
        color["A"] = value.TryGet("W", out EchoObject? w) ? w!.Clone() : new EchoObject(1f);
        return color;
    }

    private static EchoObject Rgba(float r, float g, float b, float a)
    {
        EchoObject color = EchoObject.NewCompound();
        color["R"] = new EchoObject(r);
        color["G"] = new EchoObject(g);
        color["B"] = new EchoObject(b);
        color["A"] = new EchoObject(a);
        return color;
    }
}
