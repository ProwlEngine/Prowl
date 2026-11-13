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

    private static readonly TerrainImportSettings s_settings = new()
    {
        Spacing = 1.0,
        Bottom = -100.0,
        Range = 655.35,
        Layers = [new TerrainLayerRule { Layer = 0, MaxHeight = 25, HeightFade = 2 }, new TerrainLayerRule { Layer = 1, MinHeight = 25, HeightFade = 2 }],
    };

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
        foreach (TerrainEdit edit in Edits)
        {
            a.Apply(edit);
            b.Apply(edit);
        }
        foreach (TerrainPageKey key in a.PageKeys)
        {
            Assert.Equal(a.GetPage(key)!.Heights, b.GetPage(key)!.Heights);
            Assert.Equal(a.GetPage(key)!.NormalAo, b.GetPage(key)!.NormalAo);
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

    [Fact]
    public void Raise_LiftsTheGroundInItsRadiusAndSaysWhatChanged()
    {
        using TerrainData data = Load(Rolling());
        Double2 center = new(150, 150), outside = new(150, 175);
        Assert.True(data.TryGetHeight(center, out double before));
        Assert.True(data.TryGetHeight(outside, out double untouched));
        List<TerrainEdited> heard = new();
        data.Edited += heard.Add;

        TerrainEdited result = data.Apply(new TerrainEdit(TerrainBrush.Raise, center, 20, 5));

        Assert.True(data.TryGetHeight(center, out double after));
        Assert.Equal(before + 5, after, 2);
        Assert.True(data.TryGetHeight(outside, out double still));
        Assert.Equal(untouched, still, 9);
        Assert.Single(heard);
        Assert.Equal(result, heard[0]);
        Assert.Contains(new TerrainPageKey(0, 1, 1), result.Pages);
        Assert.Contains(result.Pages, k => k.Level == data.Levels - 1);
        Assert.True(result.Min.X <= 130 && result.Max.X >= 170);
        Assert.True(data.Raycast(new Double3(150, 300, 150), new Float3(0, -1, 0), 1000, out TerrainHit hit));
        Assert.Equal(after, hit.Position.Y, 3);
    }

    // The edited pages stay when the terrain lets go of pages, so the edit is not lost to a reload from the package
    [Fact]
    public void EditedPages_SurviveUnloading()
    {
        using TerrainData data = Load(Rolling());
        data.Apply(new TerrainEdit(TerrainBrush.Crater, new Double2(60, 60), 10, 6));
        Assert.True(data.TryGetHeight(new Double2(60, 60), out double crater));
        data.Unload(new TerrainPageKey(0, 0, 0));
        data.SetInterest([new Double3(1000, 0, 1000)], 1).Wait();
        Assert.True(data.TryGetHeight(new Double2(60, 60), out double again));
        Assert.Equal(crater, again);
    }

    // Every sample on layer 0
    private static TerrainData Grassland()
    {
        MemoryStream stream = new();
        TerrainImporter.Import(Rolling(), new TerrainImportSettings { Spacing = 1.0, Bottom = -100.0, Range = 655.35, Layers = [new TerrainLayerRule { Layer = 0 }] }, stream);
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    private static TerrainData Plain()
    {
        MemoryStream stream = new();
        TerrainImporter.Import(Rolling(), new TerrainImportSettings { Spacing = 1.0, Bottom = -100.0, Range = 655.35, ControlMaxSpacing = 4.0 }, stream);
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    // Every level that keeps control holds, at each sample, the control of the finest sample it stands on
    private static void AssertLevelsFollowTheFinest(TerrainData data)
    {
        TerrainHeader header = data.Header;
        foreach (TerrainPageKey key in data.PageKeys)
        {
            TerrainPage page = data.GetPage(key)!;
            if (key.Level >= header.ControlLevels)
            {
                Assert.Null(page.Control);
                continue;
            }
            for (int z = -1; z <= TerrainPage.Quads + 1; z++)
                for (int x = -1; x <= TerrainPage.Quads + 1; x++)
                {
                    int i = Math.Clamp((key.X * TerrainPage.Quads + x) << key.Level, 0, header.QuadsX);
                    int j = Math.Clamp((key.Z * TerrainPage.Quads + z) << key.Level, 0, header.QuadsZ);
                    int px = Math.Min(i / TerrainPage.Quads, data.PagesAt(0).X - 1), pz = Math.Min(j / TerrainPage.Quads, data.PagesAt(0).Z - 1);
                    uint[]? finest = data.GetPage(new TerrainPageKey(0, px, pz))!.Control;
                    uint expected = finest == null ? 0u : finest[TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads)];
                    uint actual = page.Control == null ? 0u : page.Control[TerrainPage.Index(x, z)];
                    Assert.True(expected == actual, $"{key} {x} {z}: {expected:X} {actual:X}");
                }
        }
    }

    [Fact]
    public void Paint_PutsItsLayerUnderTheBrush()
    {
        using TerrainData data = Grassland();
        Double2 center = new(150, 150), away = new(150, 170);
        Assert.True(data.TryGetLayerWeight(center, 0, out float before) && before > 0.99f);
        TerrainEdited edited = data.Apply(new TerrainEdit(TerrainBrush.Paint, center, 12, 1, Layer: 1));

        Assert.True(data.TryGetLayerWeight(center, 1, out float painted));
        Assert.True(painted > 0.95f, $"{painted}");
        Assert.True(data.TryGetLayerWeight(away, 0, out float untouched) && untouched > 0.99f);
        Assert.True(data.TryGetLayerWeight(new Double2(150, 159), 1, out float edge) && edge > 0.05f && edge < 0.95f, $"{edge}");
        Assert.Contains(edited.Pages, k => k.Level > 0);
        AssertLevelsFollowTheFinest(data);
    }

    [Fact]
    public void Holes_CutAndFillWherePhysicsLooks()
    {
        using TerrainData data = Plain();
        Double2 center = new(100, 100);
        Assert.Null(data.GetPage(new TerrainPageKey(0, 0, 0))!.Control);
        data.Apply(new TerrainEdit(TerrainBrush.Hole, center, 4, 1));

        Assert.True(data.IsHole(center));
        Assert.False(data.TryGetHeight(center, out _));
        Assert.False(data.IsHole(new Double2(100, 106)));
        Assert.False(data.Raycast(new Double3(100.5, 300, 100.5), new Float3(0, -1, 0), 1000, out _));
        List<TerrainTriangle> triangles = new();
        data.GetTriangles(new Double3(99, -1000, 99), new Double3(101, 1000, 101), triangles);
        Assert.Empty(triangles);
        AssertLevelsFollowTheFinest(data);

        data.Apply(new TerrainEdit(TerrainBrush.Hole, center, 10, -1));
        Assert.False(data.IsHole(center));
        Assert.True(data.TryGetHeight(center, out _));
    }

    [Fact]
    public void GrassAndDetailCuts_GrowAndRegrow()
    {
        using TerrainData data = Grassland();
        Double2 center = new(150, 150);
        data.Apply(new TerrainEdit(TerrainBrush.Grass, center, 10, 1));
        data.Apply(new TerrainEdit(TerrainBrush.Detail, center, 10, 1));
        uint control = data.GetPage(new TerrainPageKey(0, 1, 1))!.Control![TerrainPage.Index(150 - 128, 150 - 128)];
        Assert.Equal(TerrainControl.MaxGrassCut, TerrainControl.GrassCut(control));
        Assert.Equal(TerrainControl.MaxDetailCut, TerrainControl.DetailCut(control));
        Assert.Equal(0, TerrainControl.BaseLayer(control));
        Assert.True(data.TryGetDetailCut(center, out float cut) && cut > 0.99f);
        Assert.True(data.TryGetDetailCut(new Double2(150, 165), out float none) && none == 0f);
        AssertLevelsFollowTheFinest(data);

        data.Apply(new TerrainEdit(TerrainBrush.Grass, center, 10, -1));
        control = data.GetPage(new TerrainPageKey(0, 1, 1))!.Control![TerrainPage.Index(150 - 128, 150 - 128)];
        Assert.Equal(0, TerrainControl.GrassCut(control));
        Assert.Equal(TerrainControl.MaxDetailCut, TerrainControl.DetailCut(control));
    }

    [Fact]
    public void ControlEdits_AreTheSameEveryTime()
    {
        using TerrainData a = Load(Rolling()), b = Load(Rolling());
        TerrainEdit[] edits =
        [
            new(TerrainBrush.Paint, new Double2(140, 150), 18, 0.6, Layer: 1),
            new(TerrainBrush.Paint, new Double2(145, 150), 9, 0.8, Layer: 3),
            new(TerrainBrush.Hole, new Double2(290, 290), 6, 1),
            new(TerrainBrush.Grass, new Double2(150, 140), 12, 0.5),
            new(TerrainBrush.Detail, new Double2(150, 140), 12, 0.4),
        ];
        foreach (TerrainEdit edit in edits)
        {
            a.Apply(edit);
            b.Apply(edit);
        }
        foreach (TerrainPageKey key in a.PageKeys)
            Assert.Equal(a.GetPage(key)!.Control, b.GetPage(key)!.Control);
    }

    [Fact]
    public void Edits_OutsideTheWorldChangeNothing()
    {
        using TerrainData data = Load(Rolling());
        TerrainEdited result = data.Apply(new TerrainEdit(TerrainBrush.Raise, new Double2(-500, -500), 10, 5));
        Assert.Empty(result.Pages);
    }
}
