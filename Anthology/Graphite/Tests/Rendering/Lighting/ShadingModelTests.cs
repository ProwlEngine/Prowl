using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// A ShadingModel document is a surface model shaders build on, and the lighting program a deferred view draws is
// written from the renderer's models, and written anew as they change
[Collection("GPU Tests")]
public sealed class ShadingModelTests : IDisposable
{
    private const int Size = RendererHarness.Size;

    private readonly RendererHarness _harness = new();
    private readonly GpuMesh _quad;

    public ShadingModelTests()
    {
        _quad = Quad(_harness);
        _harness.World.AddLight(s_light);
    }

    private static readonly LightDesc s_light = new() { Type = LightType.Point, Position = new Double3(0, 0, 8), Range = 10, Intensity = 1, Color = new Float3(1, 1, 1), Static = true };

    private static GpuMesh Quad(RendererHarness harness)
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)];
        GpuMesh quad = harness.Renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
        });
        harness.Owned.Add(quad);
        return quad;
    }

    public void Dispose() => _harness.Dispose();

    private RenderView View(RenderPath path) => View(_harness, path);

    private static RenderView View(RendererHarness harness, RenderPath path)
    {
        RenderView view = harness.View();
        view.Path = path;
        return view;
    }

    private static Float4 Centre(Float4[] pixels) => pixels[Size / 2 * Size + Size / 2];

    // A model lit by a colour for each light, whatever the light, times the first value its Pack keeps. The view's
    // ambient light comes on top.
    private static string Flat(string name, string color) => $$"""
        ShadingModel "{{name}}"
        {
            Properties
            {
                flat_scale("Scale", Float) = 1.0
            }

            Base "Lit"

            Pack
            {
                GLSLPROGRAM
                uniform float flat_scale;
                vec4 Model_Pack(PBR_Params p) { return vec4(flat_scale, 0.0, 0.0, 0.0); }
                ENDGLSL
            }

            Lighting
            {
                GLSLPROGRAM
                vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom) { return {{color}} * custom.x; }
                ENDGLSL
            }
        }
        """;

    private Material On(string model) => On(_harness, _quad, model);

    private static Material On(RendererHarness harness, GpuMesh quad, string model)
    {
        Shader shader = harness.Renderer.LoadShader(ShaderParser.Parse($"Shader \"Test/{model}\"\n{{\n    Surface \"{model}\"\n    {{\n    }}\n}}\n"));
        Material material = harness.Renderer.CreateMaterial(shader);
        harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [material]);
        return material;
    }

    [Fact]
    public void ShippedModels_HaveIds_AndAForwardOnlyOneHasNone()
    {
        ShadingModels models = _harness.Renderer.ShadingModels;
        Assert.Equal(1u, models.Find("Lit")!.Id);
        Assert.Equal(2u, models.Find("Unlit")!.Id);
        Assert.Equal(3u, models.Find("Foliage")!.Id);
        foreach (string name in new[] { "ClearCoat", "Cloth", "Subsurface", "Hair", "Eye" })
            Assert.InRange(models.Find(name)!.Id, 4u, 15u);

        Assert.True(_harness.Renderer.Standard.Anisotropic.HasPass(PassType.Forward));
        Assert.False(_harness.Renderer.Standard.Anisotropic.HasPass(PassType.GBuffer));
        Assert.Null(models.Find("Anisotropic"));
        Assert.True(_harness.Renderer.Standard.ClearCoat.HasPass(PassType.GBuffer));
        Assert.True(_harness.Renderer.Standard.ClearCoat.FindProperty("coat_weight") >= 0);
        Assert.True(_harness.Renderer.Standard.Lit.FindProperty("coat_weight") < 0);
    }

    [Theory]
    [InlineData(RenderPath.Forward)]
    [InlineData(RenderPath.Deferred)]
    public void AModelsLighting_LightsItsSurfaces_ByWhatItsPackKeeps(RenderPath path)
    {
        _harness.Renderer.LoadShadingModel(Flat("FlatGreen", "vec3(0.0, 0.5, 0.0)"));
        Material material = On("FlatGreen");
        Float4 full = Centre(_harness.Frame(View(path)));
        Assert.True(full.X < 0.03f && MathF.Abs(full.Y - 0.5f) < 0.03f && full.Z < 0.03f, $"full {full}");

        material.SetFloat("flat_scale", 0.5f);
        Float4 half = Centre(_harness.Frame(View(path)));
        Assert.True(MathF.Abs(half.Y - 0.25f) < 0.03f, $"half {half}");
    }

    [Fact]
    public void LoadingAModelAgain_RewritesTheLightingProgram_AndKeepsItsId()
    {
        ShadingModels models = _harness.Renderer.ShadingModels;
        ShadingModel first = _harness.Renderer.LoadShadingModel(Flat("FlatSwap", "vec3(0.0, 0.5, 0.0)"))!;
        On("FlatSwap");
        Float4 green = Centre(_harness.Frame(View(RenderPath.Deferred)));
        Assert.True(MathF.Abs(green.Y - 0.5f) < 0.03f && green.Z < 0.03f, $"green {green}");

        FullscreenProgram program = models.Program;
        ShadingModel second = _harness.Renderer.LoadShadingModel(Flat("FlatSwap", "vec3(0.0, 0.0, 0.5)"))!;
        Assert.Equal(first.Id, second.Id);
        Float4 blue = Centre(_harness.Frame(View(RenderPath.Deferred)));
        Assert.NotSame(program, models.Program);
        Assert.True(blue.Y < 0.03f && MathF.Abs(blue.Z - 0.5f) < 0.03f, $"blue {blue}");
    }

    [Fact]
    public void AModelWhoseLightingDoesNotCompile_IsReported_AndTheLastProgramStays()
    {
        ShadingModels models = _harness.Renderer.ShadingModels;
        _harness.Renderer.LoadShadingModel(Flat("FlatKept", "vec3(0.0, 0.5, 0.0)"));
        On("FlatKept");
        _harness.Frame(View(RenderPath.Deferred));
        FullscreenProgram good = models.Program;

        _harness.Renderer.LoadShadingModel(Flat("FlatBroken", "nothing"));
        _harness.Submit(new FrameDesc { Views = new[] { View(RenderPath.Deferred) } });
        RendererError error = Assert.Single(_harness.Errors);
        Assert.Equal("shader.compile", error.Code);
        Assert.Same(good, models.Program);
        Float4 green = Centre(_harness.ReadTarget());
        Assert.True(MathF.Abs(green.Y - 0.5f) < 0.03f, $"green {green}");
    }

    [Fact]
    public void AfterAModelThatDoesNotCompile_AnotherStillLoadsAndLights()
    {
        _harness.Renderer.LoadShadingModel(Flat("FlatBroken", "nothing"));
        _harness.Submit(new FrameDesc { Views = new[] { View(RenderPath.Deferred) } });
        Assert.Single(_harness.Errors);

        _harness.Renderer.LoadShadingModel(Flat("FlatKept", "vec3(0.0, 0.5, 0.0)"));
        On("FlatKept");
        _harness.Submit(new FrameDesc { Views = new[] { View(RenderPath.Deferred) } });
        Assert.Single(_harness.Errors);
        Float4 green = Centre(_harness.ReadTarget());
        Assert.True(green.X < 0.03f && MathF.Abs(green.Y - 0.5f) < 0.03f, $"green {green}");
    }

    [Fact]
    public void AModelAnotherRendererLoaded_LightsTheShadersBuiltOnItHere()
    {
        _harness.Renderer.LoadShadingModel(Flat("FlatGreen", "vec3(0.0, 0.5, 0.0)"));
        using RendererHarness other = new();
        other.World.AddLight(s_light);
        Assert.Null(other.Renderer.ShadingModels.Find("FlatGreen"));

        On(other, Quad(other), "FlatGreen");
        Assert.NotNull(other.Renderer.ShadingModels.Find("FlatGreen"));
        Float4 green = Centre(other.Frame(View(other, RenderPath.Deferred)));
        Assert.True(green.X < 0.03f && MathF.Abs(green.Y - 0.5f) < 0.03f, $"green {green}");
    }

    [Fact]
    public void ASurfaceOnAHighLayer_IsLitTheSameOnBothPaths()
    {
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.Standard.Lit);
        _harness.World.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), LayerMask = 1u << 5 }, [material]);
        Float4 forward = Centre(_harness.Frame(View(RenderPath.Forward)));
        Float4 deferred = Centre(_harness.Frame(View(RenderPath.Deferred)));
        Assert.True(forward.X > 0.02f, $"forward {forward}");
        Assert.True(MathF.Abs(forward.X - deferred.X) < 0.01f + 0.04f * forward.X, $"forward {forward}, deferred {deferred}");
    }

    // Light in three bands by how far the surface faces it, green
    private const string ToonLight = "float lit = dot(c.normal, l.diffuseL) * 0.5 + 0.5; return vec3(0.0, 0.5, 0.0) * (floor(lit * 3.0 + 0.5) / 3.0);";

    private const string ToonShader = $$"""
        Shader "Test/ToonShader"
        {
            Surface "Lit"
            {
                Lighting
                {
                    GLSLPROGRAM
                    vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l) { {{ToonLight}} }
                    ENDGLSL
                }
            }
        }
        """;

    private const string ToonModel = $$"""
        ShadingModel "TestToon"
        {
            Base "Lit"

            Lighting
            {
                GLSLPROGRAM
                vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom) { {{ToonLight}} }
                ENDGLSL
            }
        }
        """;

    private Float4 Lit(Shader shader, RenderPath path)
    {
        RenderableHandle handle = _harness.World.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [_harness.Renderer.CreateMaterial(shader)]);
        Float4 centre = Centre(_harness.Frame(View(path)));
        _harness.World.Remove(handle);
        return centre;
    }

    // A toon look written twice: as one shader's own Lighting block, which a GBuffer cannot keep, and as a model
    [Theory]
    [InlineData(RenderPath.Forward)]
    [InlineData(RenderPath.Deferred)]
    public void AToonLook_IsTheSame_AsAForwardOnlyShaderAndAsAModel(RenderPath path)
    {
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(ToonShader));
        Assert.True(shader.HasPass(PassType.Forward));
        Assert.False(shader.HasPass(PassType.GBuffer));

        _harness.Renderer.LoadShadingModel(ToonModel);
        Shader model = _harness.Renderer.LoadShader(ShaderParser.Parse("Shader \"Test/ToonModel\" { Surface \"TestToon\" { } }"));
        Assert.True(model.HasPass(PassType.GBuffer));

        Float4 own = Lit(shader, path);
        Float4 shared = Lit(model, path);
        Assert.True(own.X < 0.03f && MathF.Abs(own.Y - 0.5f) < 0.03f && own.Z < 0.03f, $"shader {own}");
        Assert.True(MathF.Abs(own.Y - shared.Y) < 0.02f && MathF.Abs(own.X - shared.X) < 0.02f, $"shader {own} model {shared}");
    }

    [Fact]
    public void ASurfaceMarkedForward_TakesNoGBufferPass_AndLooksTheSameInADeferredView()
    {
        Shader forward = _harness.Renderer.LoadShader(ShaderParser.Parse("Shader \"Test/ForwardLit\" { Surface \"Lit\" Forward { } }"));
        Assert.True(forward.HasPass(PassType.Forward));
        Assert.False(forward.HasPass(PassType.GBuffer));
        Assert.True(_harness.Renderer.Standard.Lit.HasPass(PassType.GBuffer));

        Float4 drawn = Lit(forward, RenderPath.Deferred);
        Float4 kept = Lit(_harness.Renderer.Standard.Lit, RenderPath.Deferred);
        Assert.True(drawn.X > 0.01f, $"lit {drawn}");
        for (int c = 0; c < 3; c++)
            Assert.True(MathF.Abs(drawn[c] - kept[c]) < 0.01f + 0.02f * kept[c], $"channel {c}: forward {drawn[c]}, deferred {kept[c]}");
    }

    // Each shipped model lights its surface the same whether the view keeps it in a GBuffer or lights it as it draws
    [Theory]
    [InlineData("ClearCoat", "coat_weight")]
    [InlineData("Cloth", "fuzz_weight")]
    [InlineData("Subsurface", "subsurface_weight")]
    [InlineData("Hair", "hair_scatter")]
    [InlineData("Eye", "iris_concavity")]
    public void AShippedModel_LooksTheSameInBothPaths(string model, string weight)
    {
        BuiltInStandard standard = _harness.Renderer.Standard;
        Shader shader = model switch
        {
            "ClearCoat" => standard.ClearCoat,
            "Cloth" => standard.Cloth,
            "Subsurface" => standard.Subsurface,
            "Hair" => standard.Hair,
            _ => standard.Eye,
        };
        Material material = _harness.Renderer.CreateMaterial(shader);
        material.SetColor("base_color", new Float4(0.6f, 0.3f, 0.2f, 1));
        material.SetFloat(weight, 0.8f);
        _harness.World.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [material]);

        Float4[] forward = _harness.Frame(View(RenderPath.Forward));
        Float4[] deferred = _harness.Frame(View(RenderPath.Deferred));
        Assert.True(Centre(forward).X > 0.005f, $"{model} is lit: {Centre(forward)}");
        for (int i = 0; i < forward.Length; i++)
        {
            for (int c = 0; c < 3; c++)
                Assert.True(MathF.Abs(forward[i][c] - deferred[i][c]) < 0.01f + 0.04f * forward[i][c], $"{model} pixel {i} channel {c}: forward {forward[i][c]}, deferred {deferred[i][c]}");
        }
    }

    [Fact]
    public void ADocument_NeedsABase_AndCannotTakeAShippedSurfaceModelsName()
    {
        Assert.ThrowsAny<Exception>(() => ShaderParser.ParseShadingModel("ShadingModel \"NoBase\"\n{\n}\n"));
        Assert.Throws<ArgumentException>(() => _harness.Renderer.LoadShadingModel("ShadingModel \"Lit\"\n{\n    Base \"Unlit\"\n}\n"));
        Assert.Throws<ArgumentException>(() => _harness.Renderer.LoadShadingModel("ShadingModel \"OnAModel\"\n{\n    Base \"ClearCoat\"\n}\n"));
        Assert.Throws<ArgumentException>(() => _harness.Renderer.LoadShadingModel("ShadingModel \"OnNothing\"\n{\n    Base \"NoSuchModel\"\n}\n"));
    }
}
