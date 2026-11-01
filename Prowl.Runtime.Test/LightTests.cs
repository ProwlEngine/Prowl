// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

using Xunit;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime.Test;

public class LightTests : RuntimeTestBase
{
    private static Light Make(LightType type)
    {
        Light light = new GameObject("Light").AddComponent<Light>();
        light.Type = type;
        return light;
    }

    [Theory]
    [InlineData(LightType.Directional, GLightType.Directional)]
    [InlineData(LightType.Point, GLightType.Point)]
    [InlineData(LightType.Spot, GLightType.Spot)]
    [InlineData(LightType.Rect, GLightType.Rect)]
    [InlineData(LightType.Disk, GLightType.Disk)]
    [InlineData(LightType.Tube, GLightType.Tube)]
    public void Type_MapsOntoGraphite(LightType type, GLightType expected)
        => Assert.Equal(expected, Make(type).Describe().Type);

    [Fact]
    public void Shapes_FillGraphitesSize()
    {
        Light rect = Make(LightType.Rect);
        rect.Width = 2f;
        rect.Height = 0.5f;
        Assert.Equal(new Float2(2f, 0.5f), rect.Describe().Size);

        Light disk = Make(LightType.Disk);
        disk.Width = 3f;
        Assert.Equal(1.5f, disk.Describe().Size.X);

        Light tube = Make(LightType.Tube);
        tube.Length = 4f;
        tube.SourceRadius = 0.1f;
        LightDesc desc = tube.Describe();
        Assert.Equal(4f, desc.Size.X);
        Assert.Equal(0.1f, desc.SourceRadius);
    }

    [Fact]
    public void Spot_ClampsItsAngles()
    {
        Light spot = Make(LightType.Spot);
        spot.SpotAngle = 240f;
        spot.SpotSoftness = -1f;
        LightDesc desc = spot.Describe();
        Assert.Equal(89.5f, desc.OuterAngle);
        Assert.Equal(89.5f, desc.InnerAngle);
    }

    [Fact]
    public void Spot_SplitsItsConeIntoGraphitesHalfAngles()
    {
        Light spot = Make(LightType.Spot);
        LightDesc defaults = spot.Describe();
        Assert.Equal(45f, defaults.OuterAngle, 3);
        Assert.Equal(30f, defaults.InnerAngle, 3);

        spot.SpotAngle = 60f;
        spot.SpotSoftness = 0.25f;
        LightDesc desc = spot.Describe();
        Assert.Equal(30f, desc.OuterAngle, 4);
        Assert.Equal(22.5f, desc.InnerAngle, 4);
    }

    [Fact]
    public void NewLights_AreA60WattBulbAt2700Kelvin_AndColorTintsTheTemperature()
    {
        Light light = Make(LightType.Point);
        Assert.True(light.UseTemperature);
        Assert.Equal(2700f, light.Temperature);
        Assert.Equal(800f / (4f * MathF.PI), light.Intensity, 4);
        Assert.Equal(Color.White, light.Color);

        light.Temperature = 2700f;
        light.Color = new Color(0.5f, 1f, 1f, 1f);
        Color blackbody = ColorTemperature.ToLinear(2700f);
        Assert.Equal(new Float3(0.5f * blackbody.R, blackbody.G, blackbody.B), light.Describe().Color);

        light.UseTemperature = false;
        Assert.Equal(new Float3(0.5f, 1f, 1f), light.Describe().Color);
    }

    [Fact]
    public void NewLights_MatchThePointDefaultPreset()
    {
        LightPreset bulb = Light.DefaultPreset(LightType.Point);
        Assert.Equal("60 W bulb", bulb.Name);

        Light fresh = new GameObject("Fresh").AddComponent<Light>();
        Light preset = Make(LightType.Point);
        preset.Intensity = 1f;
        preset.Temperature = 1000f;
        preset.ApplyPreset(bulb);
        Assert.Equal(LightType.Point, fresh.Type);
        Assert.Equal(preset.Intensity, fresh.Intensity);
        Assert.Equal(preset.Temperature, fresh.Temperature);
        Assert.Equal(preset.UseTemperature, fresh.UseTemperature);
        Assert.Equal(800f, fresh.FromStored(LightUnit.Lumen, fresh.Intensity), 3);
    }

