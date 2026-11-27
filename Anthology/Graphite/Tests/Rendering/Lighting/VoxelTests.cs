using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

public sealed class VoxelTests : IDisposable
{
    private const int Resolution = 32;
    private const float VoxelSize = 0.25f;

    private readonly RendererHarness _harness = new();
    private Renderer Renderer => _harness.Renderer;
    private RenderWorld World => _harness.World;

    public void Dispose() => _harness.Dispose();

    private void Box(Double3 center) => World.SetVoxelVolume(new VoxelVolumeDesc { Center = center, VoxelSize = VoxelSize, Resolution = new Int3(Resolution) });

    // An eight metre square facing -Z at a depth
    private void AddQuad(double z, Material material)
        => World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, z)) }, [material]);

    private Material Lit(Float3 color)
    {
        Material material = Renderer.CreateMaterial(Renderer.Standard.Lit);
        material.SetColor("base_color", new Float4(color.X, color.Y, color.Z, 1));
        return material;
    }

    // The voxel buffer's words after the header
    private uint[] ReadVoxels()
    {
        DeviceBuffer data = World.Voxels.Data!;
        using DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(data.SizeInBytes, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyBuffer(data, 0, staging, 0, data.SizeInBytes)).Wait();
        return _harness.Device.Map<uint>(staging)[(int)(VoxelHeader.Size / 4)..].ToArray();
    }

    // Mirrors Graphite_VoxelIndex
    private int Index(int x, int y, int z)
    {
        VoxelHeader h = World.Voxels.Header;
        int sx = (int)((x + h.WrapX) % Resolution), sy = (int)((y + h.WrapY) % Resolution), sz = (int)((z + h.WrapZ) % Resolution);
        return sx + Resolution * (sy + Resolution * sz);
    }

    private uint Octants(uint[] words, int x, int y, int z)
    {
        int i = Index(x, y, z);
        return (words[World.Voxels.Header.Occupancy + i / 4] >> (i % 4 * 8)) & 0xFF;
    }

    private static Float3 Albedo(uint word)
    {
        Float3 c = new((word >> 16) & 0xFF, (word >> 8) & 0xFF, word & 0xFF);
        c /= 255f;
        return c * c;
    }

    private GpuMesh FacingMesh()
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)];
        GpuMesh mesh = Renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
        });
        _harness.Owned.Add(mesh);
        return mesh;
    }

    [Fact]
    public void TheBox_FollowsItsCentre_InWholeVoxels_StoredWrapped()
    {
        // 2.1 m snaps to voxel 8, so the box starts 16 voxels before at -2 m and the quad fills its first 24 columns
        Box(new Double3(2.1, 0, 0));
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();

        Assert.Equal(new Double3(2, 0, 0), World.Voxels.Center);
        Assert.Equal(24u, World.Voxels.Header.WrapX);
        for (int x = 0; x < Resolution; x++)
            Assert.True((Octants(words, x, 16, 20) != 0) == x < 24, $"column {x}");
    }

    [Fact]
    public void PartialUpdates_ClearAndDrawTheSpaceTheBoxScrollsInto()
    {
        Box(Double3.Zero);
        RenderableHandle quad = World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 1.1)) }, [Lit(new Float3(1, 1, 1))]);
        _harness.Frame(_harness.View());

        // What moves is redrawn within the update frames, -1.1 m is voxel 11
        World.SetTransform(quad, Double4x4.CreateTranslation(new Double3(0, 0, -1.1)));
        int updateFrames = new VoxelVolumeDesc().UpdateFrames;
        for (int frame = 0; frame < updateFrames; frame++)
            _harness.Frame(_harness.View());
        uint[] moved = ReadVoxels();
        for (int x = 0; x < Resolution; x++)
        {
            Assert.True(Octants(moved, x, 16, 20) == 0, $"column {x} still holds the quad where it was");
            Assert.True(Octants(moved, x, 16, 11) != 0, $"column {x} misses the quad where it is");
        }

        World.SetTransform(quad, Double4x4.CreateTranslation(new Double3(0, 0, 1.1)));
        for (int frame = 0; frame < updateFrames; frame++)
            _harness.Frame(_harness.View());

        // Eight voxels along x: the box keeps what it drew and its last 8 columns wrap onto the quad's voxels at -4 m to
        // -2 m, which must be cleared, as the space at 4 m to 6 m they now hold has no quad
        Box(new Double3(2.1, 0, 0));
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();
        for (int x = 0; x < Resolution; x++)
            Assert.True((Octants(words, x, 16, 20) != 0) == x < 24, $"column {x}");
    }

    // A box under a black sky with every cache entry traced each frame, showing the bounced light alone
    private VoxelDebug DarkBoxShowingBounces()
    {
        Box(Double3.Zero);
        World.Environment.SkyColor = new Float4(0, 0, 0, 1);
        World.Voxels.CacheStride = 1;
        VoxelDebug debug = new(Renderer) { Mode = VoxelDebugMode.Indirect };
        World.Features.Add(debug);
        return debug;
    }

    // A white wall 8 m square facing the camera at z 1.1, its centre 2 m over a white floor as big
    private void AddWallOverFloor()
    {
        World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(0, 2, 1.1)) }, [Lit(new Float3(1, 1, 1))]);
        Quaternion flat = Quaternion.AxisAngle(new Float3(1, 0, 0), MathF.PI / 2);
        World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTRS(new Double3(0, -2, -1.5), flat, new Double3(1, 1, 1)) }, [Lit(new Float3(1, 1, 1))]);
    }

    private Float4 Centre(Float4[] pixels) => pixels[RendererHarness.Size / 2 * RendererHarness.Size + RendererHarness.Size / 2];

    [Fact]
    public void TheRadianceCache_BouncesTheSunOffAFloor_OntoAWallInShade()
    {
        DarkBoxShowingBounces();
        AddWallOverFloor();
        World.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(0, -1, 0), Color = new Float3(1, 1, 1), Intensity = 10, Shadow = new ShadowDesc { Enabled = true } });

        // The sun lights only the floor. The wall's centre, 2 m above it, sees the floor in the box with a view factor
        // of 0.279, and its voxels send back their albedo, clamped to 0.9 for bounces, times that much of the floor.
        float floor = 10 / MathF.PI * RenderView.ExposureOf(0);
        float expected = 0.279f * floor * 0.9f;
        expected /= 1 + expected;
        Float4 centre = default;
        for (int frame = 0; frame < 40; frame++)
            centre = Centre(_harness.Frame(_harness.View()));
        Assert.True(centre.X > 0.5f * expected, $"the wall's bounced light is {centre.X}, against {expected}");
    }

    [Fact]
    public void TheRadianceCache_HoldsNoLight_WithNothingToBounce()
    {
        DarkBoxShowingBounces();
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));
        for (int frame = 0; frame < 10; frame++)
            RendererHarness.AssertAll(new Float3(0, 0, 0), _harness.Frame(_harness.View()));
    }
}
