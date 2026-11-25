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

}
