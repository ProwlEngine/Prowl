using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class GpuCullTests
{
    private const int Size = RendererHarness.Size;

    // A two unit square of one colour at a depth of its own, so each level's surface is told apart
    private static GpuMesh Quad(RendererHarness harness, Float4 color, float z, int split = 1)
    {
        List<Float3> positions = new();
        List<Float4> colors = new();
        List<uint> indices = new();
        for (int y = 0; y < split; y++)
        {
            for (int x = 0; x < split; x++)
            {
                float x0 = -1 + 2f * x / split, y0 = -1 + 2f * y / split, step = 2f / split;
                uint first = (uint)positions.Count;
                positions.AddRange([new(x0, y0, z), new(x0 + step, y0, z), new(x0 + step, y0 + step, z), new(x0, y0 + step, z)]);
                colors.AddRange([color, color, color, color]);
                indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }
        }
        return harness.Renderer.CreateMesh(new MeshData
        {
            VertexCount = (uint)positions.Count,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.ToArray().AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Color, Format = VertexElementFormat.Float4, Data = MemoryMarshal.AsBytes(colors.ToArray().AsSpan()).ToArray() },
            ],
            Indices = indices.ToArray(),
        });
    }

    // Renders a scene of renderables at every level, culled every way the CPU culls, over frames that cross fade
    private static List<Float4[]> Render(bool gpu)
    {
        using RendererHarness harness = new(gpuCulling: gpu);
        GpuMesh red = Quad(harness, new Float4(1, 0, 0, 1), 0), green = Quad(harness, new Float4(0, 1, 0, 1), -0.25f), blue = Quad(harness, new Float4(0, 0, 1, 1), -0.5f);
        GpuMesh chunked = Quad(harness, new Float4(1, 1, 0, 1), 0, split: 40);
        LodSet lods = new([new LodLevel(green, 0.2f), new LodLevel(blue, 0.1f)], 0.03f);
        Material material = harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("base_color", new Float4(1, 1, 1, 1));
        material.SetFloat("emission_luminance", 1f / RenderView.ExposureOf(0));
        harness.World.Lod.FadeSeconds = 1;

        RenderableHandle Add(GpuMesh mesh, double x, double y, float bias = 1, uint layer = 1, RenderableFlags flags = RenderableFlags.None)
            => harness.World.Add(new RenderableDesc
            {
                Mesh = mesh,
                Lods = mesh == red ? lods : null,
                LodBias = bias,
                LayerMask = layer,
                Flags = flags,
                Transform = Double4x4.CreateTranslation(new Double3(x, y, 10)) * Double4x4.CreateScale(0.8),
            }, [material]);

        RenderableHandle near = Add(red, -3, 3, 1), middle = Add(red, -1, 3, 0.5f), far = Add(red, 1, 3, 0.25f);
        Add(red, 3, 3, 0.05f);
        RenderableHandle forced = Add(red, -3, 1);
        harness.World.ForceLod(forced, 2);
        Add(red, -1, 1, layer: 2);
        Add(red, 1, 1, flags: RenderableFlags.Hidden);
        Add(red, 100, 1);
        Add(chunked, 3.5, -3);

        RenderView view = harness.View();
        view.CullingMask = 1;
        List<Float4[]> frames = new();
        Float4[] Frame(double time) => harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = time } });
        frames.Add(Frame(0));
        harness.World.SetLodBias(near, 0.5f);
        harness.World.SetLodBias(middle, 1);
        harness.World.SetLodBias(far, 0.02f);
        frames.Add(Frame(0));
        frames.Add(Frame(0.5));
        frames.Add(Frame(2));
        return frames;
    }

    [Fact]
    public void TheGpuCull_DrawsWhatTheCpuCullDraws_FadesIncluded()
    {
        List<Float4[]> cpu = Render(gpu: false), gpu = Render(gpu: true);
        for (int f = 0; f < cpu.Count; f++)
        {
            for (int i = 0; i < Size * Size; i++)
                Assert.True(cpu[f][i] == gpu[f][i], $"frame {f} pixel {i % Size},{i / Size}: cpu {cpu[f][i]} gpu {gpu[f][i]}");
        }

        // The scene is not empty and differs between the frames, so the comparison means something
        Assert.Contains(cpu[0], p => p.X > 0.5f && p.Y < 0.5f);
        Assert.NotEqual(cpu[0], cpu[3]);
    }
}
