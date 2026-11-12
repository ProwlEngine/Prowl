using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

[CollectionDefinition("GPU Tests", DisableParallelization = true)]
public sealed class GpuTestCollection { }

[Collection("GPU Tests")]
public sealed class ImpostorBuilderTests : IDisposable
{
    private static readonly GlslShaderCompiler s_compiler = new();

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly Renderer _renderer;

    // A one unit square at z 0 facing +Z, its quarters red, green, blue and yellow
    private static readonly Float3[] s_quarterColors = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 0)];

    public ImpostorBuilderTests()
    {
        _renderer = new Renderer(_device, s_compiler, new RendererOptions { Threading = RenderThreading.Inline, Compile = CompilePolicy.Sync, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _device.Dispose();
    }

    // Quarter q spans x from -0.5 or 0 and y from 0 or -0.5: -x +y, +x +y, -x -y, +x -y
    private (GpuMesh Mesh, Material[] Materials) Square()
    {
        List<Float3> positions = new(), normals = new();
        List<uint> indices = new();
        SubMesh[] subs = new SubMesh[4];
        for (int q = 0; q < 4; q++)
        {
            float x0 = q % 2 == 0 ? -0.5f : 0, y0 = q < 2 ? 0 : -0.5f;
            uint first = (uint)positions.Count;
            positions.AddRange([new(x0, y0, 0), new(x0 + 0.5f, y0, 0), new(x0 + 0.5f, y0 + 0.5f, 0), new(x0, y0 + 0.5f, 0)]);
            normals.AddRange([new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)]);
            subs[q] = new SubMesh { IndexStart = (uint)indices.Count, IndexCount = 6 };
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }
        GpuMesh mesh = _renderer.CreateMesh(new MeshData
        {
            VertexCount = (uint)positions.Count,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.ToArray().AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.ToArray().AsSpan()).ToArray() },
            ],
            Indices = indices.ToArray(),
            SubMeshes = subs,
        });
        Material[] materials = new Material[4];
        for (int q = 0; q < 4; q++)
        {
            materials[q] = _renderer.CreateMaterial(_renderer.Standard.Lit);
            materials[q].Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
            materials[q].SetColor("base_color", new Float4(s_quarterColors[q], 1));
        }
        return (mesh, materials);
    }

    private static Float4 Texel(ImpostorData data, byte[] atlas, int frameX, int frameY, float u, float v)
    {
        int x = frameX * data.CellSize + (int)(u * data.CellSize), y = frameY * data.CellSize + (int)(v * data.CellSize);
        int at = (y * data.AtlasSize + x) * 4;
        return new Float4(atlas[at], atlas[at + 1], atlas[at + 2], atlas[at + 3]) / 255f;
    }

    [Fact]
    public void TheFrameFromPlusZ_SeesTheSquareTheWayItsBasisSays()
    {
        (GpuMesh mesh, Material[] materials) = Square();
        ImpostorData data = ImpostorBuilder.Bake(_renderer, mesh, materials, new ImpostorBakeDesc { Frames = 5, CellSize = 32, Layout = ImpostorLayout.Hemi });

        Assert.Equal(160, data.AtlasSize);
        Assert.Equal(new Float3(0, 0, 1), ImpostorFrames.Direction(ImpostorLayout.Hemi, 5, 4, 0));
        (Float3 right, Float3 up) = ImpostorFrames.Basis(new Float3(0, 0, 1));
        Assert.True(MathF.Abs(up.Y - 1) < 1e-5f);

        // A texel's u runs along the frame's right and v along its up, each from minus the radius to plus it
        (float U, float V)[] spots = [(0.3f, 0.3f), (0.7f, 0.3f), (0.3f, 0.7f), (0.7f, 0.7f)];
        for (int s = 0; s < 4; s++)
        {
            Float3 point = right * (spots[s].U - 0.5f) + up * (spots[s].V - 0.5f);
            int quarter = (point.X > 0 ? 1 : 0) + (point.Y > 0 ? 0 : 2);
            Float4 albedo = Texel(data, data.Albedo, 4, 0, spots[s].U, spots[s].V);
            Float3 color = s_quarterColors[quarter];
            Assert.True(MathF.Abs(albedo.X - color.X) < 0.02f && MathF.Abs(albedo.Y - color.Y) < 0.02f && MathF.Abs(albedo.Z - color.Z) < 0.02f, $"spot {s} is {albedo}");
            Assert.True(albedo.W > 0.99f);
        }
        Assert.True(Texel(data, data.Albedo, 4, 0, 0.02f, 0.02f).W < 0.01f);

        Float4 normalDepth = Texel(data, data.NormalDepth, 4, 0, 0.3f, 0.3f);
        Float2 oct = new(normalDepth.X * 2 - 1, normalDepth.Y * 2 - 1);
        Assert.True(MathF.Abs(oct.X) < 0.02f && MathF.Abs(oct.Y) < 0.02f, $"normal {oct}");
        Assert.InRange(normalDepth.Z, 0.48f, 0.52f);
        Assert.True(MathF.Abs(data.Radius - MathF.Sqrt(0.5f)) < 1e-4f);
    }

    // Renders the world from +Z through the ALBEDO debug view into a 32 pixel square, every renderable at one level
    private Float4[] Render(RenderWorld world, int level)
    {
        Texture color = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(32, 32, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer target = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, color));
        RenderView view = _renderer.CreateView(world);
        try
        {
            view.Target = target;
            view.Sky = false;
            view.ForcedLod = level;
            view.Keywords = [GlobalAxis.Debug["ALBEDO"]];
            view.Camera = ViewCamera.Orthographic(new Double3(0, 0, 5), new Float3(0, 0, -1), new Float3(0, 1, 0), 2, 0.1f, 10, aspect: 1);
            byte[]? data = null;
            _renderer.Readback(color, result => data = result.Data);
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            for (int i = 0; i < 8 && data == null; i++)
            {
                _renderer.WaitForRenderThread();
                if (data == null)
                    _renderer.SubmitFrame(new FrameDesc());
            }
            Assert.NotNull(data);
            return MemoryMarshal.Cast<byte, Float4>(data).ToArray();
        }
        finally
        {
            view.Dispose();
            _renderer.WaitForRenderThread();
            _device.WaitForIdle();
            target.Dispose();
            color.Dispose();
        }
    }

    [Fact]
    public void TheImpostor_DrawsWhatTheMeshDraws()
    {
        (GpuMesh mesh, Material[] materials) = Square();
        ImpostorData data = ImpostorBuilder.Bake(_renderer, mesh, materials, new ImpostorBakeDesc { Frames = 5, CellSize = 32 });
        using ImpostorAsset impostor = data.CreateAsset(_renderer);
        RenderWorld world = _renderer.CreateWorld();
        world.Add(new RenderableDesc { Mesh = mesh, Lods = new LodSet([impostor.Level(0.5f)]), Transform = Double4x4.Identity }, materials);

        Float4[] meshPixels = Render(world, 0);
        Float4[] impostorPixels = Render(world, 1);
        world.Dispose();

        // Pixels whose neighbours share their colour, away from the edges filtering blends
        int covered = 0, matching = 0;
        for (int y = 1; y < 31; y++)
        {
            for (int x = 1; x < 31; x++)
            {
                Float4 m = meshPixels[y * 32 + x];
                bool flat = m.X + m.Y + m.Z > 0.1f;
                foreach (int n in new[] { -1, 1, -32, 32 })
                    flat &= meshPixels[y * 32 + x + n] == m;
                if (!flat)
                    continue;
                covered++;
                Float4 d = m - impostorPixels[y * 32 + x];
                if (MathF.Abs(d.X) < 0.02f && MathF.Abs(d.Y) < 0.02f && MathF.Abs(d.Z) < 0.02f)
                    matching++;
            }
        }
        Assert.True(covered > 100, $"{covered} flat pixels");
        Assert.True(matching == covered, $"{matching} of {covered} pixels match");
    }

    [Fact]
    public void AnAtlas_EncodesItsMipsDownToAPixelAFrame()
    {
        (GpuMesh mesh, Material[] materials) = Square();
        ImpostorData data = ImpostorBuilder.Bake(_renderer, mesh, materials, new ImpostorBakeDesc { Frames = 5, CellSize = 32 });

        IReadOnlyList<byte[]> mips = data.Mips(ImpostorChannel.Albedo);
        Assert.Equal(6, mips.Count);
        Assert.Equal(5 * 5 * 4, mips[^1].Length);

        IReadOnlyList<byte[]> encoded = data.Encode(ImpostorChannel.Albedo, Prowl.Aperture.TextureFormat.Bc3UnormSrgb);
        Assert.Equal(6, encoded.Count);
        for (int mip = 0; mip < encoded.Count; mip++)
            Assert.Equal(Prowl.Aperture.TextureEncoder.GetEncodedSize(Prowl.Aperture.TextureFormat.Bc3UnormSrgb, 160 >> mip, 160 >> mip), encoded[mip].Length);
    }
}
