using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

public sealed class TerrainScatterTests
{
    private static TerrainData Build(int size, Func<int, int, double> metres, IReadOnlyList<TerrainLayerRule>? layers = null)
    {
        TerrainImportSettings settings = new() { Spacing = 1.0, Bottom = -100.0, Range = 655.35, Layers = layers ?? [] };
        ushort[] samples = new ushort[size * size];
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
                samples[z * size + x] = (ushort)Math.Round((metres(x, z) - settings.Bottom) / settings.Range * 65535.0);
        MemoryStream stream = new();
        TerrainImporter.Import(new TerrainHeightmap(size, size, samples), settings, stream);
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    private static double Rolling(int x, int z) => 20.0 + Math.Sin(x * 0.05) * 6.0 + Math.Cos(z * 0.07) * 4.0;

    private static TerrainScatterRule[] Forest =>
    [
        new TerrainScatterRule { Id = 1, Priority = 0, Density = 1.5f, Footprint = 4f, MinScale = 0.8f, MaxScale = 1.3f },
        new TerrainScatterRule { Id = 2, Priority = 1, Density = 6f, Footprint = 1f, Align = 1f, NoiseSize = 20f, NoiseThreshold = 0.4f },
    ];

    [Fact]
    public void Placement_IsTheSameOnEveryThread()
    {
        using TerrainData a = Build(300, Rolling), b = Build(300, Rolling);
        TerrainScatter first = new(a, Forest, seed: 99), second = new(b, Forest, seed: 99);
        (int, int)[] cells = [(0, 0), (1, 0), (0, 1), (2, 3), (4, 4)];
        TerrainInstance[][] one = new TerrainInstance[cells.Length][], two = new TerrainInstance[cells.Length][];
        Parallel.For(0, cells.Length, i => one[i] = first.Cell(cells[i].Item1, cells[i].Item2).ToArray());
        Task.Run(() =>
        {
            for (int i = cells.Length - 1; i >= 0; i--)
                two[i] = second.Cell(cells[i].Item1, cells[i].Item2).ToArray();
        }).Wait();
        for (int i = 0; i < cells.Length; i++)
        {
            Assert.NotEmpty(one[i]);
            Assert.Equal(one[i], two[i]);
        }
    }

    // A rule of 10 per 100 square metres on open flat ground places about that many in a 64 m cell
    [Fact]
    public void Density_MatchesTheRule()
    {
        using TerrainData data = Build(300, (x, z) => 10.0);
        TerrainScatter scatter = new(data, [new TerrainScatterRule { Id = 1, Density = 10f, Footprint = 0.1f }], seed: 1);
        int count = scatter.Cell(1, 1).Count;
        Assert.InRange(count, 370, 450);
    }

    [Fact]
    public void Footprints_KeepInstancesApart()
    {
        using TerrainData data = Build(300, (x, z) => 10.0);
        TerrainScatter scatter = new(data, [new TerrainScatterRule { Id = 1, Density = 40f, Footprint = 3f }], seed: 5);
        IReadOnlyList<TerrainInstance> cell = scatter.Cell(1, 1);
        Assert.True(cell.Count > 20);
        for (int i = 0; i < cell.Count; i++)
            for (int j = i + 1; j < cell.Count; j++)
            {
                double dx = cell[i].Position.X - cell[j].Position.X, dz = cell[i].Position.Z - cell[j].Position.Z;
                Assert.True(dx * dx + dz * dz >= 3.0 * 3.0 - 1e-6, $"{cell[i].Position} {cell[j].Position}");
            }
    }

    // Flat ground at 0 with a slope rising one in one from x 100: a rule limited to gentle ground stays off the slope,
    // and every instance stands on the ground
    [Fact]
    public void Rules_KeepToTheirSlopeAndStandOnTheGround()
    {
        using TerrainData data = Build(300, (x, z) => x < 100 ? 0.0 : x - 100.0);
        TerrainScatter scatter = new(data, [new TerrainScatterRule { Id = 1, Density = 8f, Footprint = 0.5f, MaxSteepness = 0.3f }], seed: 3);
        List<TerrainInstance> all = new();
        for (int cz = 0; cz < 4; cz++)
            for (int cx = 0; cx < 4; cx++)
                all.AddRange(scatter.Cell(cx, cz));
        Assert.NotEmpty(all);
        foreach (TerrainInstance instance in all)
        {
            Assert.True(instance.Position.X < 101, $"{instance.Position}");
            Assert.True(data.TryGetHeight(new Double2(instance.Position.X, instance.Position.Z), out double h));
            Assert.Equal(h, instance.Position.Y, 6);
        }
    }

    // Grass on the left half of the world by its layer rule, the rule asking for the grass layer only places there
    [Fact]
    public void Rules_KeepToTheirLayer()
    {
        TerrainLayerRule[] layers = [new TerrainLayerRule { Layer = 0, MaxHeight = 15, HeightFade = 0.1 }, new TerrainLayerRule { Layer = 1, MinHeight = 15, HeightFade = 0.1 }];
        using TerrainData data = Build(300, (x, z) => x < 128 ? 10.0 : 20.0, layers);
        TerrainScatter scatter = new(data, [new TerrainScatterRule { Id = 1, Density = 8f, Footprint = 0.5f, Layer = 0, MinLayerWeight = 0.5f }], seed: 4);
        List<TerrainInstance> all = new();
        for (int cx = 0; cx < 4; cx++)
            all.AddRange(scatter.Cell(cx, 1));
        Assert.NotEmpty(all);
        Assert.All(all, i => Assert.True(i.Position.X < 129, $"{i.Position}"));
    }

    // Cutting the detail in a circle clears it there, and the ground outside keeps its instances
    [Fact]
    public void DetailCut_ClearsInstances()
    {
        using TerrainData data = Build(300, (x, z) => 10.0);
        TerrainScatterRule[] rules = [new TerrainScatterRule { Id = 1, Density = 8f, Footprint = 0.5f }];
        int Near(TerrainScatter scatter) => scatter.Cell(2, 2).Count(i => Math.Pow(i.Position.X - 150, 2) + Math.Pow(i.Position.Z - 150, 2) < 15 * 15);
        Assert.True(Near(new TerrainScatter(data, rules, seed: 6)) > 30);
        data.Apply(new TerrainEdit(TerrainBrush.Detail, new Double2(150, 150), 25, 1));
        TerrainScatter cut = new(data, rules, seed: 6);
        Assert.Equal(0, Near(cut));
        Assert.True(cut.Cell(0, 0).Count > 100);
    }

    // Removing an instance by id leaves every other one where it was
    [Fact]
    public void Removing_AnInstanceLeavesTheRest()
    {
        using TerrainData data = Build(300, Rolling);
        TerrainScatter scatter = new(data, Forest, seed: 7);
        TerrainInstance[] before = scatter.Cell(2, 2).ToArray();
        Assert.Equal(before.Length, before.Select(i => i.Id).Distinct().Count());
        scatter.Remove(before[3].Id);
        TerrainInstance[] after = scatter.Cell(2, 2).ToArray();
        Assert.Equal(before.Where(i => i.Id != before[3].Id), after);
    }
}
