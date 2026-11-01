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

        Assert.Equal(0f, material.GetFloat("base_metalness"));
        Assert.Equal(1.5f, material.GetFloat("specular_ior"));
        Assert.Equal(0, material.GetInt("geometry_thin_walled"));
        Assert.Equal(new Color(0.8f, 0.8f, 0.8f, 1f), material.GetColor("base_color"));
        Assert.Equal(new Float4(1, 1, 0, 0), material.GetTextureScaleOffset("base_color_map"));
        Assert.False(material.IsOverridden("base_metalness"));
        Assert.True(material.HasProperty("base_metalness"));
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
        material.SetVector("_Scroll", new Float2(3, 4));

        Assert.Equal(new Float4(3, 4, 0, 0), material.GetVector("_Scroll"));
    }

    [Fact]
    public void TextureScaleOffsets_AreSetPerTexture_AndRevert()
    {
        Material material = Standard();
        material.SetTextureScaleOffset("_NormalTex", new Float4(2, 3, 0.5f, 0.25f));

        Assert.Equal(new Float4(2, 3, 0.5f, 0.25f), material.GetTextureScaleOffset("_NormalTex"));
        Assert.Equal(new Float4(1, 1, 0, 0), material.GetTextureScaleOffset("_MainTex"));

        material.RevertProperty("_NormalTex_ST");
        Assert.Equal(new Float4(1, 1, 0, 0), material.GetTextureScaleOffset("_NormalTex"));
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
        source.SetColor("base_color", new Color(0.1f, 0.2f, 0.3f, 1f));
        source.SetFloat("emission_luminance", 2f);
        source.SetKeyword("SOME_FEATURE", true);
        source.SetKeyword("MODE", "COPY");
        source.RenderQueue = 2200;
        source.CastShadows = false;
        source.Surface = new SurfaceSettings(SurfaceType.Cutout, SurfaceBlend.Additive, SurfaceCull.Off);

        Material loaded = Serializer.Deserialize<Material>(Serializer.Serialize(source))!;

        Assert.Same(Shader.LoadDefault(DefaultShader.Unlit), loaded.Shader);
        Assert.Equal(0.4f, loaded.GetFloat("_Metallic"));
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 1f), loaded.GetColor("base_color"));
        Assert.True(loaded.IsKeywordEnabled("SOME_FEATURE"));
        Assert.Equal("COPY", loaded.GetKeyword("MODE"));
        Assert.Equal(2200, loaded.RenderQueue);
        Assert.False(loaded.CastShadows);
        Assert.Equal(new SurfaceSettings(SurfaceType.Cutout, SurfaceBlend.Additive, SurfaceCull.Off), loaded.Surface);
        Assert.Equal(source._overrides.OrderBy(n => n), loaded._overrides.OrderBy(n => n));
    }

    // Unlit colour became light in nits: an old Unlit material glows at the luminance that keeps its look, a newer one keeps its own
    [Fact]
    public void LoadingAnUnlitMaterial_WithoutALuminance_TakesTheLegacyOne()
    {
        Material old = new(Shader.LoadDefault(DefaultShader.Unlit));
        old.SetColor("base_color", new Color(0.1f, 0.2f, 0.3f, 1f));
        Material loaded = Serializer.Deserialize<Material>(Serializer.Serialize(old))!;
        Assert.Equal(Prowl.Runtime.Rendering.LegacyLightUnits.Luminance, loaded.GetFloat("emission_luminance"));

        Material set = new(Shader.LoadDefault(DefaultShader.Unlit));
        set.SetFloat("emission_luminance", 500f);
        Assert.Equal(500f, Serializer.Deserialize<Material>(Serializer.Serialize(set))!.GetFloat("emission_luminance"));
    }

    // Materials saved before overrides were tracked count every stored value as set
    [Fact]
    public void LoadingAMaterialWithoutOverrides_TreatsEveryStoredValueAsSet()
    {
        Material material = Standard();
        material._properties.SetFloat("specular_roughness", 0.2f);
        material._overrides.Clear();

        material.OnAfterDeserialize();

        Assert.True(material.IsOverridden("specular_roughness"));
        Assert.Equal(0.2f, material.GetFloat("specular_roughness"));
    }

    [Fact]
    public void LoadingALitMaterialFromBeforeOpenPbr_MovesItsValuesOntoTheNewNames()
    {
        Material material = Standard();
        material.Surface = new SurfaceSettings(SurfaceType.Cutout);
        material._properties.SetColor("_MainColor", new Color(0.5f, 0.4f, 0.3f, 0.6f));
        material._properties.SetFloat("_Roughness", 0.2f);
        material._properties.SetFloat("_Metallic", 0.7f);
        material._properties.SetInt("_Emission", 1);
        material._properties.SetFloat("_EmissionIntensity", 3f);
        material._overrides.Clear();

        material.OnAfterDeserialize();

        Assert.Equal(new Color(0.5f, 0.4f, 0.3f, 0.6f), material.GetColor("base_color"));
        Assert.Equal(0.2f, material.GetFloat("specular_roughness"));
        Assert.Equal(0.7f, material.GetFloat("base_metalness"));
        Assert.Equal(0.6f, material.GetFloat("geometry_opacity"));
        Assert.Equal(3f * Prowl.Runtime.Rendering.LegacyLightUnits.Luminance, material.GetFloat("emission_luminance"));
        Assert.False(material.IsOverridden("_Roughness"));
        Assert.False(material.IsOverridden("_Emission"));
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
