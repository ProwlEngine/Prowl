using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// The lighting program a deferred view draws is written from the renderer's shading models, and written anew as they change
[Collection("GPU Tests")]
public sealed class ShadingModelTests : IDisposable
{
    private const int Size = RendererHarness.Size;

    private readonly RendererHarness _harness = new();

    public ShadingModelTests()
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)];
        GpuMesh quad = _harness.Renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
        });
        _harness.Owned.Add(quad);
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.Standard.Lit);
        _harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [material]);
        _harness.World.AddLight(new LightDesc { Type = LightType.Point, Position = new Double3(0, 0, 8), Range = 10, Intensity = 1, Color = new Float3(1, 1, 1), Static = true });
    }

    public void Dispose() => _harness.Dispose();

    private RenderView Deferred()
    {
        RenderView view = _harness.View();
        view.Path = RenderPath.Deferred;
        return view;
    }

    private static Float4 Centre(Float4[] pixels) => pixels[Size / 2 * Size + Size / 2];

    // A light function giving a colour for each light, whatever the light. The view's ambient light comes on top.
    private static string Constant(string name, string color)
        => $"vec3 {name}(PBR_Closure c, Graphite_LightContribution l, vec4 custom) {{ return {color}; }}\n";

    [Fact]
    public void TheBuiltInModels_HaveTheIdsTheGBufferPassesWrite()
    {
        ShadingModels models = _harness.Renderer.ShadingModels;
        Assert.Equal(ShadingModels.DefaultLit, models.Find("DefaultLit")!.Id);
        Assert.Equal(ShadingModels.Unlit, models.Find("Unlit")!.Id);
        Assert.Equal(ShadingModels.TwoSidedFoliage, models.Find("TwoSidedFoliage")!.Id);
        Assert.Equal(4u, models.Register("Mine", Constant("Mine_Light", "vec3(0.0)"), "Mine_Light").Id);
        Assert.Equal(4u, models.Register("Mine", Constant("Mine_Light", "vec3(1.0)"), "Mine_Light").Id);
        Assert.True(models.Remove("Mine"));
        Assert.False(models.Remove("DefaultLit"));
    }

    [Fact]
    public void ReplacingAModel_RewritesTheLightingProgram_WhichLightsItsPixelsByIt()
    {
        ShadingModels models = _harness.Renderer.ShadingModels;
        Float4 lit = Centre(_harness.Frame(Deferred()));
        Assert.True(lit.X > 0.01f && MathF.Abs(lit.X - lit.Y) < 0.01f, $"default lit {lit}");

        FullscreenProgram first = models.Program;
        models.Register("DefaultLit", Constant("Green_Light", "vec3(0.0, 0.25, 0.0)"), "Green_Light");
        Float4 green = Centre(_harness.Frame(Deferred()));
        Assert.NotSame(first, models.Program);
        Assert.True(green.X < 0.03f && MathF.Abs(green.Y - 0.25f) < 0.03f && green.Z < 0.03f, $"green {green}");

        models.Register("DefaultLit", Constant("Blue_Light", "vec3(0.0, 0.0, 0.5)"), "Blue_Light");
        Float4 blue = Centre(_harness.Frame(Deferred()));
        Assert.True(blue.Y < 0.03f && MathF.Abs(blue.Z - 0.5f) < 0.03f, $"blue {blue}");
    }

    [Fact]
    public void AModelThatDoesNotCompile_IsReported_AndTheLastProgramStays()
    {
        ShadingModels models = _harness.Renderer.ShadingModels;
        models.Register("DefaultLit", Constant("Green_Light", "vec3(0.0, 0.25, 0.0)"), "Green_Light");
        _harness.Frame(Deferred());
        FullscreenProgram good = models.Program;

        models.Register("Broken", "vec3 Broken_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom) { return nothing; }\n", "Broken_Light");
        _harness.Submit(new FrameDesc { Views = new[] { Deferred() } });
        RendererError error = Assert.Single(_harness.Errors);
        Assert.Equal("shader.compile", error.Code);
        Assert.Same(good, models.Program);
        Float4 green = Centre(_harness.ReadTarget());
        Assert.True(MathF.Abs(green.Y - 0.25f) < 0.03f, $"green {green}");
    }
}
