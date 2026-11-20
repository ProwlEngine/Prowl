using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>Where a heightmap sits in the world and what the package keeps over it.</summary>
public sealed class TerrainImportSettings
{
    /// <summary>Metres between finest samples.</summary>
    public double Spacing { get; init; } = 1.0;

    /// <summary>The height of a 0 sample and the metres from it to a 65535 sample.</summary>
    public double Bottom { get; init; }
    public double Range { get; init; } = 4096.0;

    /// <summary>Levels keep splat weights and holes while their spacing is at most this many metres.</summary>
    public double SplatMaxSpacing { get; init; } = 4.0;

    /// <summary>The seed detail placement hashes with.</summary>
    public ulong Seed { get; init; }

    /// <summary>Each material layer's weight over the terrain, normalised per sample. None for a world of one layer.</summary>
    public IReadOnlyList<TerrainWeightMap> Layers { get; init; } = [];

    /// <summary>Each detail prototype's density over the terrain, 0 to 255 of the prototype's full density.</summary>
    public IReadOnlyList<TerrainWeightMap> Details { get; init; } = [];

    public IReadOnlyList<TerrainTree> Trees { get; init; } = [];
}

/// <summary>
/// A map of bytes stretched over the whole terrain, row by row from z 0, its texel centres spread evenly from corner to
/// corner, read bilinear at each sample. Any resolution: a Unity alphamap or detail layer goes in as it is.
/// </summary>
public sealed class TerrainWeightMap
{
    public TerrainWeightMap(int width, int height, byte[] values)
    {
        if (width < 1 || height < 1 || values.Length != width * height)
            throw new ArgumentException("A weight map needs one value per texel.");
        Width = width;
        Height = height;
        Values = values;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Values { get; }

    /// <summary>The value at u and v from 0 to 1 across the terrain, 0 to 255.</summary>
    public double Sample(double u, double v)
    {
        double x = Math.Clamp(u * Width - 0.5, 0, Width - 1), z = Math.Clamp(v * Height - 0.5, 0, Height - 1);
        int x0 = (int)x, z0 = (int)z, x1 = Math.Min(x0 + 1, Width - 1), z1 = Math.Min(z0 + 1, Height - 1);
        double fx = x - x0, fz = z - z0;
        double a = Values[z0 * Width + x0], b = Values[z0 * Width + x1], c = Values[z1 * Width + x0], d = Values[z1 * Width + x1];
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
    }
}

/// <summary>
/// Builds a terrain package from a heightmap and the maps over it: every level from the finest up to one page, each level
/// keeping every other sample of the one under it so parents meet their children exactly, then each page's normals,
/// ambient occlusion, cavity, quarter bounds, splat weights and holes, and the finest pages' detail densities.
/// </summary>
public static class TerrainImporter
{
    public static void Import(TerrainHeightmap map, TerrainImportSettings settings, string path)
    {
        using FileStream file = new(path, FileMode.Create, FileAccess.ReadWrite);
        Import(map, settings, file);
    }

    public static void Import(TerrainHeightmap map, TerrainImportSettings settings, Stream stream)
    {
        if (settings.Layers.Count > TerrainLayers.MaxLayers)
            throw new ArgumentException($"A terrain has at most {TerrainLayers.MaxLayers} layers.", nameof(settings));
        int quadsX = map.Width - 1, quadsZ = map.Height - 1;
        int levels = TerrainHeader.LevelsFor(quadsX, quadsZ);
        int splatLevels = 1;
        while (splatLevels < levels && settings.Spacing * (1 << splatLevels) <= settings.SplatMaxSpacing)
            splatLevels++;
        TerrainHeader header = new()
        {
            Spacing = settings.Spacing,
            Bottom = settings.Bottom,
            Range = settings.Range,
            QuadsX = quadsX,
            QuadsZ = quadsZ,
            Levels = levels,
            SplatLevels = splatLevels,
            SplatMaps = settings.Layers.Count > 1 ? (settings.Layers.Count + 3) / 4 : 0,
            Details = settings.Details.Count,
            Seed = settings.Seed,
        };
        Surface surface = new(map, settings, header);

        TerrainPackage.Writer writer = TerrainPackage.Begin(stream, header);
        ushort[]? below = null;
        for (int level = 0; level < levels; level++)
        {
            int pagesX = header.PagesX(level), pagesZ = header.PagesZ(level);
            ushort[] bounds = new ushort[pagesX * pagesZ * 8];
            Level source = new(map, level, settings.Spacing * (1 << level), settings.Range / 65535.0);
            for (int pz = 0; pz < pagesZ; pz++)
            {
                TerrainPage[] row = new TerrainPage[pagesX];
                int z = pz;
                Parallel.For(0, pagesX, px =>
                {
                    row[px] = BuildPage(source, new TerrainPageKey(level, px, z), level < splatLevels ? surface : null);
                    QuarterBounds(row[px], below, header, bounds.AsSpan((z * pagesX + px) * 8, 8));
                });
                for (int px = 0; px < pagesX; px++)
                    writer.Add(row[px], bounds.AsSpan((pz * pagesX + px) * 8, 8));
            }
            below = bounds;
        }
        writer.Finish(settings.Trees);
    }