    [Theory]
    [InlineData(LightType.Directional, "Midday sun")]
    [InlineData(LightType.Point, "60 W bulb")]
    [InlineData(LightType.Spot, "Downlight")]
    [InlineData(LightType.Rect, "Ceiling panel")]
    [InlineData(LightType.Disk, "Ceiling disk")]
    [InlineData(LightType.Tube, "Fluorescent tube")]
    public void DefaultPreset_IsOneOfTheTypesPresets(LightType type, string name)
    {
        LightPreset preset = Light.DefaultPreset(type);
        Assert.Equal(name, preset.Name);
        Assert.Contains(preset, Light.Presets(type));
        Assert.Contains(preset.Unit, Light.Units(type));
    }

    [Fact]
    public void SettingType_AppliesNoPreset()
    {
        Light light = Make(LightType.Point);
        light.Intensity = 5f;
        light.Temperature = 4000f;
        light.Type = LightType.Directional;
        Assert.Equal(5f, light.Intensity);
        Assert.Equal(4000f, light.Temperature);
    }

    [Fact]
    public void ApplyPreset_SetsShapeAndTemperature_ThenIntensityForThatShape()
    {
        Light spot = Make(LightType.Spot);
        spot.UseTemperature = false;
        spot.ApplyPreset(new LightPreset("Test", LightUnit.Lumen, 1000f, 3000f, SpotAngle: 60f));
        Assert.Equal(60f, spot.SpotAngle);
        Assert.True(spot.UseTemperature);
        Assert.Equal(3000f, spot.Temperature);
        Assert.Equal(1000f / (2f * MathF.PI * (1f - MathF.Cos(30f * MathF.PI / 180f))), spot.Intensity, 2);

        Light rect = Make(LightType.Rect);
        rect.ApplyPreset(new LightPreset("Test", LightUnit.Nit, 250f, 6500f, Width: 0.6f, Height: 0.34f));
        Assert.Equal(0.6f, rect.Width);
        Assert.Equal(0.34f, rect.Height);
        Assert.Equal(250f, rect.Intensity);
    }

    [Fact]
    public void Units_ConvertToAndFromWhatTheLightStores()
    {
        Light point = Make(LightType.Point);
        Assert.Equal(100f / (4f * MathF.PI), point.ToStored(LightUnit.Lumen, 100f), 4);
        Assert.Equal(100f, point.FromStored(LightUnit.Lumen, point.ToStored(LightUnit.Lumen, 100f)), 3);
        Assert.Equal(7f, point.ToStored(LightUnit.Candela, 7f));

        Light sun = Make(LightType.Directional);
        Assert.Equal(2.5f * 1024f, sun.ToStored(LightUnit.Ev100, 10f), 2);
        Assert.Equal(10f, sun.FromStored(LightUnit.Ev100, 2.5f * 1024f), 4);

        Light disk = Make(LightType.Disk);
        disk.Width = 2f;
        Assert.Equal(1000f / (MathF.PI * MathF.PI), disk.ToStored(LightUnit.Lumen, 1000f), 3);

        Light tube = Make(LightType.Tube);
        tube.Length = 1f;
        tube.SourceRadius = 0.1f;
        float area = 2f * MathF.PI * 0.1f + 4f * MathF.PI * 0.01f;
        Assert.Equal(1000f / (MathF.PI * area), tube.ToStored(LightUnit.Lumen, 1000f), 2);
    }

    [Fact]
    public void Range_IsAutomatic_FromTheAxisCandelaAndTheCutoff()
    {
        float cutoff = QualitySettings.Active.LightCutoff;
        Assert.Equal(0.01f, cutoff);

        Light point = Make(LightType.Point);
        point.UseTemperature = false;
        point.Intensity = 1000f;
        Assert.False(point.OverrideRange);
        Assert.Equal(MathF.Sqrt(1000f / cutoff), point.Range, 3);
        Assert.Equal(point.Range, point.Describe().Range);

        point.Color = new Color(0.25f, 0.5f, 0.1f, 1f);
        Assert.Equal(MathF.Sqrt(1000f * 0.5f / cutoff), point.Range, 3);

        Light rect = Make(LightType.Rect);
        rect.UseTemperature = false;
        rect.Intensity = 100f;
        rect.Width = 2f;
        rect.Height = 0.5f;
        Assert.Equal(MathF.Sqrt(100f / cutoff), rect.Range, 3);

        Light disk = Make(LightType.Disk);
        disk.UseTemperature = false;
        disk.Intensity = 100f;
        disk.Width = 2f;
        Assert.Equal(MathF.Sqrt(100f * MathF.PI / cutoff), disk.Range, 3);

        Light tube = Make(LightType.Tube);
        tube.UseTemperature = false;
        tube.Intensity = 100f;
        tube.Length = 2f;
        tube.SourceRadius = 0.1f;
        Assert.Equal(MathF.Sqrt(100f * (2f * 0.1f * 2f + MathF.PI * 0.01f) / cutoff), tube.Range, 3);

        Assert.Equal(0f, Make(LightType.Directional).Range);
    }

