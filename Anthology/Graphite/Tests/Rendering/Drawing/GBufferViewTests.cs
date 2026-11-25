using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// A deferred view's debug views show what its GBuffer keeps, in place of a debug pass in every shader
[Collection("GPU Tests")]
public sealed class GBufferViewTests : IDisposable
{
    private const int Size = RendererHarness.Size;

    private readonly RendererHarness _harness = new();
    private readonly GBufferDebug _debug;

    public GBufferViewTests()
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
        material.SetColor("base_color", new Float4(0, 1, 0, 1));
        material.SetFloat("base_metalness", 1);
        material.SetFloat("specular_roughness", 0.25f);
        _harness.World.Add(new RenderableDesc { Mesh = quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [material]);
        _debug = new GBufferDebug(_harness.Renderer);
        _harness.World.Features.Add(_debug);
    }

    public void Dispose() => _harness.Dispose();

    private RenderView View(RenderPath path)
    {
        RenderView view = _harness.View();
        view.Path = path;
        return view;
    }

    [Theory]
    [InlineData(GBufferDebugMode.BaseColor, 0f, 1f, 0f)]
    [InlineData(GBufferDebugMode.Metalness, 1f, 1f, 1f)]
    [InlineData(GBufferDebugMode.Normals, 0.5f, 0.5f, 0f)]
    [InlineData(GBufferDebugMode.Roughness, 0.25f, 0.25f, 0.25f)]
    [InlineData(GBufferDebugMode.Occlusion, 1f, 1f, 1f)]
    public void AMode_ShowsThatPartOfTheGBuffer_AndBlackWhereItHoldsNothing(GBufferDebugMode mode, float r, float g, float b)
    {
        _debug.Mode = mode;
        Float4[] pixels = _harness.Frame(View(RenderPath.Deferred));
        Float4 centre = pixels[Size / 2 * Size + Size / 2], corner = pixels[0];
        Assert.True(MathF.Abs(centre.X - r) < 0.02f && MathF.Abs(centre.Y - g) < 0.02f && MathF.Abs(centre.Z - b) < 0.02f, $"{mode}: {centre}");
        Assert.True(corner.X + corner.Y + corner.Z < 0.01f, $"{mode} off the surface: {corner}");
    }

    [Fact]
    public void AForwardView_SkipsIt_AndSaysSo()
    {
        RenderView forward = View(RenderPath.Forward);
        Assert.Contains(_debug, forward.SkippedFeatures());
        Float4 lit = _harness.Frame(forward)[Size / 2 * Size + Size / 2];

        _debug.Enabled = false;
        Float4 plain = _harness.Frame(forward)[Size / 2 * Size + Size / 2];
        Assert.Equal(plain, lit);
        _debug.Enabled = true;

        Assert.DoesNotContain(_debug, View(RenderPath.Deferred).SkippedFeatures());
    }
}
