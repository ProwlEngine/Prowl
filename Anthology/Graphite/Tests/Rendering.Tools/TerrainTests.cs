using System;
using System.Collections.Generic;
using System.IO;

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

        using TerrainData bumpy = Build(300, 260, Bumpy, Settings());
        Assert.Equal(299.0, bumpy.SizeX);
        Assert.Equal(259.0, bumpy.SizeZ);
        foreach ((int x, int z) in new[] { (0, 0), (5, 9), (127, 128), (128, 128), (129, 3), (255, 256), (299, 259) })
        {
            Assert.True(bumpy.TryGetHeight(new Double2(x, z), out double h));
            Assert.Equal(Bumpy(x, z), h, 6);
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
    public void RaycastAcrossManyPagesHitsAWall()
    {
        // Flat at 0 with a 50 m wall from x 400 on
        using TerrainData data = Build(600, 300, (x, z) => x >= 400 ? 50.0 : 0.0, Settings());
        Assert.True(data.Raycast(new Double3(1.0, 20.0, 150.0), new Float3(1f, 0f, 0f), 1000.0, out TerrainHit hit));
        Assert.Equal(399.4, hit.Position.X, 4);
        Assert.True(hit.Normal.X < -0.9f);

        using TerrainData flat = Build(300, 300, (x, z) => 10.0, Settings());
        Assert.True(flat.Raycast(new Double3(150.3, 60.0, 140.7), Float3.Normalize(new Float3(1f, -1f, 0.5f)), 500.0, out TerrainHit slanted));
        Assert.Equal(10.0, slanted.Position.Y, 4);
        Assert.Equal(200.3, slanted.Position.X, 4);
        Assert.Equal(165.7, slanted.Position.Z, 4);
        Assert.Equal(1f, slanted.Normal.Y, 4);
        Assert.Equal(Math.Sqrt(50 * 50 * 2.25), slanted.Distance, 4);

        using TerrainData bumpy = Build(300, 300, Bumpy, Settings());
        Double2 xz = new(170.37, 131.81);
        Assert.True(bumpy.TryGetHeight(xz, out double height));
        Assert.True(bumpy.Raycast(new Double3(xz.X, 1000.0, xz.Y), new Float3(0f, -1f, 0f), 2000.0, out TerrainHit down));
        Assert.Equal(height, down.Position.Y, 4);

        using TerrainData holed = Build(64, 64, (x, z) => 10.0, Settings(), (x, z) => x == 20 && z == 30);
        Assert.False(holed.Raycast(new Double3(20.5, 50.0, 30.5), new Float3(0f, -1f, 0f), 100.0, out _));
        Assert.True(holed.Raycast(new Double3(21.5, 50.0, 30.5), new Float3(0f, -1f, 0f), 100.0, out _));
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
}
