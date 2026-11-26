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
        _pbr = Load("Standard/Lit");
        _unlit = Load("Standard/Unlit");
    }

    public void Dispose()
    {
        _store.Dispose();
        _device.Dispose();
    }

    private Shader Load(string path) => new(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}{path}.shader")!), _compiler);

    private Texture MakeTexture() => _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

    // Every surface of the standard shaders and every other shipped shader, at every tier
    public static IEnumerable<object[]> Variants()
    {
        foreach (string shader in new[] { "Standard/Lit", "Standard/Unlit" })
        {
            foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            {
                foreach (string tier in new[] { "FULL", "LITE", "MAX" })
                    yield return [shader, surface, tier];
            }
        }
        foreach (string shader in new[] { "Ocean/Water", "Terrain/Default", "Terrain/Grass", "Foliage/Leaves" })
        {
            foreach (string tier in new[] { "FULL", "LITE", "MAX" })
                yield return [shader, "", tier];
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EveryPassOfAVariantCompiles(string shader, string surface, string tier)
    {
        Shader loaded = shader switch { "Standard/Lit" => _pbr, "Standard/Unlit" => _unlit, _ => Load(shader) };
        Keyword[] combo = surface.Length > 0 ? [new Keyword("_SURFACE", surface), new Keyword("PBR_TIER", tier)] : [new Keyword("PBR_TIER", tier)];
        Assert.NotEmpty(loaded.Definition.Passes!);
        List<(ShaderPass, Keyword[])> variants = [];
        foreach (ShaderPass pass in loaded.Definition.Passes!)
            variants.Add((pass, combo));
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

    // Surface, vertex and lighting code together: a rim, a wave, a detail normal through the tangent frame, and a toon ramp
    private const string CustomSource = """
        Shader "Tests/Custom"
        {
            Properties
            {
                _RimColor("Rim", Color) = (1.0, 0.0, 0.0, 1.0)
                base_metalness("Metal", Float) = 0.5
                _WaveHeight("Wave Height", Float) = 0.2
                _DetailTex("Detail Normal", Texture2D) = "normal" {}
            }

            Surface "Lit"
            {
                GLSLPROGRAM
                uniform vec4 _RimColor;
                uniform sampler2D _DetailTex;
                void Surface(inout PBR_Params p, Graphite_SurfaceData i)
                {
                    vec3 detail = Graphite_SurfaceNormalMap(i, texture(_DetailTex, i.uv * 4.0).rgb, 1.0);
                    vec3 frame = cross(i.tangent, i.bitangent);
                    p.geometryNormal = normalize(p.geometryNormal + detail + frame * 0.0);
                    p.emissionColor = _RimColor.rgb;
                    p.emissionLuminance = 100.0 * pow(1.0 - max(dot(p.geometryNormal, i.viewDir), 0.0), 3.0);
                    p.geometryOpacity *= i.vertexColor.a;
                    p.specularRoughness = 0.5;
                }
                ENDGLSL

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

    private static readonly string[] s_pbrPasses = ["Forward", "GBuffer", "Transmission", "BackDepth", "Prepass", "ShadowCaster", "Voxelize", "Impostor"];

    [Theory]
    [InlineData("TRANSPARENT")]
    public void SurfaceLighting_ReplacesTheBRDF_AndEveryFeatureStillCompiles(string surface)
    {
        ShaderDefinition definition = new Shader(ShaderParser.Parse(CustomSource), _compiler).Definition;
        Assert.Equal(s_pbrPasses.Where(pass => pass != "GBuffer"), definition.Passes!.Select(p => p.Name));
        string[] names = [.. definition.Properties!.Select(p => p.Name)];
        Assert.Contains("base_color_map", names);
        Assert.Contains("_RimColor", names);
        Assert.Equal(0.5f, definition.Properties!.Single(p => p.Name == "base_metalness").Value.X);
        Assert.Equal(names.Length, names.Distinct().Count());

        foreach (ShaderPass pass in definition.Passes!)
        {
            bool lit = pass.Name is "Forward" or "Transmission";
            Assert.Equal(lit, pass.Source.Contains("#define GRAPHITE_CUSTOM_BRDF"));
            Assert.Equal(lit, pass.Source.Contains("float ramp"));
            Assert.Contains("void SurfaceVertex(inout Graphite_SurfaceVertex v)", pass.Source);
            _compiler.CompileProgram(pass, [new Keyword("_SURFACE", surface)]);
        }
    }

    // The axes deciding a surface's lobes and how much light a caster lets through to its shadow
    private static readonly string[] s_lobeAxes = ["PBR_THIN_WALLED", "PBR_TRANSMISSION"];

    // The combo with each lobe axis on where its bit in the mask is
    private static Keyword[] WithLobes(List<Keyword> combo, int mask)
    {
        for (int i = 0; i < s_lobeAxes.Length; i++)
            combo.Add(new Keyword(s_lobeAxes[i], (mask & (1 << i)) != 0 ? "true" : "false"));
        return [.. combo];
    }

    // Every pass at every surface and tier with the lobe axes all off and all on, every lobe combination in the forward
    // and transmission passes at every tier and in the shadow caster at every surface, and the back depth pass at
    // every surface
    [Fact]
    public void EveryLobeVariant_Compiles()
    {
        int all = (1 << s_lobeAxes.Length) - 1;
        List<(ShaderPass, Keyword[])> variants = [];
        foreach (ShaderPass pass in _pbr.Definition.Passes!)
        {
            foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            {
                foreach (string tier in new[] { "FULL", "LITE", "MAX" })
                {
                    foreach (int mask in new[] { 0, all })
                        variants.Add((pass, WithLobes([new Keyword("_SURFACE", surface), new Keyword("PBR_TIER", tier)], mask)));
                }
            }
        }

        ShaderPass forward = _pbr.Definition.Passes!.First(p => p.Name == "Forward");
        ShaderPass transmission = _pbr.Definition.Passes!.First(p => p.Name == "Transmission");
        ShaderPass caster = _pbr.Definition.Passes!.First(p => p.Name == "ShadowCaster");
        ShaderPass backDepth = _pbr.Definition.Passes!.First(p => p.Name == "BackDepth");
        for (int mask = 0; mask <= all; mask++)
        {
            foreach (string tier in new[] { "FULL", "LITE", "MAX" })
            {
                variants.Add((forward, WithLobes([new Keyword("_SURFACE", "OPAQUE"), new Keyword("PBR_TIER", tier)], mask)));
                variants.Add((transmission, WithLobes([new Keyword("_SURFACE", "OPAQUE"), new Keyword("PBR_TIER", tier)], mask)));
            }
            foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
                variants.Add((caster, WithLobes([new Keyword("_SURFACE", surface)], mask)));
        }
        foreach (string surface in new[] { "OPAQUE", "CUTOUT", "TRANSPARENT" })
            variants.Add((backDepth, [new Keyword("_SURFACE", surface)]));
        CompileAll(variants);
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

    // Every OpenPBR v1.1.1 parameter Lit has under its spec name with the spec's default, and whether it has a map. The
    // coat, fuzz, subsurface and anisotropy ones belong to their own surface models.
    private static readonly (string Name, float[] Default, bool Map)[] s_spec =
    [
        ("base_weight", [1], true), ("base_color", [0.8f, 0.8f, 0.8f], true), ("base_metalness", [0], true), ("base_diffuse_roughness", [0], true),
        ("specular_weight", [1], true), ("specular_color", [1, 1, 1], true), ("specular_roughness", [0.3f], true),
        ("specular_ior", [1.5f], false),
        ("transmission_weight", [0], true), ("transmission_color", [1, 1, 1], true), ("transmission_depth", [0], false),
        ("transmission_scatter", [0, 0, 0], false), ("transmission_scatter_anisotropy", [0], false),
        ("transmission_dispersion_scale", [0], false), ("transmission_dispersion_abbe_number", [20], false),
        ("emission_luminance", [0], true), ("emission_color", [1, 1, 1], true),
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

        foreach (string geometry in new[] { "geometry_normal_map", "geometry_height_map", "ambient_occlusion_map" })
            Assert.True(properties[_pbr.FindProperty(geometry)].MapBit >= 0, geometry);
        Assert.Equal(15, properties.Count(p => p.MapBit >= 0));
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
}
