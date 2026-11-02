using System;
using System.Collections.Generic;

using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model's subsurface and thin walled surfaces on the GPU: the white furnace, a sheet lit through its back with
// shadows, back faces shading like front ones, and both against PbrReference under lights in front and behind.
public sealed partial class LightingTests
{
    private static readonly float[] s_bothSides = [0f, 50f, 130f];

    private static readonly Action<Material>[] s_whiteSubsurface =
    [
        m => { s_whiteBases[0](m); m.SetFloat("subsurface_weight", 1); m.SetColor("subsurface_color", new Float4(1, 1, 1, 1)); },
        m => { s_whiteBases[0](m); m.SetFloat("subsurface_weight", 0.6f); m.SetColor("subsurface_color", new Float4(1, 1, 1, 1)); m.SetFloat("subsurface_radius", 3); m.SetFloat("coat_weight", 0.7f); },
        m => { s_whiteBases[0](m); m.SetInt("geometry_thin_walled", 1); m.SetFloat("subsurface_weight", 1); m.SetColor("subsurface_color", new Float4(1, 1, 1, 1)); m.SetFloat("subsurface_scatter_anisotropy", 0.5f); },
        m =>
        {
            s_whiteBases[0](m);
            m.SetInt("geometry_thin_walled", 1);
            m.SetFloat("subsurface_weight", 0.7f);
            m.SetColor("subsurface_color", new Float4(1, 1, 1, 1));
            m.SetFloat("subsurface_scatter_anisotropy", -0.4f);
            m.SetFloat("coat_weight", 1);
            m.SetFloat("coat_roughness", 0.3f);
            m.SetFloat("fuzz_weight", 0.5f);
        },
        m => { s_whiteBases[1](m); m.SetFloat("base_metalness", 0.4f); m.SetInt("geometry_thin_walled", 1); m.SetFloat("subsurface_weight", 0.8f); m.SetColor("subsurface_color", new Float4(1, 1, 1, 1)); },
    ];

    [Fact]
    public void TheFurnace_WhiteSubsurfaceAndSheets_AreOneFromEitherSide()
    {
        for (int i = 0; i < s_whiteSubsurface.Length; i++)
            AssertFurnace($"subsurface {i}", s_whiteSubsurface[i], tilts: s_bothSides);
    }

    [Fact]
    public void TheFurnace_WhiteSubsurfaceAndSheets_AreOneAtTheLiteTier()
    {
        _world.Globals.Set(GlobalAxis.PbrTier, "LITE");
        for (int i = 0; i < s_whiteSubsurface.Length; i++)
            AssertFurnace($"lite subsurface {i}", s_whiteSubsurface[i], tilts: s_bothSides);
    }

    // A pure sheet of colour S and anisotropy g, all subsurface and no specular, so its pixels show the sheet alone
    private Material Sheet(Vector3 S, float g) => PBR(m =>
    {
        m.SetColor("base_color", new Float4(0, 0, 0, 1));
        m.SetFloat("specular_weight", 0);
        m.SetInt("geometry_thin_walled", 1);
        m.SetFloat("subsurface_weight", 1);
        m.SetColor("subsurface_color", new Float4(S.X, S.Y, S.Z, 1));
        m.SetFloat("subsurface_scatter_anisotropy", g);
    });

    // Lit from behind the sheet passes 0.5 S (1 + g) of the irradiance on its back toward the view, from the front it
    // reflects 0.5 S (1 - g), each a Lambert lobe at base_diffuse_roughness 0
    [Fact]
    public void ABackLitThinSheet_Transmits_HalfSOnePlusG()
    {
        Vector3 S = new(0.9f, 0.6f, 0.3f);
        const float g = 0.4f;
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        AddFacingQuad(Sheet(S, g));
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(0.5f, 0.25f, -1), Intensity = 2 };
        LightHandle light = _world.AddLight(sun);
        float cosine = 1 / new Vector3(0.5f, 0.25f, 1).Length();
        Frame();
        AssertMatches(_ => 0.5f * S * (1 + g) / MathF.PI * cosine * 2, Frame());

