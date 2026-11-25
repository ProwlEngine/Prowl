using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// A deferred view lays the world's decals over its GBuffer, where a forward view applies them as it draws
[Collection("GPU Tests")]
public sealed class DeferredDecalTests : IDisposable
{
    private const int Size = RendererHarness.Size;

    private readonly RendererHarness _harness = new();

    public DeferredDecalTests()
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
        material.SetColor("base_color", new Float4(1, 1, 1, 1));
        material.SetFloat("specular_weight", 0);
        _harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [material]);
        _harness.World.Environment = new EnvironmentDesc { AmbientColor = new Float4(1, 1, 1, 1), Fog = default };
    }

    public void Dispose() => _harness.Dispose();

    private Float4[] Frame(RenderPath path)
    {
        RenderView view = _harness.View();
        view.Path = path;
        return _harness.Frame(view);
    }

    private static Float4 At(Float4[] pixels, int x) => pixels[Size / 2 * Size + x];

    [Fact]
    public void ADecal_ColoursTheSurfaceInsideItsBox_TheSameInBothPaths()
    {
        Float4[] plain = Frame(RenderPath.Deferred);
        Assert.True(MathF.Abs(At(plain, 6).X - At(plain, 6).Y) < 0.02f, $"undecaled {At(plain, 6)}");

        _harness.World.AddDecal(new DecalDesc
        {
            Transform = Double4x4.CreateTranslation(new Double3(-2, 0, 10)) * Double4x4.CreateScale(4, 8, -2),
            Color = new Float4(1, 0, 0, 1),
        });
        Float4[] forward = Frame(RenderPath.Forward);
        Float4[] deferred = Frame(RenderPath.Deferred);

        Float4 left = At(deferred, 6), right = At(deferred, 9);
        Assert.True(left.X > 0.1f && left.Y < 0.2f * left.X, $"inside the decal {left}");
        Assert.True(MathF.Abs(right.X - right.Y) < 0.02f && right.Y > 0.1f, $"outside the decal {right}");
        for (int x = 5; x < 11; x++)
        {
            for (int c = 0; c < 3; c++)
                Assert.True(MathF.Abs(At(forward, x)[c] - At(deferred, x)[c]) < 0.02f + 0.03f * At(forward, x)[c], $"pixel {x} channel {c}: forward {At(forward, x)[c]}, deferred {At(deferred, x)[c]}");
        }
    }

    [Fact]
    public void ADecalOutOfView_AddsNoDecalPassToTheView_AndLeavesItsImageAlone()
    {
        RenderView view = _harness.View();
        view.Path = RenderPath.Deferred;
        view.EarlyDepth = EarlyDepth.None;
        Float4[] plain = _harness.Frame(view);

        _harness.World.AddDecal(new DecalDesc { Transform = Double4x4.CreateTranslation(new Double3(0, 0, -500)) * Double4x4.CreateScale(4, 4, -2), Color = new Float4(1, 0, 0, 1) });
        Float4[] behind = _harness.Frame(view);
        Assert.False(_harness.Renderer.Frame.Camera(view)!.Info.HasDecals);
        for (int x = 5; x < 11; x++)
            Assert.True(MathF.Abs(At(plain, x).X - At(behind, x).X) < 0.01f, $"pixel {x}: {At(plain, x)} against {At(behind, x)}");

        _harness.World.AddDecal(new DecalDesc { Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(4, 4, -2), Color = new Float4(1, 0, 0, 1) });
        Float4 red = At(_harness.Frame(view), 8);
        Assert.True(_harness.Renderer.Frame.Camera(view)!.Info.HasDecals);
        Assert.True(red.Y < 0.2f * red.X, $"inside the decal {red}");
    }

    [Fact]
    public void WithoutDecals_TheViewHasNoDecalPass()
    {
        RenderView view = _harness.View();
        view.Path = RenderPath.Deferred;
        _harness.Frame(view);
        Assert.DoesNotContain(_harness.Renderer.Frame.Camera(view)!.Selected, feature => feature is GBufferDecalsFeature);

        DecalHandle decal = _harness.World.AddDecal(new DecalDesc { Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(4, 4, -2), Color = new Float4(1, 0, 0, 1) });
        _harness.Frame(view);
        Assert.Contains(_harness.Renderer.Frame.Camera(view)!.Selected, feature => feature is GBufferDecalsFeature);

        _harness.World.RemoveDecal(decal);
        _harness.Frame(view);
        Assert.DoesNotContain(_harness.Renderer.Frame.Camera(view)!.Selected, feature => feature is GBufferDecalsFeature);
    }
}
