using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model's coat, fuzz, thin film and anisotropy on the GPU: the white furnace over white bases, turned frames,
// the LITE tier, and each lobe against PbrReference under a point and a directional light.
public sealed partial class LightingTests
{
    private static void White(Material m) => m.SetColor("base_color", new Float4(1, 1, 1, 1));

    // A dielectric with diffuse, and a metal
    private static readonly Action<Material>[] s_whiteBases =
    [
        m => { White(m); m.SetFloat("specular_roughness", 0.4f); m.SetFloat("base_diffuse_roughness", 0.5f); },
        m => { White(m); m.SetFloat("base_metalness", 1); m.SetFloat("specular_roughness", 0.3f); },
    ];

    [Fact]
    public void TheFurnace_WhiteCoat_IsOneAtEitherDarkening()
    {
        foreach (float darkening in new[] { 0f, 1f })
        {
            foreach ((float ior, float r, float weight) in new[] { (1.3f, 0.05f, 1f), (1.6f, 0.4f, 1f), (2.5f, 0.8f, 0.5f) })
            {
                for (int b = 0; b < s_whiteBases.Length; b++)
                {
                    Action<Material> surface = s_whiteBases[b];
                    AssertFurnace($"coat ior {ior} r {r} weight {weight} darkening {darkening} base {b}", m =>
                    {
                        surface(m);
                        m.SetFloat("coat_weight", weight);
                        m.SetFloat("coat_ior", ior);
                        m.SetFloat("coat_roughness", r);
                        m.SetFloat("coat_darkening", darkening);
                    });
                }
            }
        }
    }

    [Fact]
    public void TheFurnace_WhiteFuzz_IsOne()
    {
        foreach (float r in new[] { 0.1f, 0.5f, 1f })
        {
            for (int b = 0; b < s_whiteBases.Length; b++)
            {
                Action<Material> surface = s_whiteBases[b];
                float coat = b;
                AssertFurnace($"fuzz r {r} base {b}", m =>
                {
                    surface(m);
                    m.SetFloat("fuzz_weight", 1);
                    m.SetFloat("fuzz_roughness", r);
                    m.SetFloat("coat_weight", coat * 0.5f);
                });
            }
        }
    }

    [Fact]
    public void TheFurnace_SpecularAndCoatAnisotropy_AreOne()
    {
        foreach (float a in new[] { 0.5f, 0.9f })
        {
            for (int b = 0; b < s_whiteBases.Length; b++)
            {
                Action<Material> surface = s_whiteBases[b];
                AssertFurnace($"anisotropy {a} base {b}", m =>
                {
                    surface(m);
                    m.SetFloat("specular_roughness_anisotropy", a);
                    m.SetFloat("coat_weight", 1);
                    m.SetFloat("coat_roughness", 0.3f);
                    m.SetFloat("coat_roughness_anisotropy", a);
                });
            }
        }
    }

    // Base and coat normals and tangents turned across the surface as normal maps would, the coat apart from the base
    private const string BumpySource = """
        Shader "Tests/Bumpy"
        {
            Surface "Lit"
            {
                GLSLPROGRAM
                void Surface(inout PBR_Params p, Graphite_SurfaceData i)
                {
                    vec3 w = i.relativePos * 0.7;
                    vec2 tilt = vec2(sin(w.x), cos(w.y)) * 0.25;
                    p.geometryNormal = normalize(i.geometryNormal + i.tangent * tilt.x + i.bitangent * tilt.y);
                    p.geometryCoatNormal = normalize(i.geometryNormal - i.tangent * tilt.y + i.bitangent * tilt.x);
                    p.geometryTangent = i.tangent * cos(w.y) + i.bitangent * sin(w.y);
                    p.geometryCoatTangent = i.tangent * sin(w.x) + i.bitangent * cos(w.x);
                }
                ENDGLSL
            }
        }
        """;

