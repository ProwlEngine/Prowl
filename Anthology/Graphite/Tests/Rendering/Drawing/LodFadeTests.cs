using System;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class LodFadeTests : IDisposable
{
    private const int Size = RendererHarness.Size;

    private readonly RendererHarness _harness = new();
    private readonly RenderableHandle _handle;

    public LodFadeTests()
    {
        GpuMesh red = Quad(new Float4(1, 0, 0, 1), 0);
        GpuMesh green = Quad(new Float4(0, 1, 0, 1), -0.5f);
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.Standard.Unlit);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("base_color", new Float4(1, 1, 1, 1));
        material.SetFloat("emission_luminance", 1f / RenderView.ExposureOf(0));
        _handle = _harness.World.Add(new RenderableDesc
        {
            Mesh = red,
            Lods = new LodSet([new LodLevel(green, 0.5f)]),
            Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5),
        }, [material]);
        _harness.World.Lod.FadeSeconds = 1;
    }

    public void Dispose() => _harness.Dispose();

    // An eight unit square of one colour, at a depth of its own so each level's surface is told apart
    private GpuMesh Quad(Float4 color, float z)
    {
        Float3[] positions = [new(-4, -4, z), new(4, -4, z), new(4, 4, z), new(-4, 4, z)];
        Float4[] colors = [color, color, color, color];
        return _harness.Renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Color, Format = VertexElementFormat.Float4, Data = MemoryMarshal.AsBytes(colors.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
        });
    }

    private Float4[] FrameAt(double time, RenderView view)
        => _harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = time } });

    // The eight by eight pixels the renderable covers
    private static Float4[] Covered(Float4[] pixels)
        => Enumerable.Range(0, Size * Size).Where(i => i % Size is >= 4 and < 12 && i / Size is >= 4 and < 12).Select(i => pixels[i]).ToArray();

    private static int Count(Float4[] pixels, Func<Float4, bool> where) => pixels.Count(where);

    private static bool Red(Float4 p) => p.X > 0.9f && p.Y < 0.1f;

    private static bool Green(Float4 p) => p.Y > 0.9f && p.X < 0.1f;

    [Fact]
    public void MidFade_EachPixelShowsOneLevel_HalfOfThemEach()
    {
        RenderView view = _harness.View();
        Assert.All(Covered(FrameAt(0, view)), p => Assert.True(Red(p)));
        _harness.World.SetLodBias(_handle, 0.2f);
        FrameAt(0, view);

        Float4[] covered = Covered(FrameAt(0.5, view));
        Assert.All(covered, p => Assert.True(Red(p) || Green(p)));
        Assert.InRange(Count(covered, Green), 24, 40);

        Assert.All(Covered(FrameAt(2, view)), p => Assert.True(Green(p)));
    }

    [Fact]
    public void APoppingView_SwitchesAtOnce()
    {
        RenderView view = _harness.View();
        view.LodTransition = LodTransition.Pop;
        FrameAt(0, view);
        _harness.World.SetLodBias(_handle, 0.2f);
        Assert.All(Covered(FrameAt(0.5, view)), p => Assert.True(Green(p)));
    }
}
