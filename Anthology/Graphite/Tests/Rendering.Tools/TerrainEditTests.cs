using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

public sealed class TerrainEditTests
{
    private const int Size = 300;

    // Layer 0 below 25 m and layer 1 above, two more layers left empty, and one detail prototype grown nowhere
    private static readonly TerrainImportSettings s_settings = new()
    {
        Spacing = 1.0,
        Bottom = -100.0,
        Range = 655.35,
        Layers = [Weights((x, z) => Low(x, z) ? (byte)255 : (byte)0), Weights((x, z) => Low(x, z) ? (byte)0 : (byte)255), Weights((x, z) => 0), Weights((x, z) => 0)],
        Details = [Weights((x, z) => 0)],
    };

    private static bool Low(int x, int z) => Rolling().Samples[z * Size + x] * 0.01 - 100.0 < 25.0;

    private static TerrainWeightMap Weights(Func<int, int, byte> value)
    {
        byte[] values = new byte[Size * Size];
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                values[z * Size + x] = value(x, z);
        return new TerrainWeightMap(Size, Size, values);
    }

    private static TerrainHeightmap Map(Func<int, int, ushort> sample)
    {
        ushort[] samples = new ushort[Size * Size];
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                samples[z * Size + x] = sample(x, z);
        return new TerrainHeightmap(Size, Size, samples);
    }

    private static TerrainHeightmap Rolling() =>
        Map((x, z) => (ushort)Math.Round((20.0 + Math.Sin(x * 0.05) * 6.0 + Math.Cos(z * 0.07) * 4.0 + 100.0) / 655.35 * 65535.0));

