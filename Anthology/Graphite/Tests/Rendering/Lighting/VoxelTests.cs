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

    private bool CellFilled(uint[] words, int level, int x, int y, int z)
    {
        VoxelHeader h = World.Voxels.Header;
        uint start = level switch { 1 => h.Level1, 2 => h.Level2, 3 => h.Level3, 4 => h.Level4, _ => h.Level5 };
        int r = Resolution >> level;
        int i = x + r * (y + r * z);
        return (words[start + i / 32] & (1u << (i % 32))) != 0;
    }

    private static Float3 Albedo(uint word)
    {
        Float3 c = new((word >> 16) & 0xFF, (word >> 8) & 0xFF, word & 0xFF);
        c /= 255f;
        return c * c;
    }

    [Fact]
    public void Voxelization_FillsTheSliceASurfaceCrosses_WithItsAlbedo()
    {
        Box(Double3.Zero);
        AddQuad(1.1, Lit(new Float3(0.25f, 0.5f, 1f)));
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();

        // 1.1 m is voxel 20 of the box from -4 m, in the lower half along z
        for (int z = 0; z < Resolution; z++)
        {
            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    uint octants = Octants(words, x, y, z);
                    if (z != 20)
                    {
                        Assert.True(octants == 0, $"voxel {x} {y} {z} is filled");
                        continue;
                    }
                    Assert.True(octants != 0 && (octants & 0xF0) == 0, $"voxel {x} {y} {z} holds octants {octants:X2}");
                    Float3 albedo = Albedo(words[Index(x, y, z)]);
                    Assert.True(MathF.Abs(albedo.X - 0.25f) < 0.01f && MathF.Abs(albedo.Y - 0.5f) < 0.01f && MathF.Abs(albedo.Z - 1f) < 0.01f, $"voxel {x} {y} {z} albedo {albedo}");
                }
            }
        }
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    public void TiltedSurfaces_LeaveNoHoles(double degrees)
    {
        Box(Double3.Zero);
        GpuMesh mesh = FacingMesh();
        double angle = degrees * Math.PI / 180;
        World.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 0.3)) * Double4x4.RotateX(angle) }, [Lit(new Float3(1, 1, 1))]);
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();

        // Every point of the surface lies in a filled voxel, or one next to it across the surface
        for (double u = -3.9; u < 3.9; u += VoxelSize / 4)
        {
            for (double v = -3.9; v < 3.9; v += VoxelSize / 4)
            {
                Double3 p = new(u, v * Math.Cos(angle), v * Math.Sin(angle) + 0.3);
                int x = (int)Math.Floor(p.X / VoxelSize) + Resolution / 2, y = (int)Math.Floor(p.Y / VoxelSize) + Resolution / 2, z = (int)Math.Floor(p.Z / VoxelSize) + Resolution / 2;
                if (y is < 1 or >= Resolution - 1 || z is < 1 or >= Resolution - 1)
                    continue;
                bool filled = false;
                for (int d = -1; d <= 1 && !filled; d++)
                    filled = Octants(words, x, y, z + d) != 0 || Octants(words, x, y + d, z) != 0;
                Assert.True(filled, $"no voxel near {p} at {degrees} degrees");
            }
        }
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
    public void ThePyramid_MarksTheCellsHoldingFilledVoxels()
    {
        Box(Double3.Zero);
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();

        // Cells are in stored coordinates, where the box's voxel 20 along z lies wrapped
        int stored = (int)((20 + World.Voxels.Header.WrapZ) % Resolution);
        for (int level = 1; level <= 5; level++)
        {
            int r = Resolution >> level;
            for (int z = 0; z < r; z++)
            {
                for (int y = 0; y < r; y++)
                {
                    for (int x = 0; x < r; x++)
                        Assert.True(CellFilled(words, level, x, y, z) == (z == stored >> level), $"level {level} cell {x} {y} {z}");
                }
            }
        }
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
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));
        _harness.Frame(_harness.View());

        // Eight voxels along x: the box keeps what it drew and its last 8 columns wrap onto the quad's voxels at -4 m to
        // -2 m, which must be cleared, as the space at 4 m to 6 m they now hold has no quad
        Box(new Double3(2.1, 0, 0));
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();
        for (int x = 0; x < Resolution; x++)
            Assert.True((Octants(words, x, 16, 20) != 0) == x < 24, $"column {x}");
    }

    [Fact]
    public void PartialUpdates_RedrawWhatMoved_WithinTheirFrames()
    {
        Box(Double3.Zero);
        RenderableHandle quad = World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 1.1)) }, [Lit(new Float3(1, 1, 1))]);
        _harness.Frame(_harness.View());
        World.SetTransform(quad, Double4x4.CreateTranslation(new Double3(0, 0, -1.1)));
        for (int frame = 0; frame < new VoxelVolumeDesc().UpdateFrames; frame++)
            _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();

        // -1.1 m is voxel 11
        for (int x = 0; x < Resolution; x++)
        {
            Assert.True(Octants(words, x, 16, 20) == 0, $"column {x} still holds the quad where it was");
            Assert.True(Octants(words, x, 16, 11) != 0, $"column {x} misses the quad where it is");
        }
    }

    [Fact]
    public void UnlitSurfaces_WriteEmissiveVoxels()
    {
        Box(Double3.Zero);
        Material unlit = Renderer.CreateMaterial(Renderer.Standard.Unlit);
        unlit.SetColor("base_color", new Float4(1, 0.5f, 0.25f, 1));

        // Emission is pre-exposed by the environment's capture exposure, daylight by default, so a bright emitter
        unlit.SetFloat("emission_luminance", 100000);
        AddQuad(1.1, unlit);
        _harness.Frame(_harness.View());
        uint[] words = ReadVoxels();

        uint word = words[Index(16, 16, 20)];
        Assert.True((word & 0x80000000u) != 0, $"word {word:X8} is not emissive");
        Assert.True(((word >> 16) & 0xFF) > ((word >> 8) & 0xFF) && ((word >> 8) & 0xFF) > (word & 0xFF), $"word {word:X8} lost the colour's order");
    }

    [Fact]
    public void TheDebugView_ShowsTheVoxelsTheCameraSees()
    {
        Box(Double3.Zero);
        AddQuad(1.1, Lit(new Float3(0.25f, 0.5f, 1f)));
        VoxelDebug debug = new(Renderer) { Mode = VoxelDebugMode.Normals };
        World.Features.Add(debug);

        // The quad's face toward the camera at the origin points down -Z
        RendererHarness.AssertAll(new Float3(0.5f, 0.5f, 0), _harness.Frame(_harness.View()));

        debug.Mode = VoxelDebugMode.Albedo;
        Float4 centre = _harness.Frame(_harness.View())[RendererHarness.Size / 2 * RendererHarness.Size + RendererHarness.Size / 2];
        Assert.True(MathF.Abs(centre.X - 0.25f) < 0.02f && MathF.Abs(centre.Y - 0.5f) < 0.02f && MathF.Abs(centre.Z - 1f) < 0.02f, $"centre {centre}");
    }

    [Fact]
    public void HitLighting_TakesTheSunThroughItsShadowOverTheBox()
    {
        Box(Double3.Zero);
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));

        // A wall behind the orthographic camera and left of the view, which the sun from the left throws onto the left
        // half: three metres nearer the sun along z moves the shadow three metres along x
        World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(-5, 0, -1.9)) * Double4x4.CreateScale(new Double3(0.5, 1, 1)) }, [Lit(new Float3(1, 1, 1))]);
        World.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(1, 0, 1) / MathF.Sqrt(2), Color = new Float3(1, 1, 1), Intensity = 10, Shadow = new ShadowDesc { Enabled = true } });
        World.Features.Add(new VoxelDebug(Renderer) { Mode = VoxelDebugMode.Lighting });
        Float4[] pixels = _harness.Frame(_harness.View());

        // The white quad's light at the view's exposure, albedo over pi times the sun at 45 degrees, then x / (1 + x)
        float lit = 10 * 0.70710678f / MathF.PI * RenderView.ExposureOf(0);
        lit /= 1 + lit;
        int row = RendererHarness.Size / 2 * RendererHarness.Size;
        for (int x = 1; x < 7; x++)
            Assert.True(pixels[row + x].X < 0.02f, $"column {x} is lit: {pixels[row + x]}");
        for (int x = 9; x < 15; x++)
            Assert.True(MathF.Abs(pixels[row + x].X - lit) < 0.02f, $"column {x} is {pixels[row + x]}, not {lit}");
    }

    [Fact]
    public void HitLighting_ShowsTheSkyWhereRaysLeaveTheBox()
    {
        Box(Double3.Zero);
        World.Features.Add(new VoxelDebug(Renderer) { Mode = VoxelDebugMode.Lighting });
        Float4 sky = World.Environment.SkyColor * RenderView.ExposureOf(0);
        RendererHarness.AssertAll(new Float3(sky.X / (1 + sky.X), sky.Y / (1 + sky.Y), sky.Z / (1 + sky.Z)), _harness.Frame(_harness.View()), 0.01f);
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
    public void TheRadianceCache_BouncesAnEmittersLightOntoTheFacesItSees()
    {
        DarkBoxShowingBounces();
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));

        // A red emitter behind the orthographic camera, facing the white quad across the box
        Material red = Renderer.CreateMaterial(Renderer.Standard.Unlit);
        red.SetColor("base_color", new Float4(1, 0, 0, 1));
        red.SetFloat("emission_luminance", 100000);
        AddQuad(-2.6, red);

        Float4 centre = default;
        for (int frame = 0; frame < 40; frame++)
            centre = Centre(_harness.Frame(_harness.View()));
        Assert.True(centre.X > 0.5f && centre.Y < 0.02f && centre.Z < 0.02f, $"the white quad's bounced light is {centre}");
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRadianceCache_HoldsTheLocalLightsReachingEachFace(bool shadowed)
    {
        Box(Double3.Zero);
        World.Environment.SkyColor = new Float4(0, 0, 0, 1);
        World.Voxels.CacheStride = 1;
        AddQuad(1.1, Lit(new Float3(1, 1, 1)));
        World.AddLight(new LightDesc { Type = LightType.Point, Position = new Double3(0, 0, -2.9), Color = new Float3(1, 1, 1), Intensity = 100, Range = 10, Shadow = new ShadowDesc { Enabled = shadowed } });
        World.Features.Add(new VoxelDebug(Renderer) { Mode = VoxelDebugMode.Lighting });

        // The wall's faces toward the light are at 1 m, the near side of its voxels, 3.9 m from a 100 candela point far
        // enough that which voxel a pixel lands in barely matters. They send back the white albedo, clamped to 0.9 for
        // bounces, times the illuminance over pi.
        float light = 100 / (3.9f * 3.9f) / MathF.PI * 0.9f * RenderView.ExposureOf(0);
        light /= 1 + light;
        Float4 centre = default;
        for (int frame = 0; frame < 4; frame++)
            centre = Centre(_harness.Frame(_harness.View()));
        Assert.True(MathF.Abs(centre.X - light) < 0.02f, $"the wall's voxels show {centre}, not {light}");
    }

    // The radiance cache's live entries
    private int CacheEntries() => CacheEntriesWhere(_ => true);

    [Fact]
    public void TheRadianceCache_LetsGoOfFacesWhoseVoxelsEmptied()
    {
        DarkBoxShowingBounces();
        Material white = Lit(new Float3(1, 1, 1));
        RenderableHandle quad = World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 1.1)) }, [white]);
        _harness.Frame(_harness.View());
        Assert.True(CacheEntries() > 0);

        // Its voxels empty once the box's rolling redraw has passed over them, and the cache lets go the frame after
        World.Remove(quad);
        for (int frame = 0; frame <= new VoxelVolumeDesc().UpdateFrames; frame++)
            _harness.Frame(_harness.View());
        Assert.Equal(0, CacheEntries());
    }

    // The radiance cache's live entries whose voxel, in the box as it is now, passes a test
    private int CacheEntriesWhere(Func<Int3, bool> where)
    {
        DeviceBuffer cache = World.Voxels.Cache!;
        using DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(cache.SizeInBytes, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyBuffer(cache, 0, staging, 0, cache.SizeInBytes)).Wait();
        Span<uint> words = _harness.Device.Map<uint>(staging);
        VoxelHeader h = World.Voxels.Header;
        int count = 0;
        for (int i = 0; i < words.Length; i += 8)
        {
            if (words[i] == 0)
                continue;
            uint key = words[i] - 1;
            Int3 voxel = new((int)(((key & 511) + Resolution - h.WrapX) % Resolution), (int)((((key >> 9) & 511) + Resolution - h.WrapY) % Resolution),
                (int)((((key >> 18) & 511) + Resolution - h.WrapZ) % Resolution));
            count += where(voxel) ? 1 : 0;
        }
        return count;
    }

    [Fact]
    public void TheRadianceCache_LetsGoOfFacesTheBoxScrollsOver()
    {
        VoxelDebug debug = DarkBoxShowingBounces();

        // Sixteen metres wide, so the slab the box scrolls into holds the quad's voxels too, in the slots its faces at
        // -4 m to -2 m held
        World.Add(new RenderableDesc { Mesh = FacingMesh(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 1.1)) * Double4x4.CreateScale(new Double3(2, 1, 1)) }, [Lit(new Float3(1, 1, 1))]);
        _harness.Frame(_harness.View());
        Assert.True(CacheEntriesWhere(voxel => voxel.X < 8) > 0);

        // Nothing spawns entries or updates more than one a frame from here
        World.Features.Remove(debug);
        World.Voxels.CacheStride = WorldVoxels.CacheEntries;
        int kept = CacheEntriesWhere(voxel => voxel.X >= 8);
        Box(new Double3(2.1, 0, 0));
        _harness.Frame(_harness.View());
        Assert.Equal(0, CacheEntriesWhere(voxel => voxel.X >= Resolution - 8));

        // The faces the box still holds keep their entries, 8 columns along now
        Assert.True(CacheEntries() >= kept - 1, $"the cache kept {CacheEntries()} of the {kept} entries the box still holds");
    }

    [Fact]
    public void TheDebugView_ShowsTheImage_WhileTheWorldHasNoBox()
    {
        World.Features.Add(new VoxelDebug(Renderer));
        RendererHarness.AssertAll(new Float3(0, 0, 0), _harness.Frame(_harness.View()));
    }

    [Fact]
    public void AWorldWithoutABox_HoldsNoVoxels()
    {
        Box(Double3.Zero);
        _harness.Frame(_harness.View());
        Assert.NotNull(World.Voxels.Data);
        World.SetVoxelVolume(null);
        _harness.Frame(_harness.View());
        Assert.Null(World.Voxels.Data);
    }
}
