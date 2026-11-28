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

    // A red quad behind a white wall that covers the view, then the wall moved out of view. Returns each frame's picture
    // and whether the red quad counted as in view after it.
    private static List<(Float4[] Picture, bool RedInView)> RenderBehindWall(bool occlusion)
    {
        using RendererHarness harness = new();
        harness.Renderer.OcclusionCulling = occlusion;
        GpuMesh quad = harness.CreateQuad();
        Material Unlit(Float4 color)
        {
            Material material = harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit);
            material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
            material.SetColor("base_color", color);
            material.SetFloat("emission_luminance", 1f / RenderView.ExposureOf(0));
            return material;
        }
        RenderableHandle wall = harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) * Double4x4.CreateScale(1.5) }, [Unlit(new Float4(1, 1, 1, 1))]);
        RenderableHandle red = harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.25) }, [Unlit(new Float4(1, 0, 0, 1))]);

        RenderView view = harness.View();
        List<(Float4[], bool)> frames = new();
        void Frame()
        {
            Float4[] picture = harness.Frame(view);
            bool inView = true;
            if (harness.Renderer.Frame.Camera(view)?.Occlusion is { } occlusion)
            {
                DeviceBuffer staging = harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(occlusion.SizeInBytes, BufferUsage.Staging));
                harness.Device.Record(cmd => cmd.CopyBuffer(occlusion, 0, staging, 0, occlusion.SizeInBytes)).Wait();
                inView = (harness.Device.Map<uint>(staging).ToArray()[red.Index] & 0x80000000u) != 0;
                staging.Dispose();
            }
            frames.Add((picture, inView));
        }
        Frame();
        Frame();
        Frame();
        harness.World.SetTransform(wall, Double4x4.CreateTranslation(new Double3(100, 0, 5)) * Double4x4.CreateScale(1.5));
        Frame();
        return frames;
    }

    [Fact]
    public void OcclusionCulling_SkipsWhatIsHidden_AndDrawsItTheFrameItShows()
    {
        List<(Float4[] Picture, bool RedInView)> culled = RenderBehindWall(occlusion: true), all = RenderBehindWall(occlusion: false);
        for (int f = 0; f < all.Count; f++)
        {
            for (int i = 0; i < Size * Size; i++)
                Assert.True(all[f].Picture[i] == culled[f].Picture[i], $"frame {f} pixel {i % Size},{i / Size}: without {all[f].Picture[i]} with {culled[f].Picture[i]}");
        }

        // Hidden once the wall's depth is known, back in view the frame the wall leaves
        Assert.False(culled[1].RedInView);
        Assert.False(culled[2].RedInView);
        Assert.True(culled[3].RedInView);
        Assert.Contains(culled[3].Picture, p => p.X > 0.5f && p.Y < 0.5f);
        Assert.DoesNotContain(culled[2].Picture, p => p.X > 0.5f && p.Y < 0.5f);
    }

    // A red quad of two chunks across the view, the lower one behind a white wall, then the wall moved out of view
    private static List<Float4[]> RenderChunkBehindWall(bool occlusion)
    {
        using RendererHarness harness = new();
        harness.Renderer.OcclusionCulling = occlusion;
        GpuMesh chunked = Quad(harness, new Float4(1, 0, 0, 1), 0, split: 40);
        Material Unlit()
        {
            Material material = harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit);
            material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
            material.SetColor("base_color", new Float4(1, 1, 1, 1));
            material.SetFloat("emission_luminance", 1f / RenderView.ExposureOf(0));
            return material;
        }
        RenderableHandle wall = harness.World.Add(new RenderableDesc { Mesh = harness.CreateQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, -2.5, 5)) }, [Unlit()]);
        harness.World.Add(new RenderableDesc { Mesh = chunked, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(4) }, [Unlit()]);

        RenderView view = harness.View();
        List<Float4[]> frames = [harness.Frame(view), harness.Frame(view), harness.Frame(view)];
        harness.World.SetTransform(wall, Double4x4.CreateTranslation(new Double3(100, 0, 5)));
        frames.Add(harness.Frame(view));
        frames.Add(harness.Frame(view));
        return frames;
    }

    [Fact]
    public void OcclusionCulling_OfAMeshOfSeveralChunks_DrawsWhatTheUnculledViewDraws()
    {
        List<Float4[]> culled = RenderChunkBehindWall(occlusion: true), all = RenderChunkBehindWall(occlusion: false);
        for (int f = 0; f < all.Count; f++)
        {
            for (int i = 0; i < Size * Size; i++)
                Assert.True(all[f][i] == culled[f][i], $"frame {f} pixel {i % Size},{i / Size}: without {all[f][i]} with {culled[f][i]}");
        }

        // The wall hides the lower part, which shows the frame the wall leaves
        Assert.Contains(culled[2], p => p.X > 0.5f && p.Y > 0.5f);
        Assert.NotEqual(culled[2], culled[3]);
        Assert.DoesNotContain(culled[3], p => p.X > 0.5f && p.Y > 0.5f);
    }

    [Fact]
    public void AViewThatTurnsOcclusionCullingOff_DoesNotCullWhatIsHidden()
    {
        using RendererHarness harness = new();
        harness.World.Add(new RenderableDesc { Mesh = harness.CreateQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) },
            [harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit)]);
        RenderView view = harness.View();
        harness.Frame(view);
        Assert.NotNull(harness.Renderer.Frame.Camera(view)!.Occlusion);

        view.OcclusionCulling = false;
        harness.Frame(view);
        Assert.Null(harness.Renderer.Frame.Camera(view)!.Occlusion);
    }

    [Fact]
    public void AViewWithAFeatureDrawingBeforeTheLatePrepass_DoesNotCullWhatIsHidden()
    {
        using RendererHarness harness = new();
        harness.World.Add(new RenderableDesc { Mesh = harness.CreateQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) },
            [harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit)]);
        RenderView view = harness.View();
        view.Features = new ExtensionSet<RenderFeature> { new DrawPassFeature(PassType.Forward, RenderSlot.Culling) };
        harness.Frame(view);
        Assert.Null(harness.Renderer.Frame.Camera(view)!.Occlusion);
    }

    [Fact]
    public void FreezeCulling_TurnedOnWithOcclusionCulling_DrawsWhatIsInView()
    {
        using RendererHarness harness = new();
        harness.Renderer.OcclusionCulling = false;
        Material material = harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("base_color", new Float4(1, 0, 0, 1));
        material.SetFloat("emission_luminance", 1f / RenderView.ExposureOf(0));
        harness.World.Add(new RenderableDesc { Mesh = harness.CreateQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.25) }, [material]);
        RenderView view = harness.View();
        static bool Red(Float4[] picture) => Array.Exists(picture, p => p.X > 0.5f && p.Y < 0.5f);

        Assert.True(Red(harness.Frame(view)));
        harness.Renderer.OcclusionCulling = true;
        harness.Renderer.FreezeCulling = true;
        Assert.True(Red(harness.Frame(view)));
        Assert.True(Red(harness.Frame(view)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FreezeCulling_KeepsWhatWasInView_AsTheCameraMovesOn(bool occlusion)
    {
        using RendererHarness harness = new();
        harness.Renderer.OcclusionCulling = occlusion;
        GpuMesh quad = harness.CreateQuad();
        Material material = harness.Renderer.CreateMaterial(harness.Renderer.Standard.Unlit);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("base_color", new Float4(1, 0, 0, 1));
        material.SetFloat("emission_luminance", 1f / RenderView.ExposureOf(0));
        harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.25) }, [material]);
        harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(100, 0, 10)) * Double4x4.CreateScale(0.25) }, [material]);

        RenderView view = harness.View();
        Float4[] At(double x)
        {
            view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = new Double3(x, 0, 0) });
            return harness.Frame(view);
        }
        static bool Red(Float4[] picture) => Array.Exists(picture, p => p.X > 0.5f && p.Y < 0.5f);

        Assert.True(Red(At(0)));
        Assert.True(Red(At(0)));
        harness.Renderer.FreezeCulling = true;
        Assert.True(Red(At(0)));
        Assert.False(Red(At(100)));
        Assert.False(Red(At(100)));
        harness.Renderer.FreezeCulling = false;
        Assert.True(Red(At(100)));
    }

}
