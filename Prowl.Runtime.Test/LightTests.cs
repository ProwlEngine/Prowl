// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.Rendering;
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
        spot.OuterAngle = 120f;
        spot.InnerAngle = 100f;
        LightDesc desc = spot.Describe();
        Assert.Equal(89.9f, desc.OuterAngle);
        Assert.Equal(89.9f, desc.InnerAngle);
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
