// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Migration.Releases;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class VolumeMigrationTests
{
    private static EchoObject Effect(string type, bool enabled, params (string Name, EchoObject Value)[] fields)
    {
        EchoObject effect = EchoObject.NewCompound();
        effect["$type"] = new EchoObject($"Prowl.Runtime.Rendering.{type}, Prowl.Runtime, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        foreach ((string name, EchoObject value) in fields) effect[name] = value;
        effect["Enabled"] = new EchoObject(enabled);
        return effect;
    }

    private static EchoObject EffectList(params EchoObject[] effects)
    {
        EchoObject list = EchoObject.NewCompound();
        list["$id"] = new EchoObject(900);
        list["$values"] = new EchoObject([.. effects]);
        return list;
    }

    private static EchoObject CameraComponent(EchoObject gameObject)
        => gameObject["Components"].List.First(c => c["$type"].StringValue.StartsWith("Prowl.Runtime.Camera,", StringComparison.Ordinal));

    // A GameObject saved with a camera holding the old Effects list
    private static EchoObject LegacyCamera(params EchoObject[] effects)
    {
        var go = new GameObject("Main Camera");
        go.AddComponent<Camera>();
        EchoObject echo = Serializer.Serialize(go);
        EchoObject camera = CameraComponent(echo);
        foreach (string name in new[] { "Profile", "AntiAliasing", "Fxaa", "Smaa", "Taa", "PostProcessing", "VolumeMask", "VolumeTrigger" })
            camera.Remove(name);
        camera["Effects"] = EffectList(effects);
        return echo;
    }

    private static Camera Migrate(EchoObject echo)
    {
        Assert.NotNull(Release_1_0_Preview5.ConvertCameraEffects(echo));
        Assert.False(CameraComponent(echo).TryGet("Effects", out _));
        return Serializer.Deserialize<GameObject>(echo)!.GetComponent<Camera>()!;
    }

    [Fact]
    public void Effects_BecomeTheBaseProfile_WithEveryFieldOverridden()
    {
        EchoObject echo = LegacyCamera(
            Effect("TonemapperEffect", true, ("Type", new EchoObject(3L)), ("Contrast", new EchoObject(1.3f))),
            Effect("BloomEffect", true, ("Intensity", new EchoObject(2f))));

        Camera camera = Migrate(echo);

        Assert.True(camera.Profile.IsValid());
        Assert.False(camera.Profile.IsFromDatabase);
        Tonemapping tonemapping = camera.Profile.Get<Tonemapping>()!;
        Assert.Equal(TonemappingMode.AgX, tonemapping.Mode.Value);
        Assert.Equal(1.3f, tonemapping.Contrast.Value);
        Assert.Equal(1.1f, tonemapping.Saturation.Value);
        Assert.All(tonemapping.Parameters, p => Assert.True(p.Override));

        Bloom bloom = camera.Profile.Get<Bloom>()!;
        Assert.Equal(2f, bloom.Intensity.Value);
        Assert.Equal(1.2f, bloom.Threshold.Value);
        Assert.True(bloom.Active);
        Assert.All(bloom.Parameters, p => Assert.True(p.Override));
        Assert.Equal(AntiAliasingMode.None, camera.AntiAliasing);
    }

    [Fact]
    public void AntiAliasingEffect_BecomesCameraAntiAliasing_WithItsSettings()
    {
        EchoObject echo = LegacyCamera(
            Effect("TAAEffect", true, ("BlendFactor", new EchoObject(0.8f)), ("JitterSpread", new EchoObject(4))),
            Effect("FXAAEffect", true));

        Camera camera = Migrate(echo);

        Assert.Equal(AntiAliasingMode.TAA, camera.AntiAliasing);
        Assert.Equal(0.8f, camera.Taa.BlendFactor);
        Assert.Equal(4, camera.Taa.JitterSpread);
        Assert.Equal(0.025f, camera.Taa.Sharpness);
        Assert.True(camera.Profile.IsNotValid());
    }

    [Fact]
    public void DisabledAntiAliasingEffect_KeepsItsSettings_ButNotTheMode()
    {
        Camera camera = Migrate(LegacyCamera(Effect("SMAAEffect", false, ("EdgeThreshold", new EchoObject(0.05f)))));

        Assert.Equal(AntiAliasingMode.None, camera.AntiAliasing);
        Assert.Equal(0.05f, camera.Smaa.EdgeThreshold);
    }

    [Fact]
    public void CinematicEffects_SplitIntoTheEnabledParts()
    {
        EchoObject echo = LegacyCamera(Effect("CinematicEffects", false,
            ("EnableVignette", new EchoObject(true)), ("VignetteIntensity", new EchoObject(0.6f)),
            ("EnableChromaticAberration", new EchoObject(false)), ("ChromaticIntensity", new EchoObject(5f)),
            ("EnableColorGrading", new EchoObject(true)), ("Contrast", new EchoObject(0.25f))));

        Camera camera = Migrate(echo);

        VolumeProfile profile = camera.Profile!;
        Assert.Equal(2, profile.Components.Count);
        Vignette vignette = profile.Get<Vignette>()!;
        Assert.Equal(0.6f, vignette.Intensity.Value);
        Assert.Equal(0.2f, vignette.Smoothness.Value);
        Assert.False(vignette.Active);
        Assert.Equal(0.25f, profile.Get<ColorGrading>()!.Contrast.Value);
        Assert.False(profile.Has<ChromaticAberration>());
    }

    [Fact]
    public void ModeParameters_ComeFromTheOldSwitches()
    {
        Camera camera = Migrate(LegacyCamera(
            Effect("BokehDepthOfFieldEffect", true, ("UseAutoFocus", new EchoObject(false)), ("ManualFocusPoint", new EchoObject(7f))),
            Effect("ScreenSpaceReflectionEffect", true),
            Effect("VolumetricFogEffect", true, ("GlobalDensity", new EchoObject(0.02f)), ("EnableDirectionalShadows", new EchoObject(false)))));

        DepthOfField dof = camera.Profile!.Get<DepthOfField>()!;
        Assert.Equal(DepthOfFieldMode.Manual, dof.Mode.Value);
        Assert.Equal(7f, dof.FocusDistance.Value);
        Assert.True(camera.Profile.Get<ScreenSpaceReflections>()!.Enabled.Value);
        VolumetricFog fog = camera.Profile.Get<VolumetricFog>()!;
        Assert.True(fog.Enabled.Value);
        Assert.Equal(0.02f, fog.GlobalDensity.Value);
    }

    [Fact]
    public void UnknownEffect_IsDropped()
    {
        EchoObject custom = EchoObject.NewCompound();
        custom["$type"] = new EchoObject("MyGame.ScreenShake, MyGame");
        Camera camera = Migrate(LegacyCamera(custom, Effect("BloomEffect", true)));

        Assert.Single(camera.Profile!.Components);
    }

    [Fact]
    public void RunningTwice_ChangesNothingTheSecondTime()
    {
        EchoObject echo = LegacyCamera(Effect("BloomEffect", true));
        Assert.NotNull(Release_1_0_Preview5.ConvertCameraEffects(echo));
        Assert.Null(Release_1_0_Preview5.ConvertCameraEffects(echo));
    }

    [Fact]
    public void PrefabOverrides_OnEffects_AreRewrittenOrDropped()
    {
        EchoObject overrides = EchoObject.NewList();
        EchoObject Override(string path, EchoObject value)
        {
            EchoObject o = EchoObject.NewCompound();
            o["Path"] = new EchoObject(path);
            o["Value"] = value;
            return o;
        }
        overrides.ListAdd(Override("go/cam/Effects", EffectList(Effect("BloomEffect", true, ("Intensity", new EchoObject(3f))), Effect("FXAAEffect", true))));
        overrides.ListAdd(Override("go/cam/Effects.0.Intensity", new EchoObject(1f)));
        overrides.ListAdd(Override("go/cam/FieldOfView", new EchoObject(50f)));
        EchoObject link = EchoObject.NewCompound();
        link["Overrides"] = overrides;
        EchoObject root = EchoObject.NewCompound();
        root["Prefab"] = link;

        Assert.NotNull(Release_1_0_Preview5.ConvertCameraEffects(root));

        List<string> paths = [.. overrides.List.Select(o => o["Path"].StringValue)];
        Assert.Equal(["go/cam/Profile", "go/cam/AntiAliasing", "go/cam/Fxaa", "go/cam/FieldOfView"], paths);
        VolumeProfile profile = Serializer.Deserialize<VolumeProfile>(overrides.List[0]["Value"])!;
        Assert.Equal(3f, profile.Get<Bloom>()!.Intensity.Value);
        Assert.Equal(1L, overrides.List[1]["Value"].LongValue);
    }
}