    [Fact]
    public void TheFurnace_TurnedNormalsAndTangents_AreOne()
    {
        Shader bumpy = _renderer.LoadShader(ShaderParser.Parse(BumpySource));
        for (int b = 0; b < s_whiteBases.Length; b++)
        {
            Action<Material> surface = s_whiteBases[b];
            AssertFurnace($"turned frames base {b}", m =>
            {
                surface(m);
                m.SetFloat("specular_roughness_anisotropy", 0.6f);
                m.SetFloat("coat_weight", 1);
                m.SetFloat("coat_roughness", 0.2f);
                m.SetFloat("coat_roughness_anisotropy", 0.4f);
                m.SetFloat("fuzz_weight", 0.5f);
            }, bumpy, [0f, 50f]);
        }
    }

    // The film moves energy between channels: luminance stays 1, each channel within the CPU furnace's band
    [Fact]
    public void TheFurnace_ThinFilm_KeepsLuminance()
    {
        foreach (float thickness in new[] { 0.3f, 0.8f })
        {
            foreach ((string name, Action<Material> surface) in new (string, Action<Material>)[]
            {
                ("dielectric", s_whiteBases[0]),
                ("half metal", m => { White(m); m.SetFloat("base_metalness", 0.5f); m.SetFloat("specular_roughness", 0.6f); m.SetFloat("specular_ior", 2); }),
                ("coated", m => { s_whiteBases[0](m); m.SetFloat("coat_weight", 1); m.SetFloat("coat_roughness", 0.2f); }),
            })
            {
                AssertFurnace($"film {thickness} um over {name}", m =>
                {
                    surface(m);
                    m.SetFloat("thin_film_weight", 1);
                    m.SetFloat("thin_film_thickness", thickness);
                    m.SetFloat("thin_film_ior", 1.4f);
                }, check: (where, pixel) =>
                {
                    float luminance = 0.2126f * pixel.X + 0.7152f * pixel.Y + 0.0722f * pixel.Z;
                    Assert.True(MathF.Abs(luminance - 1) <= 2e-3f, $"{where}: luminance {luminance}");
                    for (int c = 0; c < 3; c++)
                        Assert.True(MathF.Abs(pixel[c] - 1) <= PbrFurnaceTests.FilmBand, $"{where} channel {c}: {pixel[c]}");
                });
            }
        }
    }

    // LITE drops the film and anisotropy and lights the coat and fuzz from the surroundings only, and keeps the energy
    [Fact]
    public void TheFurnace_AtTheLiteTier_IsOne()
    {
        _world.Globals.Set(GlobalAxis.PbrTier, "LITE");
        AssertFurnace("lite", m =>
        {
            s_whiteBases[0](m);
            m.SetFloat("coat_weight", 0.7f);
            m.SetFloat("coat_roughness", 0.3f);
            m.SetFloat("fuzz_weight", 0.6f);
            m.SetFloat("thin_film_weight", 1);
            m.SetFloat("specular_roughness_anisotropy", 0.8f);
        });
    }

    private static readonly PbrInputs[] s_lobeInputs =
    [
        new(new Vector3(0.6f, 0.3f, 0.2f), Roughness: 0.5f, DiffuseRoughness: 0.3f) { CoatWeight = 0.8f, CoatColor = new Vector3(0.9f, 0.7f, 0.5f), CoatRoughness = 0.2f, CoatIor = 1.7f },
        new(new Vector3(0.9f, 0.6f, 0.4f), Metalness: 1, Roughness: 0.4f, SpecularColor: new Vector3(0.9f, 0.8f, 0.6f)) { Anisotropy = 0.7f, CoatWeight = 1, CoatRoughness = 0.1f, CoatAnisotropy = 0.5f, CoatDarkening = 0.5f },
        new(new Vector3(0.3f, 0.4f, 0.6f), Roughness: 0.6f) { FuzzWeight = 0.9f, FuzzColor = new Vector3(1, 0.8f, 0.6f), FuzzRoughness = 0.4f },
        new(new Vector3(0.5f, 0.5f, 0.5f), Metalness: 0.4f, Roughness: 0.3f) { FilmWeight = 1, FilmThickness = 0.45f, FilmIor = 1.6f },
        new(new Vector3(0.7f, 0.5f, 0.3f), Roughness: 0.35f, Ior: 1.4f) { Anisotropy = 0.5f, CoatWeight = 0.5f, CoatRoughness = 0.3f, CoatIor = 1.8f, FuzzWeight = 0.5f, FuzzRoughness = 0.7f, FilmWeight = 0.7f, FilmThickness = 0.3f, SubsurfaceWeight = 0.5f, SubsurfaceColor = new Vector3(0.9f, 0.4f, 0.3f) },
    ];