        _world.RemoveLight(light);
        _world.AddLight(sun with { Direction = new Float3(0.5f, 0.25f, 1) });
        Frame();
        AssertMatches(_ => 0.5f * S * (1 - g) / MathF.PI * cosine * 2, Frame());
    }

    // The sheet casts its own shadow and still passes the light from behind, its shadow offset turned toward the light,
    // while a caster further back shades it
    [Fact]
    public void ABackLitThinSheet_ShadowsOnlyWhereSomethingBehindItBlocksTheLight()
    {
        Vector3 S = new(0.8f, 0.8f, 0.5f);
        const float g = 0.2f;
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        _world.Add(new RenderableDesc
        {
            Mesh = FacingQuad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows,
        }, [Sheet(S, g)]);
        _world.AddLight(new LightDesc
        {
            Type = LightType.Directional,
            Direction = new Float3(0.5f, 0.25f, -1),
            Intensity = 2,
            Shadow = new ShadowDesc { Enabled = true },
        });
        float cosine = 1 / new Vector3(0.5f, 0.25f, 1).Length();

        // From depth 12 to 10 the light moves a unit in x and half in y
        AddCaster(-1, -1, 12, half: 2);
        Frame();
        AssertShadowed(_ => 0.5f * S * (1 + g) / MathF.PI * cosine * 2, p => SquareEdge(p, 0, -0.5f, 2), Frame(), 0.6f);
    }

    private static readonly PbrInputs[] s_subsurfaceInputs =
    [
        new(new Vector3(0.6f, 0.4f, 0.3f), Roughness: 0.45f) { SubsurfaceWeight = 0.7f, SubsurfaceColor = new Vector3(0.9f, 0.5f, 0.3f), SubsurfaceRadius = 0.8f, CoatWeight = 0.5f },
        new(new Vector3(0.3f, 0.5f, 0.2f), Roughness: 0.5f, DiffuseRoughness: 0.6f) { ThinWalled = true, SubsurfaceWeight = 1, SubsurfaceColor = new Vector3(0.5f, 0.8f, 0.3f), SubsurfaceAnisotropy = 0.4f },
        new(new Vector3(0.7f, 0.6f, 0.5f), Metalness: 0.2f, Roughness: 0.35f, DiffuseRoughness: 0.3f) { ThinWalled = true, SubsurfaceWeight = 0.6f, SubsurfaceColor = new Vector3(0.9f, 0.7f, 0.4f), SubsurfaceAnisotropy = -0.3f, CoatWeight = 0.8f, CoatColor = new Vector3(0.9f, 0.8f, 0.7f), CoatRoughness = 0.2f, FuzzWeight = 0.6f, FuzzRoughness = 0.5f },
        new(new Vector3(0.5f, 0.6f, 0.7f), Roughness: 0.3f) { ThinWalled = true },
    ];

    public static IEnumerable<object[]> SubsurfaceCases()
    {
        for (int i = 0; i < s_subsurfaceInputs.Length; i++)
        {
            yield return [i, false];
            if (s_subsurfaceInputs[i].ThinWalled)
                yield return [i, true];
        }
    }

    // Subsurface in bulk and thin sheets against the CPU closure under lights in front and behind. Seen from behind, a
    // thin walled quad culled Back by its surface still draws, its back face shading as a front face would.
    [Theory]
    [MemberData(nameof(SubsurfaceCases))]
    public void TheSubsurface_LightsAsTheReferenceSays(int index, bool backFace)
    {
        PbrInputs p = s_subsurfaceInputs[index];
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };
        Material material = PBR(m => SetInputs(m, p));
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Back);
        Double4x4 place = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth));
        _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = backFace ? place * Double4x4.RotateY(Math.PI) : place }, [material]);

        (Vector3 Position, Vector3 Color)[] points = [(new Vector3(1, 0.5f, 7), new Vector3(1, 0.9f, 0.8f)), (new Vector3(-1, 0.5f, 13), new Vector3(0.7f, 0.9f, 1))];
        (Vector3 Direction, Vector3 Color, float Intensity)[] suns = [(new Vector3(-0.3f, 0.2f, 1), new Vector3(0.9f, 0.95f, 1), 0.4f), (new Vector3(0.2f, -0.3f, -1), new Vector3(1, 0.8f, 0.6f), 0.3f)];
        foreach ((Vector3 position, Vector3 color) in points)
            _world.AddLight(Point(new Double3(position.X, position.Y, position.Z), 20, 3, new Float3(color.X, color.Y, color.Z)));
        foreach ((Vector3 direction, Vector3 color, float intensity) in suns)
            _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(direction.X, direction.Y, direction.Z), Color = new Float3(color.X, color.Y, color.Z), Intensity = intensity });

        Frame();
        AssertMatches(position =>
        {
            PbrReference c = new(p, s_normal, Vector3.Normalize(-position));
            Vector3 total = Vector3.Zero;
            foreach ((Vector3 light, Vector3 color) in points)
            {
                Vector3 toPoint = light - position;
                float d2 = toPoint.LengthSquared();
                float factor = d2 / 400;
                float window = Math.Clamp(1 - factor * factor, 0, 1);
                total += c.Shade(toPoint / MathF.Sqrt(d2)) * window * window / d2 * 3 * color;
            }
            foreach ((Vector3 direction, Vector3 color, float intensity) in suns)
                total += c.Shade(Vector3.Normalize(-direction)) * intensity * color;
            return total;
        }, Frame());
    }
}
