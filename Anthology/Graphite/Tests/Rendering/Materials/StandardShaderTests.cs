using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class SlotResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => texture != null ? 1u : 0u;

    public uint SamplerSlot(SamplerDescription? sampler) => 0u;
}

public sealed class StandardShaderTests : IDisposable
{
    private static readonly string[] s_litToggles = ["_NORMALMAP", "_SURFACEMAP", "_OCCLUSIONMAP", "_EMISSION", "_PARALLAXMAP", "_TRANSLUCENCY", "_ANISOTROPIC"];

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly MaterialSystem _system;
    private readonly Shader _lit;
    private readonly Shader _unlit;

    public StandardShaderTests()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        _system = new MaterialSystem(_device, new SlotResources());
        _lit = Load("Lit");
        _unlit = Load("Unlit");
    }

    public void Dispose()
    {
        _system.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private Shader Load(string name) => new(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{name}.shader")!), _compiler);

    private Texture MakeTexture() => _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

    private static string Keyword(Material material, string axis) => material.GetKeyword(axis);

    // Every surface alone and with each optional input, plus every input at once, for every pass
    public static IEnumerable<object[]> Variants()
    {
        foreach (string shader in new[] { "Lit", "Unlit" })
        {
            foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            {
                yield return [shader, surface, Array.Empty<string>()];
                string[] toggles = shader == "Lit" ? s_litToggles : [];
                foreach (string toggle in toggles)
                    yield return [shader, surface, new[] { toggle }];
                yield return [shader, surface, toggles];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EveryPassOfAVariantCompiles(string shader, string surface, string[] toggles)
    {
        Shader loaded = shader == "Lit" ? _lit : _unlit;
        foreach (ShaderPass pass in loaded.Definition.Passes!)
        {
            List<Keyword> combo = [new Keyword("_SURFACE", surface)];
            foreach (VariantSpace axis in _compiler.GetAxes(pass))
            {
                if (axis.IsToggle)
                    combo.Add(new Keyword(axis.Name, VariantSpace.ToggleValues[toggles.Contains(axis.Name) ? 1 : 0]));
            }
            _compiler.CompileProgram(pass, [.. combo]);
        }
    }

    [Fact]
    public void UnlitDeclaresOnlyTheSurfaceAxis()
        => Assert.Equal(["_SURFACE"], _unlit.MaterialAxes.Select(a => a.Name));

    [Fact]
    public void Surface_SetsQueueStateAndAxes()
    {
        Material material = _system.Create(_lit);
        Assert.Equal("OPAQUE", Keyword(material, "_SURFACE"));
        Assert.Null(material.StateOverride);

        material.Surface = new SurfaceSettings(SurfaceType.Transparent, SurfaceBlend.Additive, SurfaceCull.Off);
        Assert.Equal("TRANSPARENT", Keyword(material, "_SURFACE"));
        Assert.Equal(SurfaceSettings.TransparentQueue, material.RenderQueue);
        Assert.True(material.RenderQueue >= Shader.TransparentQueue);

        PassState state = material.StateOverride!;
        Assert.False(state.DepthStencil.DepthWriteEnabled);
        Assert.True(state.Blend.BlendEnabled);
        Assert.Equal(BlendFactor.One, state.Blend.DestinationColorFactor);
        Assert.Equal(FaceCullMode.None, state.Raster.CullMode);

        material.Surface = new SurfaceSettings(SurfaceType.Cutout);
        Assert.Equal("CUTOUT", Keyword(material, "_SURFACE"));
        Assert.Equal(SurfaceSettings.CutoutQueue, material.RenderQueue);
        Assert.Equal(PassStateFields.None, material.StateOverride!.Set & PassStateFields.BlendEnabled);
    }

    [Fact]
    public void Surface_IsInherited_AndExplicitSettingsWin()
    {
        Material parent = _system.Create(_lit);
        Material child = _system.Create(_lit);
        child.Parent = parent;
        parent.Surface = new SurfaceSettings(SurfaceType.Transparent);

        Assert.Equal("TRANSPARENT", Keyword(child, "_SURFACE"));
        Assert.Equal(SurfaceSettings.TransparentQueue, child.RenderQueue);

        child.RenderQueue = 1000;
        child.SetKeyword("_SURFACE", "OPAQUE");
        Assert.Equal(1000, child.RenderQueue);
        Assert.Equal("OPAQUE", Keyword(child, "_SURFACE"));

        PassState over = new() { Set = PassStateFields.CullMode };
        over.Raster.CullMode = FaceCullMode.Front;
        child.StateOverride = over;
        Assert.Equal(FaceCullMode.Front, child.StateOverride!.Raster.CullMode);
        Assert.Equal(PassStateFields.BlendEnabled, child.StateOverride.Set & PassStateFields.BlendEnabled);
    }

    [Fact]
    public void ASurfaceDecidesTransparency_WhateverTheQueue()
    {
        Material material = _system.Create(_lit);
        material.Surface = new SurfaceSettings(SurfaceType.Transparent);
        material.RenderQueue = 2000;
        _system.Extract(new List<Material>());
        Assert.True(material.Draw.Transparent);

        material.Surface = new SurfaceSettings(SurfaceType.Opaque);
        material.RenderQueue = 3000;
        _system.Extract(new List<Material>());
        Assert.False(material.Draw.Transparent);
    }

    [Fact]
    public void Properties_DriveTheirAxes_ThroughParents()
    {
        Material parent = _system.Create(_lit);
        Material child = _system.Create(_lit);
        child.Parent = parent;
        Assert.Equal("false", Keyword(child, "_NORMALMAP"));
        Assert.Equal("false", Keyword(child, "_ANISOTROPIC"));

        using Texture texture = MakeTexture();
        parent.SetTexture("_NormalTex", texture);
        parent.SetFloat("_Anisotropy", -0.5f);
        child.SetInt("_Emission", 1);
        Assert.Equal("true", Keyword(child, "_NORMALMAP"));
        Assert.Equal("true", Keyword(child, "_ANISOTROPIC"));
        Assert.Equal("true", Keyword(child, "_EMISSION"));
        Assert.Equal("false", Keyword(parent, "_EMISSION"));

        child.SetKeyword("_NORMALMAP", "false");
        Assert.Equal("false", Keyword(child, "_NORMALMAP"));

        parent.ClearOverride("_NormalTex");
        child.ClearOverride("_NORMALMAP");
        Assert.Equal("false", Keyword(child, "_NORMALMAP"));
    }

    [Fact]
    public void ChangingADrivingProperty_ChangesTheDraw()
    {
        Material material = _system.Create(_lit);
        List<Material> changed = new();
        _system.Extract(changed);
        ulong before = material.Draw.Variant;

        changed.Clear();
        material.SetFloat("_TranslucencyStrength", 0.5f);
        _system.Extract(changed);
        Assert.Contains(material, changed);
        Assert.NotEqual(before, material.Draw.Variant);

        changed.Clear();
        material.SetFloat("_Roughness", 0.25f);
        _system.Extract(changed);
        Assert.DoesNotContain(material, changed);
    }
}