    private static TerrainData Load(TerrainHeightmap map)
    {
        MemoryStream stream = new();
        TerrainImporter.Import(map, s_settings, stream);
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    private static TerrainEdit[] Edits =>
    [
        new(TerrainBrush.Raise, new Double2(140, 150), 18, 6),
        new(TerrainBrush.Crater, new Double2(150, 140), 12, 5),
        new(TerrainBrush.Flatten, new Double2(120, 130), 10, 0.8, 18),
        new(TerrainBrush.Smooth, new Double2(150, 140), 15, 0.7),
        new(TerrainBrush.Raise, new Double2(298, 2), 9, -4),
    ];

    // The finest samples of a terrain as a heightmap, read back from its level 0 pages
    private static TerrainHeightmap Finest(TerrainData data)
    {
        return Map((x, z) =>
        {
            int px = Math.Min(x / TerrainPage.Quads, data.PagesAt(0).X - 1), pz = Math.Min(z / TerrainPage.Quads, data.PagesAt(0).Z - 1);
            TerrainPage page = data.GetPage(new TerrainPageKey(0, px, pz))!;
            return page.Heights[TerrainPage.Index(x - px * TerrainPage.Quads, z - pz * TerrainPage.Quads)];
        });
    }

    [Fact]
    public void Edits_AreTheSameEveryTime()
    {
        using TerrainData a = Load(Rolling()), b = Load(Rolling());
        foreach (TerrainEdit edit in Edits.Concat(SurfaceEdits))
        {
            a.Apply(edit);
            b.Apply(edit);
        }
        foreach (TerrainPageKey key in a.PageKeys)
        {
            Assert.Equal(a.GetPage(key)!.Heights, b.GetPage(key)!.Heights);
            Assert.Equal(a.GetPage(key)!.NormalAo, b.GetPage(key)!.NormalAo);
            Assert.Equal(a.GetPage(key)!.Splat, b.GetPage(key)!.Splat);
            Assert.Equal(a.GetPage(key)!.Holes, b.GetPage(key)!.Holes);
            Assert.Equal(a.GetPage(key)!.Details, b.GetPage(key)!.Details);
        }
    }

    // Every level, normal, occlusion and bound after the edits is what importing the edited heights gives
    [Fact]
    public void Edits_MatchImportingTheEditedHeights()
    {
        using TerrainData edited = Load(Rolling());
        foreach (TerrainEdit edit in Edits)
            edited.Apply(edit);
        using TerrainData fresh = Load(Finest(edited));
        foreach (TerrainPageKey key in fresh.PageKeys)
        {
            Assert.True(fresh.GetPage(key)!.Heights.AsSpan().SequenceEqual(edited.GetPage(key)!.Heights), $"heights {key}");
            Assert.True(fresh.GetPage(key)!.NormalAo.AsSpan().SequenceEqual(edited.GetPage(key)!.NormalAo), $"normals {key}");
            for (int q = 0; q < 4; q++)
            {
                Assert.True(fresh.TryGetQuarterBounds(key, q, out double freshMin, out double freshMax));
                Assert.True(edited.TryGetQuarterBounds(key, q, out double min, out double max));
                Assert.Equal(freshMin, min, 9);
                Assert.Equal(freshMax, max, 9);
            }
        }
    }

    // The edited pages stay when the terrain lets go of pages, so the edit is not lost to a reload from the package
    [Fact]
    public void EditedPages_SurviveUnloading()
    {
        using TerrainData data = Load(Rolling());
        data.Apply(new TerrainEdit(TerrainBrush.Crater, new Double2(60, 60), 10, 6));
        Assert.True(data.TryGetHeight(new Double2(60, 60), out double crater));
        data.Unload(new TerrainPageKey(0, 0, 0));
        data.SetInterest([new Double3(1000, 0, 1000)], 1).Wait(TestContext.Current.CancellationToken);
        Assert.True(data.TryGetHeight(new Double2(60, 60), out double again));
        Assert.Equal(crater, again);
    }

    private static TerrainEdit[] SurfaceEdits =>
    [
        new(TerrainBrush.Raise, new Double2(140, 150), 18, 6),
        new(TerrainBrush.Paint, new Double2(145, 150), 9, 0.8, Layer: 3),
        new(TerrainBrush.Hole, new Double2(290, 290), 6, 1),
        new(TerrainBrush.Detail, new Double2(150, 140), 12, 0.4),
    ];

    private static void AssertSamePages(TerrainData a, TerrainData b)
    {
        Assert.Equal(a.Header, b.Header);
        foreach (TerrainPageKey key in a.PageKeys)
        {
            TerrainPage x = a.GetPage(key)!, y = b.GetPage(key)!;
            Assert.Equal(x.Heights, y.Heights);
            Assert.Equal(x.NormalAo, y.NormalAo);
            Assert.Equal(x.Splat, y.Splat);
            Assert.Equal(x.Holes?.Any(h => h != 0) == true ? x.Holes : null, y.Holes?.Any(h => h != 0) == true ? y.Holes : null);
            Assert.Equal(x.Details?.Any(d => d != 0) == true ? x.Details : null, y.Details?.Any(d => d != 0) == true ? y.Details : null);
            for (int q = 0; q < 4; q++)
            {
                a.TryGetQuarterBounds(key, q, out double minA, out double maxA);
                b.TryGetQuarterBounds(key, q, out double minB, out double maxB);
                Assert.Equal((minA, maxA), (minB, maxB));
            }
        }
    }

    [Fact]
    public void Save_KeepsEditsAndTrees()
    {
        using TerrainData data = Load(Rolling());
        foreach (TerrainEdit edit in SurfaceEdits)
            data.Apply(edit);
        data.AddTree(new TerrainTree(1, new Double3(10, 20, 30), 0.5f, 1.2f, 0.9f, TerrainTree.White));
        MemoryStream stream = new();
        data.Save(stream);
        stream.Position = 0;
        using TerrainData saved = TerrainData.Load(stream);
        AssertSamePages(data, saved);
        Assert.Equal(data.Trees, saved.Trees);

        TerrainExport export = data.Export();
        MemoryStream rebuiltStream = new();
        TerrainImporter.Import(export.Heights, export.Settings, rebuiltStream);
        rebuiltStream.Position = 0;
        using TerrainData rebuilt = TerrainData.Load(rebuiltStream);
        AssertSamePages(data, rebuilt);
    }

    [Fact]
    public void Restore_PutsBackWhatWasCaptured_AndSaysWhichPagesChanged()
    {
        using TerrainData data = Load(Rolling()), untouched = Load(Rolling());
        TerrainState before = data.Capture();
        Assert.True(data.TryGetHeight(new Double2(140, 150), out double ground));
        TerrainEdited lifted = data.Apply(SurfaceEdits[0]);
        foreach (TerrainEdit edit in SurfaceEdits.Skip(1))
            data.Apply(edit);
        Assert.True(data.TryGetHeight(new Double2(140, 150), out double raisedHeight));
        Assert.Equal(ground + 6, raisedHeight, 2);
        Assert.Contains(lifted.Pages, k => k.Level == data.Levels - 1);
        data.AddTree(new TerrainTree(0, new Double3(5, 0, 5), 0, 1, 1, TerrainTree.White));
        TerrainState after = data.Capture();
        using TerrainData edited = Load(Rolling());
        foreach (TerrainEdit edit in SurfaceEdits)
            edited.Apply(edit);

        List<TerrainEdited> raised = new();
        data.Edited += raised.Add;
        data.Restore(before);
        AssertSamePages(untouched, data);
        Assert.Empty(data.Trees);
        Assert.Contains(new TerrainPageKey(0, 1, 1), raised.Single().Pages);

        data.Restore(after);
        AssertSamePages(edited, data);
        Assert.Single(data.Trees);
    }
}
