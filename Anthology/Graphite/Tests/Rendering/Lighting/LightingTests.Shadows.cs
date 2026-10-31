using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// Shadows of PBR casters that let light through: coverage, transmission and thin walled subsurface dither the shadow map
// by the grey transmittance T = 1 - alpha (1 - (1 - M) T_dielectric), which the shadow filter averages on the receiver.
public sealed partial class LightingTests
{
    // The dither is a per texel pattern, so the mean over the shadow's inside lands within this of the transmittance
    private const float DitherTolerance = 0.02f;

    private static readonly Vector3 s_sunDirection = Vector3.Normalize(new Vector3(0.5f, 0.25f, 1));

    // The cosine between the sun and the caster, whose normal faces the camera
    private static readonly float s_casterCosine = s_sunDirection.Z;

    // The share of the sun reaching the receiver inside the shadow of a PBR quad caster, averaged over the shadow's
    // inside away from its edge, the receiver fully lit outside it
    private float LitInShadow(Action<Material> set, bool castShadows = true, Shader? shader = null)
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc sun = new()
        {
            Type = LightType.Directional,
            Direction = new Float3(s_sunDirection.X, s_sunDirection.Y, s_sunDirection.Z),
            Intensity = 0.1f,
            Shadow = new ShadowDesc { Enabled = true },
        };
        _world.AddLight(sun);
        Material caster = PBR(set, shader);
        caster.CastShadows = castShadows;

