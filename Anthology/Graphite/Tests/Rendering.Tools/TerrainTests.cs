using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Prowl.Aperture;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

public sealed class TerrainTests
{
    private static TerrainData Build(int width, int height, Func<int, int, double> metres, TerrainImportSettings settings, Func<int, int, bool>? hole = null)
    {
        ushort[] samples = new ushort[width * height];
        for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
                samples[z * width + x] = (ushort)Math.Round((metres(x, z) - settings.Bottom) / settings.Range * 65535.0);
        TerrainHeightmap map = new(width, height, samples);
        if (hole != null)
        {
            map.Holes = new bool[width * height];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    map.Holes[z * width + x] = hole(x, z);
        }
        MemoryStream stream = new();
        TerrainImporter.Import(map, settings, stream);
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    private static TerrainImportSettings Settings(double spacing = 1.0) => new() { Spacing = spacing, Bottom = -100.0, Range = 655.35 };

    // Heights land on multiples of 0.01 m so 16 bit quantisation over a 655.35 m range is exact
    private static double Bumpy(int x, int z) => ((x * 7 + z * 13) % 50) * 0.5 + x * 0.25;

    [Fact]
    public void HeightAtSamplesMatchesTheSource()
    {
        using TerrainData data = Build(300, 260, Bumpy, Settings());
        Assert.Equal(299.0, data.SizeX);
        Assert.Equal(259.0, data.SizeZ);
        foreach ((int x, int z) in new[] { (0, 0), (5, 9), (127, 128), (128, 128), (129, 3), (255, 256), (299, 259) })
        {
            Assert.True(data.TryGetHeight(new Double2(x, z), out double h));
            Assert.Equal(Bumpy(x, z), h, 6);
        }
    }

    [Fact]
    public void HeightBetweenSamplesFollowsTheFixedDiagonal()
    {
        // Cell (10, 10) splits from (10, 10) to (11, 11), so its raised corner (11, 10) lifts only the triangle under the diagonal
        using TerrainData data = Build(64, 64, (x, z) => x == 11 && z == 10 ? 10.0 : 0.0, Settings());
        Assert.True(data.TryGetHeight(new Double2(10.75, 10.25), out double below));
        Assert.Equal(5.0, below, 6);
        Assert.True(data.TryGetHeight(new Double2(10.25, 10.75), out double above));
        Assert.Equal(0.0, above, 6);
        Assert.True(data.TryGetHeight(new Double2(10.5, 10.5), out double diagonal));
        Assert.Equal(0.0, diagonal, 6);
    }

    [Fact]
    public void HeightOutsideTheWorldFails()
    {
        using TerrainData data = Build(100, 100, Bumpy, Settings());
        Assert.False(data.TryGetHeight(new Double2(-0.01, 5), out _));
        Assert.False(data.TryGetHeight(new Double2(5, 99.01), out _));
        Assert.True(data.TryGetHeight(new Double2(99, 99), out _));
    }

    [Fact]
    public void PositionAndSpacingPlaceTheWorld()
    {
        using TerrainData data = Build(100, 100, Bumpy, Settings(spacing: 2.0));
        data.Position = new Double3(1000.0, 50.0, -500.0);
        Assert.True(data.TryGetHeight(new Double2(1000.0 + 2 * 7, -500.0 + 2 * 3), out double h));
        Assert.Equal(Bumpy(7, 3) + 50.0, h, 6);
        Assert.Equal(198.0, data.SizeX);
    }

    [Fact]
    public void UpperLevelsKeepEveryOtherSample()
    {
        using TerrainData data = Build(600, 600, Bumpy, Settings());
        Assert.Equal(4, data.Levels);
        for (int level = 1; level < data.Levels; level++)
        {
            int step = 1 << level;
            foreach ((int x, int z) in new[] { (0, 0), (step * 3, step * 5), (step * 64, step * 64), (step * 70, step * 2) })
            {
                if (x > 599 || z > 599)
                    continue;
                Assert.True(data.TryGetHeight(new Double2(x, z), out double h, level));
                Assert.Equal(Bumpy(x, z), h, 6);
            }
        }
    }

    [Fact]
    public void PageBoundsCoverTheLeafHeightsUnderThem()
    {
        using TerrainData data = Build(600, 600, Bumpy, Settings());
        for (int level = 0; level < data.Levels; level++)
        {
            (int pagesX, int pagesZ) = data.PagesAt(level);
            for (int pz = 0; pz < pagesZ; pz++)
                for (int px = 0; px < pagesX; px++)
                {
                    Assert.True(data.TryGetPageBounds(new TerrainPageKey(level, px, pz), out double min, out double max));
                    int span = TerrainPage.Quads << level;
                    for (int z = pz * span; z <= Math.Min((pz + 1) * span, 599); z += 3)
                        for (int x = px * span; x <= Math.Min((px + 1) * span, 599); x += 3)
                        {
                            double h = Bumpy(x, z);
                            Assert.InRange(h, min - 1e-6, max + 1e-6);
                        }
                }
        }
    }

    [Fact]
    public void HolesRemoveTheirQuads()
    {
        using TerrainData data = Build(64, 64, Bumpy, Settings(), (x, z) => x == 20 && z == 30);
        Assert.True(data.IsHole(new Double2(20.5, 30.5)));
        Assert.False(data.TryGetHeight(new Double2(20.5, 30.5), out _));
        Assert.False(data.IsHole(new Double2(19.5, 30.5)));
        Assert.True(data.TryGetHeight(new Double2(19.5, 30.5), out _));
    }

    [Fact]
    public void FaceNormalsFollowTheSlope()
    {
        // Rises 0.5 m per metre along x
        using TerrainData data = Build(64, 64, (x, z) => x * 0.5, Settings());
        Float3 expected = Float3.Normalize(new Float3(-0.5f, 1f, 0f));
        Assert.True(data.TryGetNormal(new Double2(10.3, 20.6), out Float3 face));
        Assert.Equal(expected.X, face.X, 4);
        Assert.Equal(expected.Y, face.Y, 4);
        Assert.Equal(expected.Z, face.Z, 4);
        Assert.True(data.TryGetNormal(new Double2(10.3, 20.6), out Float3 smooth, smooth: true));
        Assert.Equal(expected.X, smooth.X, 2);
        Assert.Equal(expected.Y, smooth.Y, 2);
        Assert.Equal(expected.Z, smooth.Z, 2);
    }

    [Fact]
    public void FlatGroundIsFullyLit()
    {
        using TerrainData data = Build(200, 200, (x, z) => 10.0, Settings());
        Assert.True(data.TryGetAmbientOcclusion(new Double2(100, 100), out float ao));
        Assert.Equal(1f, ao, 2);
    }

    [Fact]
    public void PitFloorIsOccluded()
    {
        using TerrainData data = Build(200, 200, (x, z) => Math.Abs(x - 100) < 4 && Math.Abs(z - 100) < 4 ? 0.0 : 40.0, Settings());
        Assert.True(data.TryGetAmbientOcclusion(new Double2(100, 100), out float ao));
        Assert.True(ao < 0.5f, $"ao {ao}");
    }

    [Fact]
    public void RaycastHitsFlatGround()
    {
        using TerrainData data = Build(300, 300, (x, z) => 10.0, Settings());
        Assert.True(data.Raycast(new Double3(150.3, 60.0, 140.7), Float3.Normalize(new Float3(1f, -1f, 0.5f)), 500.0, out TerrainHit hit));
        Assert.Equal(10.0, hit.Position.Y, 4);
        Assert.Equal(200.3, hit.Position.X, 4);
        Assert.Equal(165.7, hit.Position.Z, 4);
        Assert.Equal(1f, hit.Normal.Y, 4);
        Assert.Equal(Math.Sqrt(50 * 50 * 2.25), hit.Distance, 4);
    }

    [Fact]
    public void RaycastStraightDownMatchesHeight()
    {
        using TerrainData data = Build(300, 300, Bumpy, Settings());
        Double2 xz = new(170.37, 131.81);
        Assert.True(data.TryGetHeight(xz, out double h));
        Assert.True(data.Raycast(new Double3(xz.X, 1000.0, xz.Y), new Float3(0f, -1f, 0f), 2000.0, out TerrainHit hit));
        Assert.Equal(h, hit.Position.Y, 4);
    }

    [Fact]
    public void RaycastAcrossManyPagesHitsAWall()
    {
        // Flat at 0 with a 50 m wall from x 400 on
        using TerrainData data = Build(600, 300, (x, z) => x >= 400 ? 50.0 : 0.0, Settings());
        Assert.True(data.Raycast(new Double3(1.0, 20.0, 150.0), new Float3(1f, 0f, 0f), 1000.0, out TerrainHit hit));
        Assert.Equal(399.4, hit.Position.X, 4);
        Assert.True(hit.Normal.X < -0.9f);
    }

    [Fact]
    public void RaycastMissesAboveAndBeyondRange()
    {
        using TerrainData data = Build(300, 300, (x, z) => 10.0, Settings());
        Assert.False(data.Raycast(new Double3(10.0, 60.0, 10.0), new Float3(1f, 0f, 0f), 500.0, out _));
        Assert.False(data.Raycast(new Double3(150.0, 60.0, 150.0), new Float3(0f, -1f, 0f), 49.0, out _));
        Assert.True(data.Raycast(new Double3(150.0, 60.0, 150.0), new Float3(0f, -1f, 0f), 51.0, out _));
    }

    [Fact]
    public void RaycastFallsThroughHoles()
    {
        using TerrainData data = Build(64, 64, (x, z) => 10.0, Settings(), (x, z) => x == 20 && z == 30);
        Assert.False(data.Raycast(new Double3(20.5, 50.0, 30.5), new Float3(0f, -1f, 0f), 100.0, out _));
        Assert.True(data.Raycast(new Double3(21.5, 50.0, 30.5), new Float3(0f, -1f, 0f), 100.0, out _));
    }

    [Fact]
    public void PackageRoundTripsThroughAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"terrain_{Guid.NewGuid():N}.gterrain");
        try
        {
            ushort[] samples = new ushort[200 * 200];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (ushort)(i * 31 % 65536);
            TerrainImporter.Import(new TerrainHeightmap(200, 200, samples), Settings(), path);
            using TerrainData data = TerrainData.Open(path);
            for (int z = 0; z < 200; z += 7)
                for (int x = 0; x < 200; x += 11)
                {
                    Assert.True(data.TryGetHeight(new Double2(x, z), out double h));
                    Assert.Equal(-100.0 + samples[z * 200 + x] / 65535.0 * 655.35, h, 6);
                }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Raw16LoadsSquareLittleEndian()
    {
        string path = Path.Combine(Path.GetTempPath(), $"terrain_{Guid.NewGuid():N}.raw");
        try
        {
            byte[] bytes = new byte[33 * 33 * 2];
            for (int i = 0; i < 33 * 33; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), (ushort)(i * 50));
            File.WriteAllBytes(path, bytes);
            TerrainHeightmap map = TerrainHeightmap.Load(path);
            Assert.Equal(33, map.Width);
            Assert.Equal(33, map.Height);
            Assert.Equal((ushort)(34 * 50), map.Samples[34]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void R32MapsItsSourceRange()
    {
        string path = Path.Combine(Path.GetTempPath(), $"terrain_{Guid.NewGuid():N}.r32");
        try
        {
            byte[] bytes = new byte[17 * 17 * 4];
            for (int i = 0; i < 17 * 17; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), i % 2 == 0 ? 200f : 300f);
            File.WriteAllBytes(path, bytes);
            TerrainHeightmap map = TerrainHeightmap.Load(path, new TerrainHeightmapOptions { SourceMin = 100f, SourceMax = 300f });
            Assert.Equal(17, map.Width);
            Assert.Equal((ushort)32768, map.Samples[0]);
            Assert.Equal((ushort)65535, map.Samples[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Png16LoadsThroughAperture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"terrain_{Guid.NewGuid():N}.png");
        try
        {
            ushort[] pixels = new ushort[40 * 30];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (ushort)(i * 53);
            using (Image image = Image.FromPixels(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()), 40, 30, Prowl.Aperture.PixelFormat.L16))
                image.Save(path);
            TerrainHeightmap map = TerrainHeightmap.Load(path);
            Assert.Equal(40, map.Width);
            Assert.Equal(30, map.Height);
            Assert.Equal(pixels, map.Samples);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TilesJoinIntoOneMap()
    {
        // Two by one tiles sharing their seam column
        ushort[] left = new ushort[5 * 4], right = new ushort[5 * 4];
        for (int z = 0; z < 4; z++)
            for (int x = 0; x < 5; x++)
            {
                left[z * 5 + x] = (ushort)(z * 100 + x);
                right[z * 5 + x] = (ushort)(z * 100 + x + 4);
            }
        TerrainHeightmap map = TerrainHeightmap.Join(new TerrainHeightmap[,] { { new(5, 4, left), new(5, 4, right) } }, overlap: 1);
        Assert.Equal(9, map.Width);
        Assert.Equal(4, map.Height);
        for (int z = 0; z < 4; z++)
            for (int x = 0; x < 9; x++)
                Assert.Equal((ushort)(z * 100 + x), map.Samples[z * 9 + x]);
    }

    // Two upward triangles a quad, split as the renderer splits them, none in holes, only quads touching the box
    [Fact]
    public void TrianglesCoverTheBoxWithoutHoles()
    {
        using TerrainData data = Build(64, 64, Bumpy, Settings(), (x, z) => x == 20 && z == 30);
        List<TerrainTriangle> triangles = new();
        data.GetTriangles(new Double3(18.5, -1000, 29.5), new Double3(21.5, 1000, 31.5), triangles);

        // Quads 18 to 21 along x and 29 to 31 along z, less the hole at 20, 30
        Assert.Equal((4 * 3 - 1) * 2, triangles.Count);
        foreach (TerrainTriangle t in triangles)
        {
            Double3 up = Double3.Cross(t.B - t.A, t.C - t.A);
            Assert.True(up.Y > 0, "triangles face up");
            Double3 centre = (t.A + t.B + t.C) / 3.0;
            Assert.False(Math.Floor(centre.X) == 20 && Math.Floor(centre.Z) == 30);
            Assert.True(data.TryGetHeight(new Double2(centre.X, centre.Z), out double h));
            Assert.Equal(h, centre.Y, 6);
        }
    }

    // A box above the ground's highest point in it holds no triangles
    [Fact]
    public void TrianglesSkipQuadsTheBoxIsAbove()
    {
        using TerrainData data = Build(64, 64, (x, z) => 10.0, Settings());
        List<TerrainTriangle> triangles = new();
        data.GetTriangles(new Double3(10, 11, 10), new Double3(20, 30, 20), triangles);
        Assert.Empty(triangles);
        data.GetTriangles(new Double3(10, 9, 10), new Double3(20, 30, 20), triangles);
        Assert.Equal(10 * 10 * 2, triangles.Count);
    }

    // The window keeps the finest pages near its points loaded and lets go of those it left
    [Fact]
    public async Task InterestWindowLoadsAndUnloadsFinestPages()
    {
        using TerrainData data = Build(1000, 1000, Bumpy, Settings());
        TerrainPageKey near = new(0, 1, 1), far = new(0, 6, 6);
        await data.SetInterest([new Double3(200, 0, 200)], 100);
        Assert.True(data.IsLoaded(near));
        Assert.False(data.IsLoaded(far));

        await data.SetInterest([new Double3(830, 0, 830)], 100);
        Assert.False(data.IsLoaded(near));
        Assert.True(data.IsLoaded(far));
    }

    // Grass on flat low ground, rock on the steep wall, snow on the high flat top
    [Fact]
    public void LayerRulesBakeIntoControl()
    {
        TerrainImportSettings settings = new()
        {
            Spacing = 1.0,
            Bottom = -100.0,
            Range = 655.35,
            Layers =
            [
                new TerrainLayerRule { Layer = 1 },
                new TerrainLayerRule { Layer = 2, MinSlope = 40, Weight = 2 },
                new TerrainLayerRule { Layer = 3, MinHeight = 80, Weight = 3, MaxSlope = 30 },
            ],
        };
        using TerrainData data = Build(200, 200, (x, z) => x < 100 ? 0.0 : x < 110 ? (x - 100) * 10.0 : 100.0, settings);
        uint Base(int x, int z) => data.GetPage(new TerrainPageKey(0, x / 128, z / 128))!.Control![TerrainPage.Index(x % 128, z % 128)];
        Assert.Equal(1, TerrainControl.BaseLayer(Base(40, 50)));
        Assert.Equal(2, TerrainControl.BaseLayer(Base(105, 50)));
        Assert.Equal(3, TerrainControl.BaseLayer(Base(160, 50)));
    }

    [Fact]
    public void PageDumpWritesAnImage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"terrain_{Guid.NewGuid():N}.png");
        try
        {
            using TerrainData data = Build(200, 200, Bumpy, Settings());
            TerrainDebug.DumpPage(data, new TerrainPageKey(0, 1, 0), path);
            ImageInfo info = Image.Identify(path);
            Assert.Equal(TerrainPage.Size * 2, info.Width);
            Assert.Equal(TerrainPage.Size, info.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