    [Fact]
    public void Range_FollowsTheQualityLevelsCutoff()
    {
        Light point = Make(LightType.Point);
        point.UseTemperature = false;
        point.Intensity = 800f;
        QualityLevel level = QualitySettings.Active;
        float before = level.LightCutoff;
        try
        {
            level.LightCutoff = 2f;
            Assert.Equal(20f, point.Range, 3);
        }
        finally
        {
            level.LightCutoff = before;
        }
    }

    [Fact]
    public void Range_SetByHand_OverridesTheAutomaticOne()
    {
        Light point = Make(LightType.Point);
        point.Range = 6f;
        Assert.True(point.OverrideRange);
        Assert.Equal(6f, point.Describe().Range);

        point.OverrideRange = false;
        Assert.Equal(point.AutomaticRange, point.Range);
    }

    [Fact]
    public void ColorTemperature_FollowsThePlanckianLocus()
    {
        Float2 xy = ColorTemperature.Chromaticity(2700f);
        Assert.Equal(0.4599f, xy.X, 3);
        Assert.Equal(0.4106f, xy.Y, 3);

        Color daylight = ColorTemperature.ToLinear(6500f);
        Assert.Equal(1f, MathF.Max(daylight.R, MathF.Max(daylight.G, daylight.B)), 5);
        Assert.InRange(daylight.R, 0.93f, 1f);
        Assert.InRange(daylight.G, 0.93f, 1f);
        Assert.InRange(daylight.B, 0.93f, 1f);

        Color bulb = ColorTemperature.ToLinear(2700f);
        Assert.Equal(1f, bulb.R, 5);
        Assert.Equal(0.416f, bulb.G, 2);
        Assert.Equal(0.098f, bulb.B, 2);

        Color candle = ColorTemperature.ToLinear(1900f);
        Assert.Equal(1f, candle.R, 5);
        Assert.Equal(0.232f, candle.G, 2);
        Assert.InRange(candle.B, 0f, 0.01f);
    }

    [Fact]
    public void Directional_TakesItsCascadesFromTheActiveQualityLevel()
    {
        QualityLevel quality = QualitySettings.Active;
        Light sun = Make(LightType.Directional);
        sun.ShadowResolution = ShadowResolution._4096;
        sun.AngularRadius = 0.5f;
        LightDesc desc = sun.Describe();
        Assert.Equal((int)quality.ShadowCascades, desc.Shadow.Cascades);
        Assert.Equal(quality.ShadowDistance, desc.Shadow.Distance);
        Assert.Equal((uint)quality.MaxShadowResolution, desc.Shadow.Resolution);
        Assert.Equal(0.5f, desc.AngularRadius);
    }

    [Theory]
    [InlineData(LightType.Rect)]
    [InlineData(LightType.Disk)]
    [InlineData(LightType.Tube)]
    public void AreaLights_CastNoShadows(LightType type)
    {
        Light light = Make(type);
        light.CastShadows = true;
        Assert.False(light.Describe().Shadow.Enabled);
    }

    [Fact]
    public void ShadowSettings_PassThrough()
    {
        Light light = Make(LightType.Point);
        light.ShadowResolution = ShadowResolution._2048;
        light.ShadowFilter = ShadowFilter.ContactHardening;
        light.ShadowStrength = 0.5f;
        light.LayerMask = LayerMask.FromMask(0b101);
        LightDesc desc = light.Describe();
        Assert.True(desc.Shadow.Enabled);
        Assert.Equal(2048u, desc.Shadow.Resolution);
        Assert.Equal(Graphite.Rendering.ShadowFilter.ContactHardening, desc.Shadow.Filter);
        Assert.Equal(0.5f, desc.Shadow.Strength);
        Assert.Equal(0b101u, desc.LayerMask);
    }

    [Theory]
    [InlineData(ShadowCasting.Off, RenderableFlags.None)]
    [InlineData(ShadowCasting.On, RenderableFlags.CastShadows)]
    [InlineData(ShadowCasting.ShadowsOnly, RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly)]
    public void ShadowCasting_MapsOntoRenderableFlags(ShadowCasting casting, RenderableFlags expected)
        => Assert.Equal(expected, casting.ToFlags());
}