    // A level's view of the finest samples: sample i, j of level n is finest sample i << n, j << n, held at the world's edge
    private readonly struct Level(TerrainHeightmap map, int level, double spacing, double metresPerUnit) : ITerrainSamples
    {
        public readonly TerrainHeightmap Map = map;
        public readonly int Number = level;
        public readonly double Spacing = spacing;
        public readonly double MetresPerUnit = metresPerUnit;

        public int Index(int i, int j)
        {
            int x = Math.Clamp(i << Number, 0, Map.Width - 1), z = Math.Clamp(j << Number, 0, Map.Height - 1);
            return z * Map.Width + x;
        }

        public ushort this[int i, int j] => Map.Samples[Index(i, j)];

        public double Metres(int i, int j) => this[i, j] * MetresPerUnit;
    }

    // The splat weights, holes and detail densities of every finest sample, which coarser levels read at the sample they stand on
    private sealed class Surface
    {
        public readonly byte[]? Splat;
        public readonly bool[]? Holes;
        public readonly byte[]? Details;
        public readonly int Maps, DetailCount, Width;

        public Surface(TerrainHeightmap map, TerrainImportSettings settings, TerrainHeader header)
        {
            Width = map.Width;
            int samples = map.Width * map.Height;
            Holes = map.Holes;
            Maps = header.SplatMaps;
            DetailCount = header.Details;
            double du = 1.0 / Math.Max(map.Width - 1, 1), dv = 1.0 / Math.Max(map.Height - 1, 1);
            if (Maps > 0)
            {
                byte[] splat = new byte[samples * Maps * 4];
                int layers = settings.Layers.Count;
                Parallel.For(0, map.Height, z =>
                {
                    Span<double> weights = stackalloc double[layers];
                    for (int x = 0; x < map.Width; x++)
                    {
                        double total = 0;
                        for (int l = 0; l < layers; l++)
                            total += weights[l] = settings.Layers[l].Sample(x * du, z * dv);
                        int i = z * map.Width + x;
                        for (int l = 0; l < layers; l++)
                        {
                            double w = total > 0 ? weights[l] / total : l == 0 ? 1 : 0;
                            splat[((l >> 2) * samples + i) * 4 + (l & 3)] = (byte)Math.Round(w * 255);
                        }
                    }
                });
                Splat = splat;
            }
            if (DetailCount > 0)
            {
                byte[] details = new byte[samples * DetailCount];
                Parallel.For(0, DetailCount, d =>
                {
                    TerrainWeightMap density = settings.Details[d];
                    for (int z = 0; z < map.Height; z++)
                        for (int x = 0; x < map.Width; x++)
                            details[d * samples + z * map.Width + x] = (byte)Math.Round(density.Sample(x * du, z * dv));
                });
                Details = details;
            }
        }
    }

    private static TerrainPage BuildPage(in Level source, TerrainPageKey key, Surface? surface)
    {
        ushort[] heights = new ushort[TerrainPage.Samples];
        byte[] normalAo = new byte[TerrainPage.Samples * 4];
        byte[]? splat = surface?.Splat != null ? new byte[TerrainPage.Samples * 4 * surface.Maps] : null;
        byte[]? holes = surface?.Holes != null ? new byte[TerrainPage.Samples] : null;
        byte[]? details = key.Level == 0 && surface?.Details != null ? new byte[TerrainPage.Samples * surface.DetailCount] : null;
        int finest = source.Map.Width * source.Map.Height;
        int x0 = key.X * TerrainPage.Quads, z0 = key.Z * TerrainPage.Quads;
        for (int z = -TerrainPage.Apron; z <= TerrainPage.Quads + TerrainPage.Apron; z++)
            for (int x = -TerrainPage.Apron; x <= TerrainPage.Quads + TerrainPage.Apron; x++)
            {
                int i = x0 + x, j = z0 + z, index = TerrainPage.Index(x, z);
                heights[index] = source[i, j];
                TerrainBake.Sample(source, i, j, source.Spacing, normalAo.AsSpan(index * 4, 4));
                if (surface == null)
                    continue;
                int from = source.Index(i, j);
                if (holes != null && surface.Holes![from])
                    holes[index] = 1;
                if (splat != null)
                    for (int m = 0; m < surface.Maps; m++)
                        for (int c = 0; c < 4; c++)
                            splat[(m * TerrainPage.Samples + index) * 4 + c] = surface.Splat![(m * finest + from) * 4 + c];
                if (details != null)
                    for (int d = 0; d < surface.DetailCount; d++)
                        details[d * TerrainPage.Samples + index] = surface.Details![d * finest + from];
            }
        return new TerrainPage(key, heights, normalAo, splat, holes, details);
    }

    // Each quarter's lowest and highest finest sample: the finest level reads its own samples, every coarser quarter
    // covers exactly one page of the level below
    private static void QuarterBounds(TerrainPage page, ushort[]? below, TerrainHeader header, Span<ushort> bounds)
    {
        if (below == null)
        {
            TerrainBake.FinestBounds(page, bounds);
            return;
        }
        int pagesX = header.PagesX(page.Key.Level - 1);
        TerrainBake.CoarseBounds(page.Key, header, (x, z) => below.AsSpan((z * pagesX + x) * 8, 8).ToArray(), bounds);
    }
}
