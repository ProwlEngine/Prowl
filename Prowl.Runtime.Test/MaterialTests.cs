// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class MaterialTests
{
    private static Material Standard() => new(Shader.LoadDefault(DefaultShader.Lit));

    [Fact]
    public void UnsetProperties_ReadTheShadersDefaults()
    {
        Material material = Standard();

        Assert.Equal(0f, material.GetFloat("_Metallic"));
        Assert.Equal(0.5f, material.GetFloat("_ScatteringDistortion"));
        Assert.Equal(16, material.GetInt("_ParallaxSteps"));
        Assert.Equal(Color.White, material.GetColor("_MainColor"));
        Assert.Equal(new Float4(1, 1, 0, 0), material.GetVector("_Tiling"));
        Assert.False(material.IsOverridden("_Metallic"));
        Assert.True(material.HasProperty("_Metallic"));
        Assert.False(material.HasProperty("_NotAProperty"));
    }

    [Fact]
    public void SettingAValue_OverridesIt_AndRevertingReadsTheDefaultAgain()
    {
        Material material = Standard();

        material.SetFloat("_Metallic", 0.25f);
        Assert.True(material.IsOverridden("_Metallic"));
        Assert.Equal(0.25f, material.GetFloat("_Metallic"));

        material.RevertProperty("_Metallic");
        Assert.False(material.IsOverridden("_Metallic"));
        Assert.Equal(0f, material.GetFloat("_Metallic"));
    }

    // Older materials store whole numbers as floats and the other way round
    [Fact]
    public void IntsAndFloats_ReadAsEitherKind()
    {
        Material material = Standard();
        material.SetFloat("_ParallaxSteps", 8f);
        material.SetInt("_Parallax", 2);

        Assert.Equal(8, material.GetInt("_ParallaxSteps"));
        Assert.Equal(2f, material.GetFloat("_Parallax"));
    }

    [Fact]
    public void SmallerVectors_ReadBackWidened()
    {
        Material material = Standard();
        material.SetVector("_Tiling", new Float2(3, 4));

        Assert.Equal(new Float4(3, 4, 0, 0), material.GetVector("_Tiling"));
    }

    [Fact]
    public void Keywords_ToggleAndAxisValues()
    {
        Material material = Standard();
        Assert.False(material.IsKeywordEnabled("SOME_FEATURE"));
        Assert.Equal("", material.GetKeyword("MODE"));

        material.SetKeyword("SOME_FEATURE", true);
        material.SetKeyword("MODE", "UP");
        Assert.True(material.IsKeywordEnabled("SOME_FEATURE"));
        Assert.Equal("UP", material.GetKeyword("MODE"));

        material.SetKeyword("SOME_FEATURE", false);
        Assert.False(material.IsKeywordEnabled("SOME_FEATURE"));
    }

    [Fact]
    public void Copy_StartsEqual_ThenChangesIndependently()
    {
        Material source = Standard();
        source.SetFloat("_Metallic", 0.3f);
        source.SetKeyword("SOME_FEATURE", true);
        source.RenderQueue = 2100;
        source.CastShadows = false;

        Material copy = source.Clone();
        Assert.Same(source.Shader, copy.Shader);
        Assert.Equal(0.3f, copy.GetFloat("_Metallic"));
        Assert.True(copy.IsOverridden("_Metallic"));
        Assert.True(copy.IsKeywordEnabled("SOME_FEATURE"));
        Assert.Equal(2100, copy.RenderQueue);
        Assert.False(copy.CastShadows);

        copy.SetFloat("_Metallic", 0.9f);
        copy.SetKeyword("SOME_FEATURE", false);
        Assert.Equal(0.3f, source.GetFloat("_Metallic"));
        Assert.True(source.IsKeywordEnabled("SOME_FEATURE"));
    }

    [Fact]
    public void RenderQueue_FollowsTheSurfaceThenTheShaderUntilSet()
    {
        Material opaque = Standard();
        Material transparent = new(Shader.LoadDefault(DefaultShader.Lit)) { Surface = new SurfaceSettings(SurfaceType.Transparent) };

        Assert.Equal(Shader.LoadDefault(DefaultShader.Lit).RenderQueue, opaque.RenderQueue);
        Assert.True(transparent.RenderQueue >= 2500);

        opaque.RenderQueue = 2600;
        Assert.Equal(2600, opaque.RenderQueue);
    }

    [Fact]
    public void MaterialWithoutAShader_UsesLit()
    {
        Assert.Same(Shader.LoadDefault(DefaultShader.Lit), new Material().Shader);
    }

    [Fact]
    public void SaveAndLoad_KeepsShaderValuesOverridesAndKeywords()
    {
        Material source = new(Shader.LoadDefault(DefaultShader.Unlit));
        source.SetFloat("_Metallic", 0.4f);
        source.SetColor("_MainColor", new Color(0.1f, 0.2f, 0.3f, 1f));
        source.SetKeyword("SOME_FEATURE", true);
        source.SetKeyword("MODE", "COPY");
        source.RenderQueue = 2200;
        source.CastShadows = false;
        source.Surface = new SurfaceSettings(SurfaceType.Cutout, SurfaceBlend.Additive, SurfaceCull.Off);

        Material loaded = Serializer.Deserialize<Material>(Serializer.Serialize(source))!;

        Assert.Same(Shader.LoadDefault(DefaultShader.Unlit), loaded.Shader);
        Assert.Equal(0.4f, loaded.GetFloat("_Metallic"));
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 1f), loaded.GetColor("_MainColor"));
        Assert.True(loaded.IsKeywordEnabled("SOME_FEATURE"));
        Assert.Equal("COPY", loaded.GetKeyword("MODE"));
        Assert.Equal(2200, loaded.RenderQueue);
        Assert.False(loaded.CastShadows);
        Assert.Equal(new SurfaceSettings(SurfaceType.Cutout, SurfaceBlend.Additive, SurfaceCull.Off), loaded.Surface);
        Assert.Equal(source._overrides.OrderBy(n => n), loaded._overrides.OrderBy(n => n));
    }

    // Materials saved before overrides were tracked count every stored value as set
    [Fact]
    public void LoadingAMaterialWithoutOverrides_TreatsEveryStoredValueAsSet()
    {
        Material material = Standard();
        material._properties.SetFloat("_Roughness", 0.2f);
        material._overrides.Clear();

        material.OnAfterDeserialize();

        Assert.True(material.IsOverridden("_Roughness"));
        Assert.Equal(0.2f, material.GetFloat("_Roughness"));
    }

    [Fact]
    public void ShaderKeywordAxes_AreListedFromItsVariantPragmas()
    {
        Shader terrain = Shader.LoadDefault(DefaultShader.Terrain);

        Assert.Contains(terrain.Keywords, axis => axis.Name == "TERRAIN_8_LAYERS");
    }

    [Fact]
    public void AVariant_ReadsWhatItDoesNotSetFromItsParent()
    {
        Material parent = Standard();
        parent.SetFloat("_Roughness", 0.3f);
        parent.Surface = new SurfaceSettings(SurfaceType.Transparent);
        Material variant = new() { Parent = parent };
        variant.SetColor("_MainColor", new Color(1f, 0f, 0f, 1f));

        Assert.Same(parent.Shader, variant.Shader);
        Assert.Equal(0.3f, variant.GetFloat("_Roughness"));
        Assert.Equal(new Color(1f, 0f, 0f, 1f), variant.GetColor("_MainColor"));
        Assert.Equal(SurfaceType.Transparent, variant.Surface!.Value.Type);
        Assert.False(variant.IsSurfaceOverridden);

        parent.SetFloat("_Roughness", 0.8f);
        Assert.Equal(0.8f, variant.GetFloat("_Roughness"));
        Assert.Throws<System.InvalidOperationException>(() => parent.Parent = variant);
    }

    [Theory]
    [InlineData("StandardCutoutDoubleSided", DefaultShader.Lit, SurfaceType.Cutout, SurfaceCull.Off)]
    [InlineData("StandardTransparent", DefaultShader.Lit, SurfaceType.Transparent, SurfaceCull.Back)]
    [InlineData("UnlitDoubleSided", DefaultShader.Unlit, SurfaceType.Opaque, SurfaceCull.Off)]
    public void AMaterialOfAShaderFromBeforeGraphite_LoadsOntoLitOrUnlit(string oldShader, DefaultShader expected, SurfaceType type, SurfaceCull cull)
    {
        EchoObject saved = Serializer.Serialize(Standard());
        saved["_shader"]["$asset"] = new EchoObject(BuiltInAssets.DeterministicGuid($"$Default:Shader/{oldShader}").ToString());

        Material loaded = Serializer.Deserialize<Material>(saved)!;

        Assert.Same(Shader.LoadDefault(expected), loaded.Shader);
        Assert.Equal(new SurfaceSettings(type, SurfaceBlend.Alpha, cull), loaded.Surface);
    }

    [Fact]
    public void AMissingShader_FallsBackToInvalid()
    {
        EchoObject saved = Serializer.Serialize(Standard());
        saved["_shader"]["$asset"] = new EchoObject(System.Guid.NewGuid().ToString());

        Material loaded = Serializer.Deserialize<Material>(saved)!;

        Assert.Same(Shader.LoadDefault(DefaultShader.Invalid), loaded.Shader);
    }
}
