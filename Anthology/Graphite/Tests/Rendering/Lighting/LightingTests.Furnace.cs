using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model itself, Graphite/PBR, on the GPU: the white furnace under a uniform white sky with no lights, emission
// through the exposure, coverage, and direct light against PbrReference with every base input away from its default.
public sealed partial class LightingTests
{
    private static readonly float[] s_furnaceTilts = [0f, 50f, 75f];

    // The quad wound to face the camera, which standard surfaces need to shade their front
    private GpuMesh FacingQuad => _facingQuad ??= _renderer.CreateMesh(new MeshData
    {
        VertexCount = 4,
        Streams =
        [
            new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(new Float3[] { new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0) }.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(new Float3[] { new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1) }.AsSpan()).ToArray() },
        ],
        Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
    });

    private GpuMesh? _facingQuad;

    private RenderableHandle AddFacingQuad(Material material)
        => _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [material]);

    // A large quad tilted about Y, filling the view, so its pixels see it from far off its normal
    private RenderableHandle AddTiltedQuad(Material material, float degrees)
        => _world.Add(new RenderableDesc
        {
            Mesh = FacingQuad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, 40)) * Double4x4.RotateY(degrees * Math.PI / 180) * Double4x4.CreateScale(20),
        }, [material]);

    private Material PBR(Action<Material> set, Shader? shader = null)
    {
        Material material = _renderer.CreateMaterial(shader ?? _renderer.Standard.Lit);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        set(material);
        return material;
    }

    // Under a uniform white sky and its harmonics, no lights and no occlusion, a surface that keeps its energy is 1, or
    // passes check where one is given
    private void AssertFurnace(string what, Action<Material> set, Shader? shader = null, float[]? tilts = null, Action<string, Float4>? check = null)
    {
        foreach (float tilt in tilts ?? s_furnaceTilts)
        {
            _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Ambient = AmbientMode.Sky, AmbientIntensity = 1, Fog = default, Sky = SkyMaterial(new Float3(1, 1, 1), 0) };
            RenderableHandle quad = AddTiltedQuad(PBR(set, shader), tilt);
            Frame();
            Float4[] pixels = Frame();
            _world.Remove(quad);
            for (int i = 0; i < pixels.Length; i++)
            {
                string where = $"{what} tilt {tilt} pixel {i}";
                if (check != null)
                {
                    check(where, pixels[i]);
                    continue;
                }
                for (int c = 0; c < 3; c++)
                    Assert.True(MathF.Abs(pixels[i][c] - 1) <= 2e-3f, $"{where} channel {c}: {pixels[i][c]}");
            }
        }
    }

    [Fact]
    public void TheFurnace_WhiteMetal_IsOneAtEveryRoughness()
    {
        foreach (float r in new[] { 0.05f, 0.25f, 0.5f, 0.75f, 1f })
        {
            AssertFurnace($"metal r {r}", m =>
            {
                m.SetColor("base_color", new Float4(1, 1, 1, 1));
                m.SetFloat("base_metalness", 1);
                m.SetFloat("specular_roughness", r);
            });
        }
    }

    [Fact]
    public void TheFurnace_WhiteEon_IsOneAtEveryDiffuseRoughness()
    {
        foreach (float rd in new[] { 0f, 0.5f, 1f })
        {
            AssertFurnace($"EON rd {rd}", m =>
            {
                m.SetColor("base_color", new Float4(1, 1, 1, 1));
                m.SetFloat("specular_weight", 0);
                m.SetFloat("base_diffuse_roughness", rd);
            });
        }
    }

    [Fact]
    public void TheFurnace_WhiteDielectric_IsOneAcrossIorWeightAndRoughness()
    {
        foreach (float ior in new[] { 1f, 1.5f, 3f })
        {
            foreach (float weight in new[] { 0f, 0.5f, 1f })
            {
                foreach (float r in new[] { 0.05f, 0.5f, 1f })
                {
                    AssertFurnace($"dielectric ior {ior} weight {weight} r {r}", m =>
                    {
                        m.SetColor("base_color", new Float4(1, 1, 1, 1));
                        m.SetFloat("specular_ior", ior);
                        m.SetFloat("specular_weight", weight);
                        m.SetFloat("specular_roughness", r);
                        m.SetFloat("base_diffuse_roughness", 0.5f);
                    });
                }
            }
        }
    }

    [Fact]
    public void TheFurnace_MixesOfMetalAndDielectric_AreOne()
    {
        foreach (float metalness in new[] { 0.3f, 0.7f })
        {
            AssertFurnace($"metalness {metalness}", m =>
            {
                m.SetColor("base_color", new Float4(1, 1, 1, 1));
                m.SetFloat("base_metalness", metalness);
                m.SetFloat("specular_roughness", 0.4f);
                m.SetFloat("specular_ior", 1.8f);
            });
        }
    }

    // A black, unreflecting surface leaves its emission alone, in nits through the view's exposure
    [Fact]
    public void Emission_IsInNits_ThroughTheViewsExposure()
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        AddFacingQuad(PBR(m =>
        {
            m.SetColor("base_color", new Float4(0, 0, 0, 1));
            m.SetFloat("specular_weight", 0);
            m.SetColor("emission_color", new Float4(1, 0.5f, 0.25f, 1));
            m.SetFloat("emission_luminance", 400);
        }));
        _ev100 = 8;
        float exposure = RenderView.ExposureOf(8);
        Frame();
        AssertMatches(_ => new Vector3(400, 200, 100) * exposure, Frame());
    }

    // A map's UVs scale, turn counter clockwise and offset in KHR_texture_transform's order, the offset exact in float:
    // turned a quarter, u' = offset - v, so a horizontal two texel ramp runs along v
    [Fact]
    public void MapUVs_ScaleRotateThenOffset_AtFullPrecision()
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        Texture ramp = Lightmap<Float4>(2, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0, 0, 0, 1), new Float4(1, 1, 1, 1)]);
        _ev100 = 8;
        float exposure = RenderView.ExposureOf(8);
        const float offset = 1024.25f;
        _world.Add(new RenderableDesc { Mesh = IndirectQuad(secondUVs: false), Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [PBR(m =>
        {
            m.SetColor("base_color", new Float4(0, 0, 0, 1));
            m.SetFloat("specular_weight", 0);
            m.SetFloat("emission_luminance", 1 / exposure);
            m.SetTexture("emission_luminance_map", ramp, SamplerDescription.Linear);
            m.SetTextureScaleOffset("emission_luminance_map", new Float4(1, 1, offset, 0));
            m.SetTextureRotation("emission_luminance_map", MathF.PI / 2);
        })]);
        Frame();
        Float4[] pixels = Frame();

        for (int i = 0; i < pixels.Length; i++)
        {
            Vector3 p = PixelPosition(i % Size, i / Size);
            float v = (p.Y + 4) / 8;
            float w = offset - v - MathF.Floor(offset - v);
            float want = w < 0.25f ? (0.25f - w) * 2 : w < 0.75f ? (w - 0.25f) * 2 : 1 - (w - 0.75f) * 2;
            Assert.True(MathF.Abs(pixels[i].X - want) <= 6e-3f, $"pixel {i}: expected {want}, got {pixels[i].X}");
        }
    }

    // Light too bright for the half float scene colour saturates at its largest value instead of becoming infinite
    [Fact]
    public void Emission_PastTheHalfFloatRange_Saturates()
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        AddFacingQuad(PBR(m =>
        {
            m.SetColor("base_color", new Float4(0, 0, 0, 1));
            m.SetFloat("specular_weight", 0);
            m.SetColor("emission_color", new Float4(1, 1, 1, 1));
            m.SetFloat("emission_luminance", 1e7f);
        }));
        _ev100 = 0;
        Frame();
        AssertMatches(_ => new Vector3(65504), Frame());
    }

    // Half coverage over an unlit backdrop blends to half of each, whichever of the coverage blends the material picks
    [Theory]
    [InlineData(SurfaceBlend.Alpha)]
    [InlineData(SurfaceBlend.Premultiplied)]
    public void HalfGeometryOpacity_OverABackdrop_BlendsHalfOfEach(SurfaceBlend blend)
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        Material backdrop = _renderer.CreateMaterial(_renderer.Standard.Unlit);
        backdrop.SetColor("base_color", new Float4(0.2f, 0.4f, 0.8f, 1));
        _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, 20)) }, [backdrop]);
        AddFacingQuad(PBR(m =>
        {
            m.Surface = new SurfaceSettings(SurfaceType.Transparent, blend, SurfaceCull.Off);
            m.SetColor("base_color", new Float4(0, 0, 0, 1));
            m.SetFloat("specular_weight", 0);
            m.SetColor("emission_color", new Float4(1, 1, 1, 1));
            m.SetFloat("emission_luminance", 0.8f);
            m.SetFloat("geometry_opacity", 0.5f);
        }));
        Frame();
        AssertMatches(_ => new Vector3(0.5f) * 0.8f + new Vector3(0.2f, 0.4f, 0.8f) * 0.5f, Frame());
    }

    public static IEnumerable<object[]> ModelInputs()
    {
        yield return [new Float3(0.8f, 0.6f, 0.3f), 0f, 0.4f, 0.7f, 1f, new Float3(1, 1, 1), 1.5f];
        yield return [new Float3(0.5f, 0.7f, 0.9f), 0.3f, 0.25f, 0.2f, 0.6f, new Float3(1, 0.6f, 0.4f), 2.2f];
        yield return [new Float3(0.95f, 0.64f, 0.54f), 1f, 0.35f, 0f, 0.8f, new Float3(0.9f, 0.8f, 0.5f), 1.5f];
        yield return [new Float3(0.3f, 0.3f, 0.3f), 0f, 0.6f, 1f, 1f, new Float3(1, 1, 1), 0.8f];
    }

    // The whole model, material inputs and all, against the CPU closure under a point and a directional light
    [Theory]
    [MemberData(nameof(ModelInputs))]
    public void ThePBRModel_LightsAsItsReferenceSays(Float3 color, float metalness, float roughness, float diffuseRoughness, float weight, Float3 tint, float ior)
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        AddFacingQuad(PBR(m =>
        {
            m.SetColor("base_color", new Float4(color.X, color.Y, color.Z, 1));
            m.SetFloat("base_metalness", metalness);
            m.SetFloat("specular_roughness", roughness);
            m.SetFloat("base_diffuse_roughness", diffuseRoughness);
            m.SetFloat("specular_weight", weight);
            m.SetColor("specular_color", new Float4(tint.X, tint.Y, tint.Z, 1));
            m.SetFloat("specular_ior", ior);
        }));
        LightDesc point = Point(new Double3(1, 0.5, 7), 20, 3, new Float3(1, 0.9f, 0.8f));
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(-0.3f, 0.2f, 1), Color = new Float3(0.9f, 0.95f, 1), Intensity = 0.4f };
        _world.AddLight(point);
        _world.AddLight(sun);

        PbrInputs inputs = new(new Vector3(color.X, color.Y, color.Z), metalness, roughness, diffuseRoughness, 1, weight, new Vector3(tint.X, tint.Y, tint.Z), ior);
        Frame();
        AssertMatches(p =>
        {
            PbrReference c = new(inputs, s_normal, Vector3.Normalize(-p));
            Vector3 toPoint = new Vector3(1, 0.5f, 7) - p;
            float d2 = toPoint.LengthSquared();
            Vector3 l = toPoint / MathF.Sqrt(d2);
            float factor = d2 / 400;
            float window = Math.Clamp(1 - factor * factor, 0, 1);
            float nl = MathF.Max(Vector3.Dot(s_normal, l), 0);
            Vector3 lit = c.Evaluate(l) * nl * window * window / d2 * 3 * new Vector3(1, 0.9f, 0.8f);
            Vector3 ls = Vector3.Normalize(-new Vector3(-0.3f, 0.2f, 1));
            return lit + c.Evaluate(ls) * MathF.Max(Vector3.Dot(s_normal, ls), 0) * 0.4f * new Vector3(0.9f, 0.95f, 1);
        }, Frame());
    }
}