        // From depth 8 to 10 the light moves a unit in x and half in y
        _world.Add(new RenderableDesc
        {
            Mesh = FacingQuad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(-1, -1, 8)) * Double4x4.CreateScale(0.5),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        }, [caster]);
        Frame();
        Float4[] pixels = Frame();

        float sum = 0;
        int inside = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 p = PixelPosition(x, y);
                float edge = SquareEdge(p, 0, -0.5f, 2);
                float lit = Reference.Directional(sun, p, s_normal, albedo, 0, 0.5f).X;
                float got = pixels[y * Size + x].X;
                if (edge < -0.6f)
                {
                    sum += got / lit;
                    inside++;
                }
                else if (edge > 0.6f)
                {
                    Assert.True(MathF.Abs(got - lit) <= 2e-4f + 0.01f * lit, $"pixel {x},{y} outside the shadow: expected {lit}, got {got}");
                }
            }
        }
        Assert.True(inside > 4, $"{inside} pixels inside the shadow");
        return sum / inside;
    }

    // E_spec of the base dielectric at the sun's angle, roughness r and IOR eta, Turquin compensated as the shader does
    private static float DielectricE(float r, float eta)
    {
        Vector3 dfg = EnergyTables.LookupDfg(EnergyTables.EmbeddedDfg(), s_casterCosine, r);
        double E = EnergyTables.LookupDielectric(EnergyTables.EmbeddedDielectric(), s_casterCosine, r, eta);
        return (float)EnergyTables.TurquinDielectric(dfg, E, eta);
    }

    private static void AssertNear(float expected, float got, string what)
        => Assert.True(MathF.Abs(got - expected) <= DitherTolerance, $"{what}: expected {expected} of the light through, got {got}");

    [Fact]
    public void AKeywordOnlyLitPassesRead_LeavesCachedShadowsAlone()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        _world.Add(new RenderableDesc
        {
            Mesh = FacingQuad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.5),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        }, [PBR(_ => { })]);
        Frame();
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);

        // A light with an IES profile turns GRAPHITE_LIGHT_MASKS on, which the caster's lit passes read and its ShadowCaster pass does not
        Texture profile = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _harness.Owned.Add(profile);
        _world.AddLight(new LightDesc { Type = LightType.Spot, Position = new Double3(0, 0, -50), Direction = new Float3(0, 0, -1), Range = 1, Intensity = 0.1f, InnerAngle = 10, OuterAngle = 20, IesProfile = profile });
        Frame();
        Assert.Equal(VariantSpace.ToggleValues[1], _world.Globals.KeywordSnapshot.GetValueOrDefault(GlobalAxis.LightMasks.Name));
        Assert.Equal(0, Stats.ShadowViewsDrawn);
    }

    [Fact]
    public void AHalfOpaqueCaster_CastsHalfAShadow()
    {
        float lit = LitInShadow(m =>
        {
            m.Surface = new SurfaceSettings(SurfaceType.Transparent, Cull: SurfaceCull.Off);
            m.SetFloat("geometry_opacity", 0.5f);
        });
        AssertNear(0.5f, lit, "half opacity");
    }

    [Fact]
    public void AHalfTransparentUnlitCaster_CastsHalfAShadow()
    {
        float lit = LitInShadow(m =>
        {
            m.Surface = new SurfaceSettings(SurfaceType.Transparent, Cull: SurfaceCull.Off);
            m.SetColor("base_color", new Float4(1, 1, 1, 0.5f));
        }, shader: _renderer.Standard.Unlit);
        AssertNear(0.5f, lit, "half transparent unlit");
    }

    [Fact]
    public void AClearGlassPane_CastsOnlyItsSpecularShare()
    {
        float lit = LitInShadow(m =>
        {
            m.SetFloat("transmission_weight", 1);
            m.SetColor("transmission_color", new Float4(1, 1, 1, 1));
            m.SetFloat("transmission_depth", 0);
        });
        float expected = 1 - DielectricE(0.3f, 1.5f);
        Assert.True(expected > 0.9f, $"glass passes {expected}");
        AssertNear(expected, lit, "clear glass");
    }

    [Fact]
    public void AThinWalledLeaf_LetsThroughHalfSOnePlusG()
    {
        const float g = 0.4f;
        float lit = LitInShadow(m =>
        {
            m.SetInt("geometry_thin_walled", 1);
            m.SetFloat("subsurface_weight", 1);
            m.SetColor("subsurface_color", new Float4(1, 1, 1, 1));
            m.SetFloat("subsurface_scatter_anisotropy", g);
        });
        AssertNear((1 - DielectricE(0.3f, 1.5f)) * 0.5f * (1 + g), lit, "thin walled leaf");
    }

    [Fact]
    public void AnOpaqueMetal_CastsAFullShadow()
        => AssertNear(0, LitInShadow(m => m.SetFloat("base_metalness", 1)), "opaque metal");

    [Fact]
    public void ATransmissiveMetal_CastsAFullShadow()
        => AssertNear(0, LitInShadow(m => { m.SetFloat("base_metalness", 1); m.SetFloat("transmission_weight", 1); }), "transmissive metal");

    private static readonly Action<Material>[] s_noShadowCasters =
    [
        _ => { },
        m => { m.Surface = new SurfaceSettings(SurfaceType.Transparent, Cull: SurfaceCull.Off); m.SetFloat("geometry_opacity", 0.5f); },
        m => m.SetFloat("transmission_weight", 0.5f),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CastersWithCastShadowsOff_CastNone(int caster)
        => AssertNear(1, LitInShadow(s_noShadowCasters[caster], castShadows: false), $"caster {caster}");

    // A view moved to another world whose sun casts at a lower resolution draws fresh cascades, so none of the old
    // world's casters linger in them
    [Fact]
    public void AViewMovedToAWorldWithSmallerCascades_KeepsNoneOfTheOldCasters()
    {
        AssertNear(0, LitInShadow(m => m.SetFloat("base_metalness", 1)), "opaque metal");

        using RenderWorld other = _renderer.CreateWorld();
        other.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default };
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        other.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);
        LightDesc sun = new()
        {
            Type = LightType.Directional,
            Direction = new Float3(s_sunDirection.X, s_sunDirection.Y, s_sunDirection.Z),
            Intensity = 0.1f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 256 },
        };
        other.AddLight(sun);

        RenderView view = _harness.BareView(1);
        view.World = other;
        view.EV100 = _ev100;
        view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = _origin });
        Float4[] pixels = _harness.Frame(view);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 p = PixelPosition(x, y);
                float lit = Reference.Directional(sun, p, s_normal, albedo, 0, 0.5f).X;
                float got = pixels[y * Size + x].X;
                Assert.True(MathF.Abs(got - lit) <= 2e-4f + 0.01f * lit, $"pixel {x},{y}: expected {lit}, got {got}");
            }
        }
    }

    // The dither sits on the light's texel grid in the world, so a camera moving sideways refits the cascade without
    // sliding the pattern. Inside the shadow the receiver changes only smoothly, by the shadow's distance fade, never
    // from one pixel to the next as a moved pattern would.
    [Fact]
    public void ADitheredShadow_StaysOnTheReceiver_AsTheCameraMoves()
    {
        AddReceiver(new Vector3(0.8f, 0.8f, 0.8f));
        _world.AddLight(new LightDesc
        {
            Type = LightType.Directional,
            Direction = new Float3(s_sunDirection.X, s_sunDirection.Y, s_sunDirection.Z),
            Intensity = 0.1f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 256, Cascades = 1 },
        });
        Material caster = PBR(m =>
        {
            m.Surface = new SurfaceSettings(SurfaceType.Transparent, Cull: SurfaceCull.Off);
            m.SetFloat("geometry_opacity", 0.5f);
        });
        _world.Add(new RenderableDesc
        {
            Mesh = FacingQuad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(-1, -1, 8)) * Double4x4.CreateScale(0.5),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        }, [caster]);
        Frame();
        Float4[] before = Frame();

        // Three pixels of 0.5 along x
        _origin += new Double3(1.5, 0, 0);
        Frame();
        Float4[] after = Frame();
        float Change(int x, int y) => after[y * Size + x - 3].X - before[y * Size + x].X;
        bool Inside(int x, int y) => SquareEdge(PixelPosition(x, y), 0, -0.5f, 2) <= -0.6f;
        float worst = 0, lit = before.Max(p => p.X);
        int pairs = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 3; x < Size - 1; x++)
            {
                if (!Inside(x, y) || !Inside(x + 1, y))
                    continue;
                pairs++;
                worst = MathF.Max(worst, MathF.Abs(Change(x + 1, y) - Change(x, y)));
            }
        }
        Assert.True(pairs > 4, $"{pairs} pixel pairs inside the shadow");
        Assert.True(worst <= 0.01f * lit, $"the change jumps by {worst} of {lit} between neighbouring pixels");
    }
}
