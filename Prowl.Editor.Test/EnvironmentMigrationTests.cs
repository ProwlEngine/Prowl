// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Migration.Releases;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

public class EnvironmentMigrationTests
{
    private static string Guid(string path) => BuiltInAssets.DeterministicGuid(path).ToString();

    private static EchoObject Vector(string a, string b, string c, string d, float x, float y, float z, float w)
    {
        EchoObject v = EchoObject.NewCompound();
        v[a] = new EchoObject(x);
        v[b] = new EchoObject(y);
        v[c] = new EchoObject(z);
        v[d] = new EchoObject(w);
        return v;
    }

    private static EchoObject Rgba(float r, float g, float b, float a) => Vector("R", "G", "B", "A", r, g, b, a);
    private static EchoObject Xyzw(float x, float y, float z, float w) => Vector("X", "Y", "Z", "W", x, y, z, w);

    // A scene as the GL era saved it: its skybox, ambient and fog as three fields of the scene
    private static EchoObject OldScene(long skyMode, EchoObject? customMaterial = null)
    {
        EchoObject skybox = EchoObject.NewCompound();
        skybox["Mode"] = new EchoObject(skyMode);
        skybox["SolidColor"] = Rgba(0.1f, 0.2f, 0.3f, 1);
        skybox["GradientTop"] = Rgba(0.9f, 0.8f, 0.7f, 1);
        skybox["GradientBottom"] = Rgba(0.1f, 0.1f, 0.2f, 1);
        skybox["GradientExponent"] = new EchoObject(2.5f);
        skybox["CustomMaterial"] = customMaterial ?? new EchoObject(EchoType.Null, null);

        EchoObject ambient = EchoObject.NewCompound();
        ambient["Mode"] = new EchoObject(1L);
        ambient["Strength"] = new EchoObject(0.5f);
        ambient["Color"] = Xyzw(0.4f, 0.5f, 0.6f, 1);
        ambient["SkyColor"] = Xyzw(0.7f, 0.8f, 0.9f, 1);
        ambient["GroundColor"] = Xyzw(0.1f, 0.2f, 0.1f, 1);

        EchoObject fog = EchoObject.NewCompound();
        fog["Mode"] = new EchoObject(1L);
        fog["Color"] = Rgba(0.3f, 0.3f, 0.35f, 1);
        fog["Start"] = new EchoObject(5f);
        fog["End"] = new EchoObject(50f);
        fog["Density"] = new EchoObject(0.02f);
        fog["UseSky"] = new EchoObject(true);

        EchoObject scene = EchoObject.NewCompound();
        scene["$type"] = new EchoObject("Prowl.Runtime.Resources.Scene, Prowl.Runtime, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        scene["Fog"] = fog;
        scene["Ambient"] = ambient;
        scene["Skybox"] = skybox;
        scene["Name"] = new EchoObject("Old");
        return scene;
    }

    [Fact]
    public void AProceduralSkybox_BecomesAnEmbeddedProceduralSkyMaterial()
    {
        EchoObject scene = OldScene(0);

        Assert.NotNull(Release_1_0_Preview5.ConvertSceneEnvironments(scene));

        Assert.False(scene.Contains("Skybox"));
        Assert.False(scene.Contains("Fog"));
        Assert.False(scene.Contains("Ambient"));
        EchoObject environment = scene["Environment"];
        Assert.Equal(Guid("$Default:Shader/ProceduralSky"), environment["SkyMaterial"]["_shader"]["$asset"].StringValue);
        Assert.Equal(0.2f, environment["SkyColor"]["G"].FloatValue);
    }

    [Fact]
    public void AGradientSkybox_KeepsItsColoursAndExponent_AsSetValues()
    {
        EchoObject scene = OldScene(2);

        Release_1_0_Preview5.ConvertSceneEnvironments(scene);

        EchoObject material = scene["Environment"]["SkyMaterial"];
        Assert.Equal(Guid("$Default:Shader/GradientSky"), material["_shader"]["$asset"].StringValue);
        Assert.Equal(0.9f, material["_properties"]["_colors"]["_TopColor"]["R"].FloatValue);
        Assert.Equal(0.2f, material["_properties"]["_colors"]["_BottomColor"]["B"].FloatValue);
        Assert.Equal(2.5f, material["_properties"]["_floats"]["_Exponent"].FloatValue);
        Assert.Equal(["_BottomColor", "_Exponent", "_TopColor"], material["_overrides"].List.Select(o => o.StringValue).Order());
    }

    [Fact]
    public void ASolidColourSkybox_HasNoSkyMaterial_AndKeepsItsColour()
    {
        EchoObject scene = OldScene(1);

        Release_1_0_Preview5.ConvertSceneEnvironments(scene);

        EchoObject environment = scene["Environment"];
        Assert.Equal(EchoType.Null, environment["SkyMaterial"].TagType);
        Assert.Equal(0.3f, environment["SkyColor"]["B"].FloatValue);
    }

    [Fact]
    public void AMaterialSkybox_KeepsItsMaterialReference()
    {
        string custom = System.Guid.NewGuid().ToString();
        EchoObject reference = EchoObject.NewCompound();
        reference["$asset"] = new EchoObject(custom);

        EchoObject scene = OldScene(3, reference);
        Release_1_0_Preview5.ConvertSceneEnvironments(scene);

        Assert.Equal(custom, scene["Environment"]["SkyMaterial"]["$asset"].StringValue);
    }

    [Fact]
    public void AmbientAndFog_MoveOver_StrengthBecomingIntensity_AndVectorsColours()
    {
        EchoObject scene = OldScene(0);

        Release_1_0_Preview5.ConvertSceneEnvironments(scene);

        EchoObject ambient = scene["Environment"]["Ambient"];
        Assert.Equal(1L, ambient["Mode"].LongValue);
        Assert.Equal(0.5f, ambient["Intensity"].FloatValue);
        Assert.Equal(0.8f, ambient["SkyColor"]["G"].FloatValue);
        Assert.Equal(0.1f, ambient["GroundColor"]["R"].FloatValue);
        Assert.False(ambient["Color"].Contains("X"));

        EchoObject fog = scene["Environment"]["Fog"];
        Assert.Equal(1L, fog["Mode"].LongValue);
        Assert.Equal(5f, fog["Start"].FloatValue);
        Assert.Equal(50f, fog["End"].FloatValue);
        Assert.Equal(0.02f, fog["Density"].FloatValue);
        Assert.True(fog["UseSky"].BoolValue);
    }

    [Fact]
    public void RunningAgain_ChangesNothing()
    {
        EchoObject scene = OldScene(2);
        Release_1_0_Preview5.ConvertSceneEnvironments(scene);
        string once = scene.WriteToString();

        Assert.Null(Release_1_0_Preview5.ConvertSceneEnvironments(scene));
        Assert.Equal(once, scene.WriteToString());
    }
}
