// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0_Preview5
{
    private static MigrationStep[] GraphiteVolumeSteps =>
    [
        new("Camera effects to a base volume profile and anti aliasing", ctx => ctx.RewriteEchoAssets(ConvertCameraEffects)),
    ];

    private const string RuntimeAssembly = ", Prowl.Runtime";
    private const string OldEffects = "Prowl.Runtime.Rendering.";

    // An old effect field, the parameter it becomes, and its value when the saved effect lacks it
    private readonly record struct FieldMap(string Old, string New, EchoObject Default);

    private static FieldMap F(string name, EchoObject value) => new(name, name, value);
    private static FieldMap F(string old, string name, EchoObject value) => new(old, name, value);
    private static EchoObject EffectColor(float r, float g, float b, float a)
    {
        EchoObject color = EchoObject.NewCompound();
        color["R"] = new(r);
        color["G"] = new(g);
        color["B"] = new(b);
        color["A"] = new(a);
        return color;
    }

    // Effects that become one component each, with the parameters every saved value moves to
    private static readonly Dictionary<string, (string Component, FieldMap[] Fields)> s_effectMaps = new()
    {
        ["TonemapperEffect"] = ("Tonemapping", [F("Contrast", new(1.1f)), F("Saturation", new(1.1f))]),
        ["AutoExposureEffect"] = ("AutoExposure", [F("ExposureCompensation", new(0f)), F("AdaptSpeedUp", new(3f)), F("AdaptSpeedDown", new(1.5f)), F("MinExposure", new(0.1f)), F("MaxExposure", new(10f))]),
        ["BloomEffect"] = ("Bloom", [F("Intensity", new(1.5f)), F("Threshold", new(1.2f)), F("SoftKnee", new(0.5f)), F("Clamp", new(65000f)), F("AntiFlicker", new(false)), F("Iterations", new(6)), F("Scatter", new(0.7f))]),
        ["GTAOEffect"] = ("AmbientOcclusion", [F("Intensity", new(1f)), F("Radius", new(1f)), F("Slices", new(6)), F("DirectionSamples", new(32)), F("BlurRadius", new(1f)), F("UseTemporal", new(true)), F("TemporalResponse", new(0.9f)), F("Resolution", new(2L))]),
        ["ScreenSpaceReflectionEffect"] = ("ScreenSpaceReflections", [F("RayResolution", new(1L)), F("RayDistance", new(70f)), F("BRDFBias", new(0.7f)), F("RayReuse", new(true)), F("Normalization", new(true)), F("ReduceFireflies", new(true)), F("UseMipMap", new(true)), F("UseTemporal", new(true)), F("TemporalScale", new(2f)), F("TemporalResponse", new(0.85f)), F("ReflectionVelocity", new(true)), F("UseFresnel", new(true)), F("ScreenFadeSize", new(0.25f))]),
        ["MotionBlurEffect"] = ("MotionBlur", [F("Intensity", new(1f)), F("Samples", new(8)), F("MaxBlurRadius", new(40f))]),
        ["BokehDepthOfFieldEffect"] = ("DepthOfField", [F("ManualFocusPoint", "FocusDistance", new(10f)), F("FocusStrength", new(2f)), F("MaxBlurRadius", new(2f)), F("Resolution", new(2L)), F("Quality", new(1L)), F("FocusSpeed", new(4f))]),
        ["VolumetricFogEffect"] = ("VolumetricFog", [F("GlobalDensity", new(0.005f)), F("GlobalColorTint", EffectColor(1, 1, 1, 1)), F("Scattering", new(0.5f)), F("Extinction", new(1f)), F("Dithering", new(0.02f)), F("AmbientColor", EffectColor(0.4f, 0.5f, 0.6f, 1)), F("AmbientIntensity", new(0.3f)), F("EnableDirectional", new(true)), F("EnablePointLights", new(true)), F("EnableSpotLights", new(true)), F("MaxDistance", new(100f)), F("Steps", new(48)), F("DownsampleScale", new(2)), F("UpsampleDepthThreshold", new(0.1f)), F("EnableTemporalReprojection", new(true)), F("TemporalBlendWeight", new(0.9f))]),
    };

    // CinematicEffects splits into one component per enabled sub effect
    private static readonly (string Enable, string Component, FieldMap[] Fields)[] s_cinematicMaps =
    [
        ("EnableGodRays", "GodRays", [F("GodRayIntensity", "Intensity", new(0.5f)), F("GodRayDecay", "Decay", new(0.96f)), F("GodRayDensity", "Density", new(0.5f)), F("GodRayWeight", "Weight", new(0.6f)), F("GodRaySamples", "Samples", new(64)), F("GodRayThreshold", "Threshold", new(0.8f))]),
        ("EnablePixelation", "Pixelation", [F("PixelSize", new(4f))]),
        ("EnableChromaticAberration", "ChromaticAberration", [F("ChromaticIntensity", "Intensity", new(3f)), F("ChromaticDistortion", "Distortion", new(0.5f))]),
        ("EnableSharpen", "Sharpen", [F("SharpenAmount", "Amount", new(0.5f)), F("SharpenRadius", "Radius", new(1f))]),
        ("EnableEdgeDetection", "EdgeDetection", [F("EdgeIntensity", "Intensity", new(1f)), F("EdgeColor", "Color", EffectColor(0, 0, 0, 1)), F("EdgeBackgroundFade", "BackgroundFade", new(1f))]),
        ("EnableColorGrading", "ColorGrading", [F("PostExposure", new(0f)), F("Contrast", new(0f)), F("Saturation", new(0f)), F("Temperature", new(0f)), F("Lift", EffectColor(0, 0, 0, 0)), F("Gamma", EffectColor(0, 0, 0, 0)), F("Gain", EffectColor(0, 0, 0, 0))]),
        ("EnableLUT", "ColorLookup", [F("LUTTexture", "Texture", new(EchoType.Null, null)), F("LUTContribution", "Contribution", new(1f))]),
        ("EnableFilmGrain", "FilmGrain", [F("GrainIntensity", "Intensity", new(0.15f)), F("GrainResponse", "Response", new(0.5f))]),
        ("EnableVignette", "Vignette", [F("VignetteIntensity", "Intensity", new(0.4f)), F("VignetteSmoothness", "Smoothness", new(0.2f)), F("VignetteRoundness", "Roundness", new(1f))]),
    ];

    // Anti aliasing effects become Camera.AntiAliasing and its settings block
    private static readonly Dictionary<string, (long Mode, string Settings, FieldMap[] Fields)> s_antiAliasingMaps = new()
    {
        ["FXAAEffect"] = (1, "Fxaa", [F("EdgeThresholdMax", new(0.0625f)), F("EdgeThresholdMin", new(0.0312f)), F("SubpixelQuality", new(0.75f))]),
        ["SMAAEffect"] = (2, "Smaa", [F("EdgeThreshold", new(0.1f))]),
        ["TAAEffect"] = (3, "Taa", [F("BlendFactor", new(0.95f)), F("MotionBlendFactor", new(0.85f)), F("MotionScale", new(2f)), F("Sharpness", new(0.025f)), F("JitterSpread", new(8))]),
    };

    /// <summary>What a camera's old effect list becomes.</summary>
    internal sealed class ConvertedEffects
    {
        public EchoObject? Profile;
        public long AntiAliasing;
        public string? AntiAliasingSettings;
        public EchoObject? Settings;
    }

    internal static EchoObject? ConvertCameraEffects(EchoObject echo)
    {
        bool changed = false;
        ConvertCameraEffects(echo, ref changed);
        return changed ? echo : null;
    }

    private static void ConvertCameraEffects(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                ConvertCameraEffects(item, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (tag.TryGet("$type", out EchoObject? type) && type!.TagType == EchoType.String
            && type.StringValue.StartsWith("Prowl.Runtime.Camera,", StringComparison.Ordinal)
            && tag.TryGet("Effects", out EchoObject? effects))
        {
            ConvertedEffects converted = ConvertEffectList(effects!);
            tag.Remove("Effects");
            if (converted.Profile != null && !tag.TryGet("Profile", out _)) tag["Profile"] = converted.Profile;
            if (converted.AntiAliasingSettings != null)
            {
                tag["AntiAliasing"] = new EchoObject(converted.AntiAliasing);
                tag[converted.AntiAliasingSettings] = converted.Settings!;
            }
            changed = true;
        }

        if (tag.TryGet("Overrides", out EchoObject? overrides))
            ConvertEffectOverrides(overrides!, ref changed);

        foreach (string name in tag.GetNames().ToList())
            ConvertCameraEffects(tag[name], ref changed);
    }

    // Prefab instance overrides of a camera's whole Effects list become overrides of what it turned into
    private static void ConvertEffectOverrides(EchoObject overrides, ref bool changed)
    {
        List<EchoObject>? items = Items(overrides);
        if (items == null) return;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            EchoObject item = items[i];
            if (item.TagType != EchoType.Compound || !item.TryGet("Path", out EchoObject? pathTag) || pathTag!.TagType != EchoType.String) continue;
            string path = pathTag.StringValue;
            int member = path.LastIndexOf('/') + 1;
            string field = path[member..];
            if (field != "Effects" && !field.StartsWith("Effects.", StringComparison.Ordinal)) continue;

            changed = true;
            items.RemoveAt(i);
            if (field != "Effects" || !item.TryGet("Value", out EchoObject? value))
            {
                Runtime.Debug.LogWarning($"[Migration] Dropped the prefab override '{path}': camera effects moved into the camera's base volume profile.");
                continue;
            }

            string prefix = path[..member];
            ConvertedEffects converted = ConvertEffectList(value!);
            items.Insert(i, Override(prefix + "Profile", converted.Profile ?? new EchoObject(EchoType.Null, null)));
            if (converted.AntiAliasingSettings != null)
            {
                items.Insert(i + 1, Override(prefix + "AntiAliasing", new EchoObject(converted.AntiAliasing)));
                items.Insert(i + 2, Override(prefix + converted.AntiAliasingSettings, converted.Settings!));
            }
        }
    }

    private static EchoObject Override(string path, EchoObject value)
    {
        EchoObject entry = EchoObject.NewCompound();
        entry["Path"] = new EchoObject(path);
        entry["Value"] = value;
        return entry;
    }

    // The items of a list written plainly or as a reference body with $values
    private static List<EchoObject>? Items(EchoObject list)
    {
        if (list.TagType == EchoType.List) return list.List;
        if (list.TagType == EchoType.Compound && list.TryGet("$values", out EchoObject? values) && values!.TagType == EchoType.List) return values.List;
        return null;
    }

    internal static ConvertedEffects ConvertEffectList(EchoObject effects)
    {
        var result = new ConvertedEffects();
        var components = new List<EchoObject>();

        foreach (EchoObject effect in Items(effects) ?? [])
        {
            if (effect.TagType != EchoType.Compound || !effect.TryGet("$type", out EchoObject? typeTag) || typeTag!.TagType != EchoType.String) continue;
            string typeName = typeTag.StringValue;
            int comma = typeName.IndexOf(',');
            string fullName = comma >= 0 ? typeName[..comma].Trim() : typeName;
            string name = fullName.StartsWith(OldEffects, StringComparison.Ordinal) ? fullName[OldEffects.Length..] : "";
            bool enabled = !effect.TryGet("Enabled", out EchoObject? enabledTag) || enabledTag!.BoolValue;

            if (s_antiAliasingMaps.TryGetValue(name, out var aa))
            {
                if (result.AntiAliasingSettings != null) continue;
                EchoObject settings = EchoObject.NewCompound();
                foreach (FieldMap map in aa.Fields)
                    settings[map.New] = (effect.TryGet(map.Old, out EchoObject? v) ? v! : map.Default).Clone();
                result.AntiAliasing = enabled ? aa.Mode : 0;
                result.AntiAliasingSettings = aa.Settings;
                result.Settings = settings;
                continue;
            }

            if (name == "CinematicEffects")
            {
                foreach ((string enable, string component, FieldMap[] fields) in s_cinematicMaps)
                    if (effect.TryGet(enable, out EchoObject? on) && on!.BoolValue)
                        components.Add(Component(component, enabled, effect, fields));
                continue;
            }

            if (!s_effectMaps.TryGetValue(name, out var target))
            {
                Runtime.Debug.LogWarning($"[Migration] Dropped the camera effect '{fullName}': effects are volume components now, port it to a VolumeComponent.");
                continue;
            }

            EchoObject converted = Component(target.Component, enabled, effect, target.Fields);
            switch (name)
            {
                case "TonemapperEffect":
                    long op = effect.TryGet("Type", out EchoObject? opTag) ? opTag!.LongValue : 3;
                    converted["Mode"] = Parameter(new EchoObject(op + 1));
                    break;
                case "BokehDepthOfFieldEffect":
                    bool auto = !effect.TryGet("UseAutoFocus", out EchoObject? autoTag) || autoTag!.BoolValue;
                    converted["Mode"] = Parameter(new EchoObject(auto ? 1L : 2L));
                    break;
                case "AutoExposureEffect":
                case "ScreenSpaceReflectionEffect":
                case "VolumetricFogEffect":
                    converted["Enabled"] = Parameter(new EchoObject(true));
                    break;
            }
            components.Add(converted);
        }

        if (components.Count == 0) return result;

        EchoObject profile = EchoObject.NewCompound();
        profile["Name"] = new EchoObject("Camera Profile");
        profile["Components"] = new EchoObject(components);
        result.Profile = profile;
        return result;
    }

    private static EchoObject Component(string name, bool active, EchoObject source, FieldMap[] fields)
    {
        EchoObject component = EchoObject.NewCompound();
        component["$type"] = new EchoObject("Prowl.Runtime.Rendering." + name + RuntimeAssembly);
        component["Active"] = new EchoObject(active);
        foreach (FieldMap map in fields)
            component[map.New] = Parameter((source.TryGet(map.Old, out EchoObject? value) ? value! : map.Default).Clone());
        return component;
    }

    private static EchoObject Parameter(EchoObject value)
    {
        EchoObject parameter = EchoObject.NewCompound();
        parameter["Override"] = new EchoObject(true);
        parameter["Value"] = value;
        return parameter;
    }
}
