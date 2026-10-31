using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class SlotResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => texture != null ? 1u : 0u;

    public uint SamplerSlot(SamplerDescription? sampler) => 0u;
}

[Collection("GPU Tests")]
public sealed class StandardShaderTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly MaterialSystem _system;
    private readonly Shader _pbr;
    private readonly Shader _unlit;

    public StandardShaderTests()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        _system = new MaterialSystem(_device, _store, new SlotResources());
        _pbr = Load("Lit");
        _unlit = Load("Unlit");
    }

    public void Dispose()
    {
        _store.Dispose();
        _device.Dispose();
    }

    private Shader Load(string name) => new(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{name}.shader")!), _compiler);

    private Texture MakeTexture() => _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

    private static string Keyword(Material material, string axis) => material.Get(material.Shader.Axis(axis));

    // Every surface at both tiers, the Debug pass at every view it shows
    public static IEnumerable<object[]> Variants()
    {
        foreach (string shader in new[] { "Lit", "Unlit" })
        {
            foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            {
                foreach (string tier in new[] { "FULL", "LITE" })
                    yield return [shader, surface, tier];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EveryPassOfAVariantCompiles(string shader, string surface, string tier)
    {
        Shader loaded = shader == "Lit" ? _pbr : _unlit;
        List<(ShaderPass, Keyword[])> variants = [];
        foreach (ShaderPass pass in loaded.Definition.Passes!)
        {
            IReadOnlyList<string> views = _compiler.GetAxes(pass).Where(a => a.Name == GlobalAxis.Debug.Name).Select(a => a.Values).FirstOrDefault() ?? [""];
            foreach (string view in views)
            {
                List<Keyword> combo = [new Keyword("_SURFACE", surface), new Keyword("PBR_TIER", tier)];
                if (view.Length > 0)
                    combo.Add(new Keyword(GlobalAxis.Debug.Name, view));
                variants.Add((pass, [.. combo]));
            }
        }
        CompileAll(variants);
    }

    // Compiles variants on every core, failing with each variant that did not compile
    private void CompileAll(List<(ShaderPass Pass, Keyword[] Combo)> variants)
    {
        System.Collections.Concurrent.ConcurrentBag<string> failures = [];
        Parallel.ForEach(variants, variant =>
        {
            try
            {
                _compiler.CompileProgram(variant.Pass, variant.Combo);
            }
            catch (Exception e)
            {
                failures.Add($"{variant.Pass.Name} [{string.Join(", ", variant.Combo.Select(k => $"{k.Name}={k.Value}"))}]: {e.Message}");
            }
        });
        Assert.Empty(failures);
    }

    private const string RimSource = """
        Shader "Tests/Rim"
        {
            Properties
            {
                _RimColor("Rim", Color) = (1.0, 0.0, 0.0, 1.0)
                base_metalness("Metal", Float) = 0.5
            }

            Surface "Lit"
            {
                GLSLPROGRAM
                uniform vec4 _RimColor;
                void Surface(inout PBR_Params p, Graphite_SurfaceData i)
                {
                    p.emissionColor = _RimColor.rgb;
                    p.emissionLuminance = 100.0 * pow(1.0 - max(dot(p.geometryNormal, i.viewDir), 0.0), 3.0);
                    p.geometryOpacity *= i.vertexColor.a;
                }
                ENDGLSL
            }
        }
        """;

    private static readonly string[] s_pbrPasses = ["Forward", "Transmission", "BackDepth", "Prepass", "ShadowCaster", "Debug"];

    [Theory]
    [InlineData("OPAQUE")]
    [InlineData("CUTOUT")]
    [InlineData("TRANSPARENT")]
    public void SurfaceShaders_TakeTheStandardPassesAndProperties_AndCompile(string surface)
    {
        Shader rim = new(ShaderParser.Parse(RimSource), _compiler);
        ShaderDefinition definition = rim.Definition;
        Assert.Equal(s_pbrPasses, definition.Passes!.Select(p => p.Name));
        string[] names = [.. definition.Properties!.Select(p => p.Name)];
        Assert.Contains("base_color_map", names);
        Assert.Contains("_RimColor", names);
        Assert.Equal(0.5f, definition.Properties!.Single(p => p.Name == "base_metalness").Value.X);
        Assert.Equal(names.Length, names.Distinct().Count());

        foreach (ShaderPass pass in definition.Passes!)
            _compiler.CompileProgram(pass, [new Keyword("_SURFACE", surface)]);
    }

    private const string ToonSource = """
        Shader "Tests/Toon"
        {
            Surface "Lit"
            {
                GLSLPROGRAM
                void Surface(inout PBR_Params p, Graphite_SurfaceData i) { p.specularRoughness = 0.5; }
                ENDGLSL

                Lighting
                {
                    GLSLPROGRAM
                    vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)
                    {
                        float ramp = step(0.5, dot(c.normal, l.diffuseL) * 0.5 + 0.5);
                        return c.diffuseAlbedo * ramp * l.diffuse * l.radiance;
                    }
                    ENDGLSL
                }
            }
        }
        """;

    [Theory]
    [InlineData("OPAQUE")]
    [InlineData("CUTOUT")]
    [InlineData("TRANSPARENT")]
    public void SurfaceLighting_ReplacesTheBRDF_AndEveryFeatureStillCompiles(string surface)
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse(ToonSource), _compiler).Definition;
        Assert.Equal(s_pbrPasses, definition.Passes!.Select(p => p.Name));

        foreach (ShaderPass pass in definition.Passes!)
        {
            bool lit = pass.Name is "Forward" or "Debug" or "Transmission";
            Assert.Equal(lit, pass.Source.Contains("#define GRAPHITE_CUSTOM_BRDF"));
            Assert.Equal(lit, pass.Source.Contains("float ramp"));

            List<Keyword> combo = [new Keyword("_SURFACE", surface)];
            if (pass.Name == "Debug")
                combo.Add(new Keyword(GlobalAxis.Debug.Name, "DIFFUSE"));
            _compiler.CompileProgram(pass, [.. combo]);
        }
    }

    private static string WaveSource(string model) => $$"""
        Shader "Tests/Wave"
        {
            Properties
            {
                _WaveHeight("Wave Height", Float) = 0.2
            }

            Surface "{{model}}"
            {
                Vertex
                {
                    GLSLPROGRAM
                    uniform float _WaveHeight;
                    void SurfaceVertex(inout Graphite_SurfaceVertex v)
                    {
                        v.position += v.normal * sin(v.position.x + v.time) * _WaveHeight * v.color.a;
                        v.uv += v.uv1 * 0.0;
                        v.tangent.xyz = normalize(v.tangent.xyz);
                    }
                    ENDGLSL
                }
            }
        }
        """;

    [Theory]
    [InlineData("Lit", "OPAQUE")]
    [InlineData("Lit", "CUTOUT")]
    [InlineData("Lit", "TRANSPARENT")]
    [InlineData("Unlit", "OPAQUE")]
    [InlineData("Unlit", "CUTOUT")]
    [InlineData("Unlit", "TRANSPARENT")]
    public void SurfaceVertex_RunsInEveryPass_AndCompiles(string model, string surface)
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse(WaveSource(model)), _compiler).Definition;
        Assert.Equal(model == "Lit" ? s_pbrPasses : ["Forward", "Prepass", "ShadowCaster", "Debug"], definition.Passes!.Select(p => p.Name));

        foreach (ShaderPass pass in definition.Passes!)
        {
            Assert.Contains("void SurfaceVertex(inout Graphite_SurfaceVertex v)", pass.Source);
            _compiler.CompileProgram(pass, [new Keyword("_SURFACE", surface)]);
        }
    }

    private static string DetailSource(string model) => $$"""
        Shader "Tests/Detail"
        {
            Properties
            {
                _DetailTex("Detail Normal", Texture2D) = "normal" {}
            }

            Surface "{{model}}"
            {
                GLSLPROGRAM
                uniform sampler2D _DetailTex;
                void Surface(inout {{(model == "Lit" ? "PBR_Params" : "Unlit_Params")}} p, Graphite_SurfaceData i)
                {
                    vec3 detail = Graphite_SurfaceNormalMap(i, texture(_DetailTex, i.uv * 4.0).rgb, 1.0);
                    vec3 frame = cross(i.tangent, i.bitangent);
                    {{(model == "Lit" ? "p.geometryNormal = normalize(p.geometryNormal + detail + frame * 0.0);" : "p.baseColor *= detail * 0.5 + 0.5 + frame * 0.0;")}}
                }
                ENDGLSL
            }
        }
        """;

    [Theory]
    [InlineData("Lit", "OPAQUE")]
    [InlineData("Lit", "CUTOUT")]
    [InlineData("Unlit", "OPAQUE")]
    [InlineData("Unlit", "CUTOUT")]
    public void SurfaceData_HasTheTangentFrame_AndANormalMapHelper(string model, string surface)
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse(DetailSource(model)), _compiler).Definition;
        foreach (ShaderPass pass in definition.Passes!)
            _compiler.CompileProgram(pass, [new Keyword("_SURFACE", surface)]);
    }

    [Fact]
    public void ASurfaceShaderNamingNoFallback_TakesTheModels()
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse("""
            Shader "Tests/NoFallback"
            {
                Surface "Lit"
                {
                    GLSLPROGRAM
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { }
                    ENDGLSL
                }
            }
            """), _compiler).Definition;
        Assert.Equal(Renderer.FallbackShaderName, definition.Fallback);
    }

    [Fact]
    public void SurfaceVertex_TakesSurfaceAndLightingBlocksAlongside()
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse("""
            Shader "Tests/All"
            {
                Surface "Lit"
                {
                    GLSLPROGRAM
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { p.specularRoughness = 0.5; }
                    ENDGLSL
                    Vertex
                    {
                        GLSLPROGRAM
                        void SurfaceVertex(inout Graphite_SurfaceVertex v) { v.position.y += 1.0; }
                        ENDGLSL
                    }
                    Lighting
                    {
                        GLSLPROGRAM
                        vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l) { return PBR_EvaluateLight(c, l); }
                        ENDGLSL
                    }
                }
            }
            """), _compiler).Definition;

        ShaderPass forward = definition.Passes!.Single(p => p.Name == "Forward");
        _compiler.CompileProgram(forward, [new Keyword("_SURFACE", "OPAQUE")]);
    }

    [Fact]
    public void SurfaceLighting_SharesUniformsTheSurfaceCodeDeclares()
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse("""
            Shader "Tests/Bands"
            {
                Properties
                {
                    _Bands("Bands", Float) = 3.0
                    _Ramp("Ramp", Texture2D) = "white" {}
                }

                Surface "Lit"
                {
                    GLSLPROGRAM
                    uniform float _Bands;
                    uniform sampler2D _Ramp;
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { p.specularRoughness = texture(_Ramp, i.uv).r / _Bands; }
                    ENDGLSL
                    Lighting
                    {
                        GLSLPROGRAM
                        uniform float _Bands;
                        uniform sampler2D _Ramp;
                        vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)
                        {
                            return floor(PBR_EvaluateLight(c, l) * _Bands) / _Bands * texture(_Ramp, vec2(0.5)).rgb;
                        }
                        vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i)
                        {
                            return floor(i.irradiance * _Bands) / _Bands * c.diffuseAlbedo + c.specularAlbedo * i.specular;
                        }
                        ENDGLSL
                    }
                }
            }
            """), _compiler).Definition;

        foreach (ShaderPass pass in definition.Passes!)
        {
            List<Keyword> combo = [new Keyword("_SURFACE", "OPAQUE")];
            if (pass.Name == "Debug")
                combo.Add(new Keyword(GlobalAxis.Debug.Name, "LIGHTING"));
            _compiler.CompileProgram(pass, [.. combo]);
        }
    }

    [Fact]
    public void SurfaceLighting_DefinesOnlyTheHooksItsCodeHas()
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse("""
            Shader "Tests/AmbientOnly"
            {
                Surface "Lit"
                {
                    Lighting
                    {
                        GLSLPROGRAM
                        vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i) { return i.irradiance * c.diffuseAlbedo; }
                        ENDGLSL
                    }
                }
            }
            """), _compiler).Definition;

        ShaderPass forward = definition.Passes!.Single(p => p.Name == "Forward");
        Assert.Contains("#define GRAPHITE_CUSTOM_AMBIENT_BRDF", forward.Source);
        Assert.DoesNotContain("#define GRAPHITE_CUSTOM_BRDF", forward.Source);
        _compiler.CompileProgram(forward, [new Keyword("_SURFACE", "OPAQUE")]);
    }

    [Fact]
    public void SurfaceLighting_OnAModelWithoutLights_FailsToLoad()
    {
        ShaderLoadException error = Assert.Throws<ShaderLoadException>(() => new Shader(ShaderParser.Parse("""
            Shader "Tests/UnlitToon"
            {
                Surface "Unlit"
                {
                    Lighting
                    {
                        GLSLPROGRAM
                        vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l) { return vec3(0.0); }
                        ENDGLSL
                    }
                }
            }
            """), _compiler));
        Assert.Contains("surface_lighting", error.Message);
    }

    [Fact]
    public void AnUnknownSurfaceModel_FailsToLoad()
    {
        ShaderLoadException error = Assert.Throws<ShaderLoadException>(() => new Shader(ShaderParser.Parse("""
            Shader "Tests/Unknown"
            {
                Surface "Toon" { }
            }
            """), _compiler));
        Assert.Contains("Toon", error.Message);
    }

    private static readonly string[] s_lobes = ["PBR_COAT", "PBR_FUZZ", "PBR_THIN_FILM", "PBR_ANISOTROPY", "PBR_SUBSURFACE", "PBR_THIN_WALLED"];

    // The axes deciding how much light a caster lets through to its shadow
    private static readonly string[] s_shadowAxes = ["PBR_SUBSURFACE", "PBR_THIN_WALLED", "PBR_TRANSMISSION"];

    [Fact]
    public void Unlit_DeclaresOnlyTheSurfaceAxis_AndPBRItsLobesBeside_MapsBeingBitsOfTheMaterial()
    {
        Assert.Equal(["_SURFACE"], _unlit.MaterialAxes.Select(a => a.Name));
        Assert.Equal([.. s_lobes.Append("_SURFACE").Append("PBR_TRANSMISSION").Append("PBR_PARALLAX").Order(StringComparer.Ordinal)], _pbr.MaterialAxes.Select(a => a.Name).Order(StringComparer.Ordinal));
        foreach (ShaderPass pass in _pbr.Definition.Passes!)
        {
            bool lit = pass.Name is "Forward" or "Debug" or "Transmission";
            bool shadow = pass.Name == "ShadowCaster";
            foreach (string lobe in s_lobes)
                Assert.Equal(lit || (shadow && s_shadowAxes.Contains(lobe)), _compiler.GetAxes(pass).Any(a => a.Name == lobe));
            Assert.Equal(pass.Name == "Debug" || shadow, _compiler.GetAxes(pass).Any(a => a.Name == "PBR_TRANSMISSION"));
            Assert.Contains(_compiler.GetAxes(pass), a => a.Name == "PBR_PARALLAX");
        }
        Assert.Contains(_pbr.GlobalAxes, a => a.Name == "PBR_TIER" && a.Values.SequenceEqual(["FULL", "LITE"]));
        Assert.DoesNotContain(_unlit.GlobalAxes, a => a.Name == "PBR_TIER");
    }

    // Each lobe turns on with its weight, anisotropy with either roughness anisotropy, and off again at zero
    [Fact]
    public void LobeAxes_FollowTheirWeights()
    {
        Material material = _system.Create(_pbr);
        foreach (string lobe in s_lobes)
            Assert.Equal("false", Keyword(material, lobe));

        foreach ((string property, string lobe) in new[] { ("coat_weight", "PBR_COAT"), ("fuzz_weight", "PBR_FUZZ"), ("thin_film_weight", "PBR_THIN_FILM"), ("specular_roughness_anisotropy", "PBR_ANISOTROPY"), ("coat_roughness_anisotropy", "PBR_ANISOTROPY"), ("subsurface_weight", "PBR_SUBSURFACE") })
        {
            material.SetFloat(property, 0.4f);
            Assert.Equal("true", Keyword(material, lobe));
            material.SetFloat(property, 0);
            Assert.Equal("false", Keyword(material, lobe));
        }

        material.SetFloat("specular_roughness_anisotropy", 0.5f);
        material.SetFloat("coat_roughness_anisotropy", 0);
        Assert.Equal("true", Keyword(material, "PBR_ANISOTROPY"));
        material.SetFloat("specular_roughness_anisotropy", 0);
        material.SetFloat("coat_roughness_anisotropy", 0.5f);
        Assert.Equal("true", Keyword(material, "PBR_ANISOTROPY"));

        material.SetInt("geometry_thin_walled", 1);
        Assert.Equal("true", Keyword(material, "PBR_THIN_WALLED"));
        material.SetInt("geometry_thin_walled", 0);
        Assert.Equal("false", Keyword(material, "PBR_THIN_WALLED"));
    }

    // Thin walled draws both sides whatever the surface culls, from geometry_thin_walled or the axis set directly
    [Fact]
    public void ThinWalled_DrawsBothSides()
    {
        Assert.Equal("PBR_THIN_WALLED", _pbr.TwoSidedAxis);
        Assert.Null(_unlit.TwoSidedAxis);

        Material material = _system.Create(_pbr);
        material.Surface = new SurfaceSettings(SurfaceType.Transparent, SurfaceBlend.Premultiplied, SurfaceCull.Back);
        FaceCullMode Culling()
        {
            _system.Extract(new List<Material>());
            return material.Draw.StateOverride!.Raster.CullMode;
        }

        Assert.Equal(FaceCullMode.Back, Culling());
        material.SetInt("geometry_thin_walled", 1);
        Assert.Equal(FaceCullMode.None, Culling());
        Assert.True(material.Draw.StateOverride!.Blend.BlendEnabled);
        Assert.Equal(FaceCullMode.Back, material.StateOverride!.Raster.CullMode);
        material.SetInt("geometry_thin_walled", 0);
        Assert.Equal(FaceCullMode.Back, Culling());

        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Front);
        material.Set(material.Shader.Axis("PBR_THIN_WALLED"), true);
        Assert.Equal(FaceCullMode.None, Culling());

        Material plain = _system.Create(_pbr);
        plain.SetInt("geometry_thin_walled", 1);
        _system.Extract(new List<Material>());
        Assert.Equal(FaceCullMode.None, plain.Draw.StateOverride!.Raster.CullMode);
    }

    // Every combination of lobes, subsurface and thin walled at both tiers, the forward pass and the debug pass at its lobe views
    [Fact]
    public void EveryLobeCombination_Compiles()
    {
        ShaderPass forward = _pbr.Definition.Passes!.First(p => p.Name == "Forward");
        ShaderPass transmission = _pbr.Definition.Passes!.First(p => p.Name == "Transmission");
        ShaderPass debug = _pbr.Definition.Passes!.First(p => p.Name == "Debug");
        List<(ShaderPass, Keyword[])> variants = [];
        foreach (string tier in new[] { "FULL", "LITE" })
        {
            for (int mask = 0; mask < 1 << s_lobes.Length; mask++)
            {
                List<Keyword> combo = [new Keyword("_SURFACE", "OPAQUE"), new Keyword("PBR_TIER", tier)];
                for (int i = 0; i < s_lobes.Length; i++)
                    combo.Add(new Keyword(s_lobes[i], (mask & (1 << i)) != 0 ? "true" : "false"));
                variants.Add((forward, [.. combo]));
                variants.Add((transmission, [.. combo]));
                if (mask == 0 || mask == (1 << s_lobes.Length) - 1)
                {
                    foreach (string transmits in new[] { "false", "true" })
                    {
                        foreach (string view in new[] { "DIFFUSE", "SPECULAR", "FURNACE", "COAT", "FUZZ", "THIN_FILM", "DARKENING", "SUBSURFACE", "BACK", "TRANSMISSION", "THICKNESS" })
                            variants.Add((debug, [.. combo, new Keyword("PBR_TRANSMISSION", transmits), new Keyword(GlobalAxis.Debug.Name, view)]));
                    }
                }
            }
        }
        ShaderPass backDepth = _pbr.Definition.Passes!.First(p => p.Name == "BackDepth");
        foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            variants.Add((backDepth, [new Keyword("_SURFACE", surface)]));
        CompileAll(variants);
    }

    // Parallax is off until height_scale leaves zero, a height map alone turning nothing on, and off again at zero
    [Fact]
    public void Parallax_FollowsHeightScale_OffByDefault()
    {
        Material material = _system.Create(_pbr);
        Assert.Equal("false", Keyword(material, "PBR_PARALLAX"));
        Assert.Equal(0f, _pbr.Properties[_pbr.FindProperty("height_scale")].Default.Vector[0]);

        using Texture texture = MakeTexture();
        material.SetTexture("geometry_height_map", texture);
        Assert.Equal("false", Keyword(material, "PBR_PARALLAX"));
        material.SetFloat("height_scale", 0.05f);
        Assert.Equal("true", Keyword(material, "PBR_PARALLAX"));
        material.SetFloat("height_scale", 0);
        Assert.Equal("false", Keyword(material, "PBR_PARALLAX"));
    }

    // Every pass with parallax on, at every surface and both tiers, with the lobes, the shadow axes and the Debug pass's views beside
    [Fact]
    public void EveryParallaxVariant_Compiles()
    {
        List<(ShaderPass, Keyword[])> variants = [];
        foreach (ShaderPass pass in _pbr.Definition.Passes!)
        {
            foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            {
                foreach (string tier in new[] { "FULL", "LITE" })
                {
                    foreach (bool all in new[] { false, true })
                    {
                        List<Keyword> combo = [new Keyword("_SURFACE", surface), new Keyword("PBR_TIER", tier), new Keyword("PBR_PARALLAX", "true")];
                        foreach (string axis in s_lobes.Append("PBR_TRANSMISSION"))
                            combo.Add(new Keyword(axis, all ? "true" : "false"));
                        if (pass.Name == "Debug")
                        {
                            foreach (string view in new[] { "ALBEDO", "NORMALS", "LIGHTING" })
                                variants.Add((pass, [.. combo, new Keyword(GlobalAxis.Debug.Name, view)]));
                        }
                        else
                            variants.Add((pass, [.. combo]));
                    }
                }
            }
        }
        CompileAll(variants);
    }

    // The shadow caster at every surface and every combination of the axes deciding what it lets through
    [Fact]
    public void EveryShadowCasterVariant_Compiles()
    {
        ShaderPass caster = _pbr.Definition.Passes!.First(p => p.Name == "ShadowCaster");
        List<(ShaderPass, Keyword[])> variants = [];
        foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
        {
            for (int mask = 0; mask < 1 << s_shadowAxes.Length; mask++)
            {
                List<Keyword> combo = [new Keyword("_SURFACE", surface)];
                for (int i = 0; i < s_shadowAxes.Length; i++)
                    combo.Add(new Keyword(s_shadowAxes[i], (mask & (1 << i)) != 0 ? "true" : "false"));
                variants.Add((caster, [.. combo]));
            }
        }
        CompileAll(variants);
    }

    // transmission_weight turns PBR_TRANSMISSION on, which draws the material as transparent through its Transmission
    // pass, blending premultiplied whatever its surface, and volumes, not thin walled, through their BackDepth pass too
    [Fact]
    public void Transmission_DrawsTransparentThroughItsOwnPasses()
    {
        Assert.Equal("PBR_TRANSMISSION", _pbr.TransmissiveAxis);
        Assert.Null(_unlit.TransmissiveAxis);
        int transmissionPass = _pbr.PassIndex(PassType.Transmission);
        Assert.True(_pbr.PassReadsSceneColor(transmissionPass) && _pbr.PassReadsSceneDepth(transmissionPass) && _pbr.PassReadsBackDepth(transmissionPass));
        Assert.False(_pbr.PassReadsSceneColor(_pbr.PassIndex(PassType.Forward)));
        Assert.True(_pbr.PassReadsIn(transmissionPass, new Dictionary<string, string>()));
        Assert.True(_pbr.PassReadsIn(transmissionPass, new Dictionary<string, string> { ["PBR_TIER"] = "FULL" }));
        Assert.False(_pbr.PassReadsIn(transmissionPass, new Dictionary<string, string> { ["PBR_TIER"] = "LITE" }));

        Material material = _system.Create(_pbr);
        void Extract() => _system.Extract(new List<Material>());
        Extract();
        Assert.False(material.Draw.Transparent || material.Draw.Transmissive);

        material.SetFloat("transmission_weight", 0.7f);
        Assert.Equal("true", Keyword(material, "PBR_TRANSMISSION"));
        Extract();
        MaterialDraw draw = material.Draw;
        Assert.True(draw.Transparent && draw.Transmissive && draw.Volume);
        Assert.True(draw.RenderQueue >= SurfaceSettings.TransparentQueue);
        Assert.True(draw.StateOverride!.Blend.BlendEnabled);
        Assert.False(draw.StateOverride.DepthStencil.DepthWriteEnabled);
        Assert.Equal((BlendFactor.One, BlendFactor.InverseSourceAlpha), (draw.StateOverride.Blend.SourceColorFactor, draw.StateOverride.Blend.DestinationColorFactor));
        Assert.Equal(FaceCullMode.Back, draw.StateOverride.Raster.CullMode);

        material.SetInt("geometry_thin_walled", 1);
        Extract();
        Assert.True(material.Draw.Transmissive);
        Assert.False(material.Draw.Volume);
        Assert.Equal(FaceCullMode.None, material.Draw.StateOverride!.Raster.CullMode);

        material.Surface = new SurfaceSettings(SurfaceType.Transparent, SurfaceBlend.Additive);
        Extract();
        Assert.Equal((BlendFactor.One, BlendFactor.One), (material.Draw.StateOverride!.Blend.SourceColorFactor, material.Draw.StateOverride.Blend.DestinationColorFactor));

        material.SetFloat("transmission_weight", 0);
        material.Surface = null;
        Extract();
        Assert.False(material.Draw.Transparent || material.Draw.Transmissive);
    }

    [Fact]
    public void Surface_SetsQueueStateAndAxes()
    {
        Material material = _system.Create(_pbr);
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
    public void PBRCoverage_IsPremultiplied_SoEveryBlendAppliesItOnce_WhileUnlitStaysStraight()
    {
        Assert.True(_pbr.PremultipliedOutput);
        Assert.False(_unlit.PremultipliedOutput);

        Material pbr = _system.Create(_pbr);
        Material unlit = _system.Create(_unlit);
        foreach ((SurfaceBlend blend, BlendFactor source, BlendFactor destination, BlendFactor straight) in new[]
        {
            (SurfaceBlend.Alpha, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFactor.SourceAlpha),
            (SurfaceBlend.Premultiplied, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFactor.One),
            (SurfaceBlend.Additive, BlendFactor.One, BlendFactor.One, BlendFactor.SourceAlpha),
        })
        {
            pbr.Surface = unlit.Surface = new SurfaceSettings(SurfaceType.Transparent, blend);
            Assert.Equal(source, pbr.StateOverride!.Blend.SourceColorFactor);
            Assert.Equal(destination, pbr.StateOverride!.Blend.DestinationColorFactor);
            Assert.Equal(straight, unlit.StateOverride!.Blend.SourceColorFactor);
        }
    }

    [Fact]
    public void Surface_IsInherited_AndExplicitSettingsWin()
    {
        Material parent = _system.Create(_pbr);
        Material child = _system.Create(_pbr);
        child.Parent = parent;
        parent.Surface = new SurfaceSettings(SurfaceType.Transparent);

        Assert.Equal("TRANSPARENT", Keyword(child, "_SURFACE"));
        Assert.Equal(SurfaceSettings.TransparentQueue, child.RenderQueue);

        child.RenderQueue = 1000;
        child.Set(child.Shader.Axis("_SURFACE"), "OPAQUE");
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
        Material material = _system.Create(_pbr);
        material.Surface = new SurfaceSettings(SurfaceType.Transparent);
        material.RenderQueue = 2000;
        _system.Extract(new List<Material>());
        Assert.True(material.Draw.Transparent);

        material.Surface = new SurfaceSettings(SurfaceType.Opaque);
        material.RenderQueue = 3000;
        _system.Extract(new List<Material>());
        Assert.False(material.Draw.Transparent);
    }

    // Every OpenPBR v1.1.1 parameter under its spec name with the spec's default, and whether it has a map
    private static readonly (string Name, float[] Default, bool Map)[] s_spec =
    [
        ("base_weight", [1], true), ("base_color", [0.8f, 0.8f, 0.8f], true), ("base_metalness", [0], true), ("base_diffuse_roughness", [0], true),
        ("specular_weight", [1], true), ("specular_color", [1, 1, 1], true), ("specular_roughness", [0.3f], true),
        ("specular_roughness_anisotropy", [0], true), ("specular_ior", [1.5f], false),
        ("transmission_weight", [0], true), ("transmission_color", [1, 1, 1], true), ("transmission_depth", [0], false),
        ("transmission_scatter", [0, 0, 0], false), ("transmission_scatter_anisotropy", [0], false),
        ("transmission_dispersion_scale", [0], false), ("transmission_dispersion_abbe_number", [20], false),
        ("subsurface_weight", [0], true), ("subsurface_color", [0.8f, 0.8f, 0.8f], true), ("subsurface_radius", [1], true),
        ("subsurface_radius_scale", [1, 0.5f, 0.25f], false), ("subsurface_scatter_anisotropy", [0], false),
        ("coat_weight", [0], true), ("coat_color", [1, 1, 1], true), ("coat_roughness", [0], true), ("coat_roughness_anisotropy", [0], true),
        ("coat_ior", [1.6f], false), ("coat_darkening", [1], true),
        ("fuzz_weight", [0], true), ("fuzz_color", [1, 1, 1], true), ("fuzz_roughness", [0.5f], true),
        ("emission_luminance", [0], true), ("emission_color", [1, 1, 1], true),
        ("thin_film_weight", [0], true), ("thin_film_thickness", [0.5f], true), ("thin_film_ior", [1.4f], false),
        ("geometry_opacity", [1], true), ("geometry_thin_walled", [0], false),
    ];

    [Fact]
    public void PBR_DeclaresEverySpecParameter_UnderItsSpecName_WithAMapForEachTexturableOne()
    {
        IReadOnlyList<MaterialProperty> properties = _pbr.Properties;
        foreach ((string name, float[] value, bool map) in s_spec)
        {
            MaterialProperty property = properties.Single(p => p.Name.Text() == name);
            for (int c = 0; c < value.Length; c++)
                Assert.Equal(value[c], property.Default.Vector[c], 5);
            int index = _pbr.FindProperty(name + "_map");
            Assert.Equal(map, index >= 0);
            if (map)
                Assert.True(properties[index].MapBit >= 0, name);
        }

        foreach (string geometry in new[] { "geometry_normal_map", "geometry_coat_normal_map", "geometry_tangent_map", "geometry_height_map", "ambient_occlusion_map" })
            Assert.True(properties[_pbr.FindProperty(geometry)].MapBit >= 0, geometry);
        Assert.Equal(31, properties.Count(p => p.MapBit >= 0));
        Assert.True(_pbr.Schema.MapMaskOffset >= 0);
    }

    [Fact]
    public void Maps_PackSlotSamplerChannelUVAndFloatScaleOffsetRotation_AndTheirMaskFollowsParents()
    {
        Material parent = _system.Create(_pbr);
        Material child = _system.Create(_pbr);
        child.Parent = parent;
        using Texture texture = MakeTexture();
        parent.SetTexture("specular_roughness_map", texture);
        parent.SetTextureChannel("specular_roughness_map", 1);
        child.SetTextureUVSet("specular_roughness_map", 1);
        child.SetTextureScaleOffset("specular_roughness_map", new Prowl.Vector.Float4(1.0001f, 4, 0.73291f, -0.25f));
        child.SetTextureRotation("specular_roughness_map", 0.5f);

        uint[] record = Pack(child);
        MaterialProperty map = _pbr.Properties[_pbr.FindProperty("specular_roughness_map")];
        int at = (int)(map.Offset / 4);
        Assert.Equal(1u, record[at] & 0xFFFFu);
        Assert.Equal(1u, (record[at] >> 22) & 3u);
        Assert.Equal(1u, (record[at] >> 24) & 1u);
        Assert.Equal(1.0001f, BitConverter.UInt32BitsToSingle(record[at + 1]));
        Assert.Equal(4f, BitConverter.UInt32BitsToSingle(record[at + 2]));
        Assert.Equal(0.73291f, BitConverter.UInt32BitsToSingle(record[at + 3]));
        Assert.Equal(-0.25f, BitConverter.UInt32BitsToSingle(record[at + 4]));
        Assert.Equal(MathF.Cos(0.5f), BitConverter.UInt32BitsToSingle(record[at + 5]));
        Assert.Equal(MathF.Sin(0.5f), BitConverter.UInt32BitsToSingle(record[at + 6]));

        child.SetTexture("specular_roughness_map", texture);
        Assert.Equal(MathF.Sin(0.5f), BitConverter.UInt32BitsToSingle(Pack(child)[at + 6]));

        int mask = _pbr.Schema.MapMaskOffset / 4;
        Assert.Equal(1u << map.MapBit, record[mask]);

        child.SetTexture("specular_roughness_map", null);
        Assert.Equal(0u, Pack(child)[mask]);
    }

    private uint[] Pack(Material material)
    {
        MaterialValue[] values = new MaterialValue[_pbr.Properties.Count];
        material.Resolve(values);
        uint[] record = new uint[_pbr.Schema.StrideWords];
        _pbr.Schema.Pack(values, record, new SlotResources());
        return record;
    }

    [Fact]
    public void SettingAMap_ChangesTheRecord_NotTheDraw()
    {
        Material material = _system.Create(_pbr);
        _system.Extract(new List<Material>());

        List<Material> changed = new();
        List<Material> records = new();
        using Texture texture = MakeTexture();
        material.SetTexture("base_color_map", texture);
        material.SetFloat("specular_roughness", 0.25f);
        _system.Extract(changed, records);
        Assert.DoesNotContain(material, changed);
        Assert.Contains(material, records);
    }
}