    public static IEnumerable<object[]> LobeCases()
    {
        for (int i = 0; i < s_lobeInputs.Length; i++)
            yield return [i];
    }

    private static void SetInputs(Material m, in PbrInputs p)
    {
        m.SetColor("base_color", new Float4(p.BaseColor.X, p.BaseColor.Y, p.BaseColor.Z, 1));
        m.SetFloat("base_metalness", p.Metalness);
        m.SetFloat("specular_roughness", p.Roughness);
        m.SetFloat("base_diffuse_roughness", p.DiffuseRoughness);
        m.SetFloat("specular_ior", p.Ior);
        m.SetColor("specular_color", new Float4(p.Tint.X, p.Tint.Y, p.Tint.Z, 1));
        m.SetFloat("specular_roughness_anisotropy", p.Anisotropy);
        m.SetFloat("subsurface_weight", p.SubsurfaceWeight);
        m.SetColor("subsurface_color", new Float4(p.SubsurfaceColor.X, p.SubsurfaceColor.Y, p.SubsurfaceColor.Z, 1));
        m.SetFloat("subsurface_radius", p.SubsurfaceRadius);
        m.SetColor("subsurface_radius_scale", new Float4(p.SubsurfaceRadiusScale.X, p.SubsurfaceRadiusScale.Y, p.SubsurfaceRadiusScale.Z, 1));
        m.SetFloat("subsurface_scatter_anisotropy", p.SubsurfaceAnisotropy);
        m.SetInt("geometry_thin_walled", p.ThinWalled ? 1 : 0);
        m.SetFloat("transmission_weight", p.TransmissionWeight);
        m.SetColor("transmission_color", new Float4(p.TransmissionColor.X, p.TransmissionColor.Y, p.TransmissionColor.Z, 1));
        m.SetFloat("coat_weight", p.CoatWeight);
        m.SetColor("coat_color", new Float4(p.CoatColor.X, p.CoatColor.Y, p.CoatColor.Z, 1));
        m.SetFloat("coat_roughness", p.CoatRoughness);
        m.SetFloat("coat_roughness_anisotropy", p.CoatAnisotropy);
        m.SetFloat("coat_ior", p.CoatIor);
        m.SetFloat("coat_darkening", p.CoatDarkening);
        m.SetFloat("fuzz_weight", p.FuzzWeight);
        m.SetColor("fuzz_color", new Float4(p.FuzzColor.X, p.FuzzColor.Y, p.FuzzColor.Z, 1));
        m.SetFloat("fuzz_roughness", p.FuzzRoughness);
        m.SetFloat("thin_film_weight", p.FilmWeight);
        m.SetFloat("thin_film_thickness", p.FilmThickness);
        m.SetFloat("thin_film_ior", p.FilmIor);
    }

    // Each lobe through its material inputs against the CPU closure under a point and a directional light
    [Theory]
    [MemberData(nameof(LobeCases))]
    public void TheLobes_LightAsTheReferenceSays(int index)
    {
        PbrInputs p = s_lobeInputs[index];
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        AddFacingQuad(PBR(m => SetInputs(m, p)));
        _world.AddLight(Point(new Double3(1, 0.5, 7), 20, 3, new Float3(1, 0.9f, 0.8f)));
        _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(-0.3f, 0.2f, 1), Color = new Float3(0.9f, 0.95f, 1), Intensity = 0.4f });

        Frame();
        AssertMatches(position =>
        {
            PbrReference c = new(p, s_normal, Vector3.Normalize(-position));
            Vector3 toPoint = new Vector3(1, 0.5f, 7) - position;
            float d2 = toPoint.LengthSquared();
            float factor = d2 / 400;
            float window = Math.Clamp(1 - factor * factor, 0, 1);
            Vector3 lit = c.Shade(toPoint / MathF.Sqrt(d2)) * window * window / d2 * 3 * new Vector3(1, 0.9f, 0.8f);
            return lit + c.Shade(Vector3.Normalize(-new Vector3(-0.3f, 0.2f, 1))) * 0.4f * new Vector3(0.9f, 0.95f, 1);
        }, Frame());
    }
}
