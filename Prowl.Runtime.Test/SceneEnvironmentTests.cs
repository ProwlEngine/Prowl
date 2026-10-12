// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using Material = Prowl.Runtime.Resources.Material;

namespace Prowl.Runtime.Test;

public class SceneEnvironmentTests
{
    [Fact]
    public void ANewScene_HasAnEmbeddedProceduralSky()
    {
        var scene = new Scene();

        Material? sky = scene.Environment.SkyMaterial;
        Assert.True(sky.IsValid());
        Assert.Same(Prowl.Runtime.Resources.Shader.LoadDefault(DefaultShader.ProceduralSky), sky.Shader);
        Assert.True(sky.HasProperty("_SunSize"));
        Assert.True(scene.Rendering.HasSky);
    }

    [Theory]
    [InlineData(BuiltInSky.Procedural, "_Exposure")]
    [InlineData(BuiltInSky.Gradient, "_TopColor")]
    [InlineData(BuiltInSky.Cubemap, "_Cubemap")]
    [InlineData(BuiltInSky.Panorama, "_Panorama")]
    [InlineData(BuiltInSky.SixSided, "_CubeFront")]
    public void EachBuiltInSky_IsASkyShader_SharedByItsMaterials(BuiltInSky kind, string property)
    {
        Material material = SceneEnvironment.CreateSkyMaterial(kind);

        Assert.True(material.Shader!.HasPass(PassType.Sky));
        Assert.True(material.HasProperty(property));
        Assert.Same(material.Shader, SceneEnvironment.CreateSkyMaterial(kind).Shader);
    }

    [Fact]
    public void ApplyTo_CopiesEverySetting_OntoTheWorldsEnvironment()
    {
        SceneEnvironment environment = new() { SkyMaterial = null, SkyColor = new Color(0.1f, 0.2f, 0.3f, 1) };
        environment.Ambient = new SceneEnvironment.AmbientSettings { Mode = AmbientMode.Hemisphere, Intensity = 0.5f, Color = new Color(1f, 0f, 0f, 1f), SkyColor = new Color(0f, 1f, 0f, 1f), GroundColor = new Color(0f, 0f, 1f, 1f) };
        environment.Fog = new SceneEnvironment.FogSettings { Mode = FogMode.Linear, Color = new Color(0.4f, 0.4f, 0.4f, 1), Density = 0.03f, Start = 2, End = 30, UseSky = true };
        environment.SkyCapture = new SceneEnvironment.SkyCaptureSettings { Mode = SkyCaptureMode.EveryFrame, Resolution = 100, Interval = 0 };

        EnvironmentDesc desc = new() { Sky = null };
        environment.ApplyTo(desc);

        Assert.Equal(Prowl.Graphite.Rendering.AmbientMode.Hemisphere, desc.Ambient);
        Assert.Equal(0.5f, desc.AmbientIntensity);
        Assert.Equal(new Float4(1, 0, 0, 1), desc.AmbientColor);
        Assert.Equal(new Float4(0, 1, 0, 1), desc.AmbientSky);
        Assert.Equal(new Float4(0, 0, 1, 1), desc.AmbientGround);
        Assert.Equal(new FogDesc { Mode = Prowl.Graphite.Rendering.FogMode.Linear, Color = new Float4(0.4f, 0.4f, 0.4f, 1), Density = 0.03f, Start = 2, End = 30, UseSky = true }, desc.Fog);
        Assert.Equal(new Float4(0.1f, 0.2f, 0.3f, 1), desc.SkyColor);
        Assert.Null(desc.Sky);
        Assert.Equal(Prowl.Graphite.Rendering.SkyCaptureMode.EveryFrame, desc.SkyCapture);
        Assert.Equal(128u, desc.SkyProbeResolution);
        Assert.Equal(1, desc.SkyCaptureInterval);
    }

    [Fact]
    public void ANoSkyScene_ShowsNoSky()
    {
        var scene = new Scene();
        scene.Environment.SkyMaterial = null;
        Assert.False(scene.Rendering.HasSky);
    }

    [Fact]
    public void EvaluateAmbient_FollowsTheMode_AndSkyFallsBackToTheSkyColourWithoutAWorld()
    {
        SceneEnvironment environment = new() { SkyColor = new Color(0.2f, 0.4f, 0.6f, 1) };
        environment.Ambient.Intensity = 2;

        environment.Ambient.Mode = AmbientMode.Uniform;
        environment.Ambient.Color = new Color(0.1f, 0.2f, 0.3f, 1);
        Assert.Equal(new Float3(0.2f, 0.4f, 0.6f), environment.EvaluateAmbient(Float3.UnitY, null));

        environment.Ambient.Mode = AmbientMode.Hemisphere;
        environment.Ambient.SkyColor = new Color(1f, 0f, 0f, 1f);
        environment.Ambient.GroundColor = new Color(0f, 0f, 1f, 1f);
        Assert.Equal(new Float3(2, 0, 0), environment.EvaluateAmbient(Float3.UnitY, null));
        Assert.Equal(new Float3(1, 0, 1), environment.EvaluateAmbient(Float3.UnitX, null));

        environment.Ambient.Mode = AmbientMode.Sky;
        Assert.Equal(new Float3(0.4f, 0.8f, 1.2f), environment.EvaluateAmbient(Float3.UnitY, null));
    }

    [Fact]
    public void ASavedEnvironment_LoadsBackItsSkyAndSettings_AndANullSkyStaysNull()
    {
        SceneEnvironment environment = new();
        environment.SkyMaterial = SceneEnvironment.CreateSkyMaterial(BuiltInSky.Gradient);
        environment.SkyMaterial.SetFloat("_Exponent", 3);
        environment.Fog.Mode = FogMode.Off;

        SceneEnvironment loaded = Serializer.Deserialize<SceneEnvironment>(Serializer.Serialize(environment))!;
        Assert.Same(Prowl.Runtime.Resources.Shader.LoadDefault(DefaultShader.GradientSky), loaded.SkyMaterial!.Shader);
        Assert.Equal(3f, loaded.SkyMaterial.GetFloat("_Exponent"));
        Assert.Equal(FogMode.Off, loaded.Fog.Mode);

        environment.SkyMaterial = null;
        loaded = Serializer.Deserialize<SceneEnvironment>(Serializer.Serialize(environment))!;
        Assert.True(loaded.SkyMaterial.IsNotValid());
    }
}
