using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
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

    // A large quad tilted about Y, filling the view, so its pixels see it from far off its normal
    private RenderableHandle AddTiltedQuad(Material material, float degrees)
        => _world.Add(new RenderableDesc
        {
            Mesh = FacingQuad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, 40)) * Double4x4.RotateY(degrees * Math.PI / 180) * Double4x4.CreateScale(20),
        }, [material]);

    private Shader? _wide;

    // Lit with the coat, fuzz, subsurface and anisotropy lobes all on at once, which no shipped surface model has, lit as
    // it is drawn
    private Shader Wide => _wide ??= LoadWide();

    private Shader LoadWide()
    {
        List<ShaderProperty> properties = new();
        foreach (string model in new[] { "ClearCoat", "Cloth", "Subsurface", "Anisotropic" })
            properties.AddRange(ShaderParser.ParseShadingModel(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{model}.shader")!).Properties);
        properties.AddRange(ShaderParser.ParseShadingModel("ShadingModel \"Extra\" { Properties { coat_roughness_anisotropy(\"Coat Roughness Anisotropy\", Float) = 0.0 [Range(0, 1)] coat_roughness_anisotropy_map(\"Coat Roughness Anisotropy Map\", Texture2D) = \"white\" {} [Map] } Base \"Lit\" }").Properties);
        _renderer.ShadingModels.Load(new ShadingModelDefinition("TestWide", "Lit", [.. properties], ["PBR_COAT", "PBR_FUZZ", "PBR_SUBSURFACE", "PBR_ANISOTROPY"], true, "", "", ""));
        return _renderer.LoadShader(ShaderParser.Parse("Shader \"Test/Wide\" { Surface \"TestWide\" { } }"));
    }

    private Material PBR(Action<Material> set, Shader? shader = null)
    {
        Material material = _renderer.CreateMaterial(shader ?? Wide);
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

    // FacingQuad with tangents, UV0 running 0 to 1 across it and UV1 twice UV0
    private GpuMesh TangentQuad => _tangentQuad ??= _renderer.CreateMesh(new MeshData
    {
        VertexCount = 4,
        Streams =
        [
            new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(new Float3[] { new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0) }.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(new Float3[] { new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1) }.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.Tangent, Format = VertexElementFormat.Float4, Data = MemoryMarshal.AsBytes(new Float4[] { new(1, 0, 0, -1), new(1, 0, 0, -1), new(1, 0, 0, -1), new(1, 0, 0, -1) }.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.UV0, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes(new Float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) }.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.UV1, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes(new Float2[] { new(0, 0), new(2, 0), new(2, 2), new(0, 2) }.AsSpan()).ToArray() },
        ],
        Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
    });

    private GpuMesh? _tangentQuad;

    // Standard Unlit glowing at one nit, so it shows its base colour as it is under the unit exposure
    private Material UnitUnlit()
    {
        Material material = _renderer.CreateMaterial(_renderer.Standard.Unlit);
        material.SetFloat("emission_luminance", 1);
        return material;
    }

    public static IEnumerable<object[]> ModelInputs()
    {
        yield return [new Float3(0.8f, 0.6f, 0.3f), 0f, 0.4f, 0.7f, 1f, new Float3(1, 1, 1), 1.5f];
        yield return [new Float3(0.5f, 0.7f, 0.9f), 0.3f, 0.25f, 0.2f, 0.6f, new Float3(1, 0.6f, 0.4f), 2.2f];
        yield return [new Float3(0.95f, 0.64f, 0.54f), 1f, 0.35f, 0f, 0.8f, new Float3(0.9f, 0.8f, 0.5f), 1.5f];
        yield return [new Float3(0.3f, 0.3f, 0.3f), 0f, 0.6f, 1f, 1f, new Float3(1, 1, 1), 0.8f];
    }
}
