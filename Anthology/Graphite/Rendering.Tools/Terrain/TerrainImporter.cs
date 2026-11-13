using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>Where a heightmap sits in the world and how its package is built.</summary>
public sealed class TerrainImportSettings
{
    /// <summary>Metres between finest samples.</summary>
    public double Spacing { get; init; } = 1.0;

    /// <summary>The height of a 0 sample and the metres from it to a 65535 sample.</summary>
    public double Bottom { get; init; }
    public double Range { get; init; } = 4096.0;

    /// <summary>Levels keep control samples while their spacing is at most this many metres.</summary>
    public double ControlMaxSpacing { get; init; } = 4.0;

    public ulong Seed { get; init; }

    /// <summary>Rules that bake each sample's two strongest layers into its control, none for every sample on layer 0.</summary>
    public IReadOnlyList<TerrainLayerRule> Layers { get; init; } = [];
}

/// <summary>
/// Where a layer grows: its weight is the product of soft windows over height and slope, varied by noise. A sample takes
/// its strongest layer as base and the next as overlay, blended by their weights.
/// </summary>
public sealed class TerrainLayerRule
{
    public required int Layer { get; init; }

    /// <summary>The height window in metres and how far beyond it the weight fades out.</summary>
    public double MinHeight { get; init; } = double.NegativeInfinity;
    public double MaxHeight { get; init; } = double.PositiveInfinity;
    public double HeightFade { get; init; } = 10.0;

    /// <summary>The slope window in degrees from flat and how far beyond it the weight fades out.</summary>
    public double MinSlope { get; init; }
    public double MaxSlope { get; init; } = 90.0;
    public double SlopeFade { get; init; } = 5.0;

    /// <summary>The weight before the windows, so a broad rule can sit under narrower ones.</summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>How much value noise of this many metres varies the weight, 0 to 1.</summary>
    public double NoiseStrength { get; init; }
    public double NoiseSize { get; init; } = 20.0;
}

/// <summary>
/// Builds a terrain package from a heightmap: every level from the finest up to one page, each level keeping every
/// other sample of the one under it so parents meet their children exactly, then each page's normals, ambient
/// occlusion, cavity, holes and quarter bounds.
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
        int quadsX = map.Width - 1, quadsZ = map.Height - 1;
        int levels = TerrainHeader.LevelsFor(quadsX, quadsZ);
        int controlLevels = 1;
        while (controlLevels < levels && settings.Spacing * (1 << controlLevels) <= settings.ControlMaxSpacing)
            controlLevels++;
        TerrainHeader header = new()
        {
            Spacing = settings.Spacing,
            Bottom = settings.Bottom,
            Range = settings.Range,
            QuadsX = quadsX,
            QuadsZ = quadsZ,
            Levels = levels,
            ControlLevels = controlLevels,
            Seed = settings.Seed,
        };

        TerrainPackage.Writer writer = TerrainPackage.Begin(stream, header);
        ushort[]? below = null;
        for (int level = 0; level < levels; level++)
        {
            int pagesX = header.PagesX(level), pagesZ = header.PagesZ(level);
            ushort[] bounds = new ushort[pagesX * pagesZ * 8];
            Level source = new(map, level, settings.Spacing * (1 << level), settings.Range / 65535.0);
            bool control = level < controlLevels && (map.Holes != null || settings.Layers.Count > 0);
            for (int pz = 0; pz < pagesZ; pz++)
            {
                TerrainPage[] row = new TerrainPage[pagesX];
                int z = pz;
                Parallel.For(0, pagesX, px =>
                {
                    row[px] = BuildPage(source, new TerrainPageKey(level, px, z), control ? settings : null);
                    QuarterBounds(row[px], below, header, bounds.AsSpan((z * pagesX + px) * 8, 8));
                });
                for (int px = 0; px < pagesX; px++)
                    writer.Add(row[px], bounds.AsSpan((pz * pagesX + px) * 8, 8));
            }
            below = bounds;
        }
        writer.Finish();
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

    private static TerrainPage BuildPage(in Level source, TerrainPageKey key, TerrainImportSettings? control)
    {
        ushort[] heights = new ushort[TerrainPage.Samples];
        byte[] normalAo = new byte[TerrainPage.Samples * 4];
        uint[]? controls = control != null ? new uint[TerrainPage.Samples] : null;
        int x0 = key.X * TerrainPage.Quads, z0 = key.Z * TerrainPage.Quads;
        for (int z = -TerrainPage.Apron; z <= TerrainPage.Quads + TerrainPage.Apron; z++)
            for (int x = -TerrainPage.Apron; x <= TerrainPage.Quads + TerrainPage.Apron; x++)
            {
                int i = x0 + x, j = z0 + z, index = TerrainPage.Index(x, z);
                heights[index] = source[i, j];
                TerrainBake.Sample(source, i, j, source.Spacing, normalAo.AsSpan(index * 4, 4));
                if (controls != null)
                    controls[index] = Control(source, i, j, control!);
            }
        return new TerrainPage(key, heights, normalAo, controls);
    }

    // The sample's two strongest layers by the rules, read at the finest sample it stands on, and its hole bit
    private static uint Control(in Level source, int i, int j, TerrainImportSettings settings)
    {
        TerrainHeightmap map = source.Map;
        int index = source.Index(i, j);
        int x = index % map.Width, z = index / map.Width;
        uint bits = map.Holes != null && map.Holes[index] ? TerrainControl.Hole : 0u;
        if (settings.Layers.Count == 0)
            return bits;

        double unit = settings.Range / 65535.0;
        double h = settings.Bottom + map.Samples[index] * unit;
        double dx = (map.Samples[z * map.Width + Math.Min(x + 1, map.Width - 1)] - map.Samples[z * map.Width + Math.Max(x - 1, 0)]) * unit / (2 * settings.Spacing);
        double dz = (map.Samples[Math.Min(z + 1, map.Height - 1) * map.Width + x] - map.Samples[Math.Max(z - 1, 0) * map.Width + x]) * unit / (2 * settings.Spacing);
        double slope = Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180.0 / Math.PI;

        int first = 0, second = 0;
        double best = 0, next = 0;
        foreach (TerrainLayerRule rule in settings.Layers)
        {
            double w = rule.Weight * Window(h, rule.MinHeight, rule.MaxHeight, rule.HeightFade) * Window(slope, rule.MinSlope, rule.MaxSlope, rule.SlopeFade);
            if (rule.NoiseStrength > 0)
                w *= 1 + rule.NoiseStrength * Noise(x * settings.Spacing / rule.NoiseSize, z * settings.Spacing / rule.NoiseSize, rule.Layer + (int)settings.Seed);
            if (w > best)
            {
                (second, next) = (first, best);
                (first, best) = (rule.Layer, w);
            }
            else if (w > next)
                (second, next) = (rule.Layer, w);
        }
        double blend = best + next > 0 ? next / (best + next) : 0;
        return bits | (uint)(first & 63) | (uint)(second & 63) << 6 | (uint)Math.Round(blend * 255) << 12;
    }

    // 1 inside the window, falling smoothly to 0 over fade beyond either end
    private static double Window(double value, double min, double max, double fade)
    {
        double outside = Math.Max(min - value, value - max);
        if (outside <= 0)
            return 1;
        double t = 1 - Math.Min(outside / Math.Max(fade, 1e-6), 1);
        return t * t * (3 - 2 * t);
    }

    // Smooth value noise from -1 to 1, the same on every machine
    private static double Noise(double x, double z, int seed)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        double tx = x - fx, tz = z - fz;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        int ix = (int)fx, iz = (int)fz;
        double a = Hash(ix, iz, seed), b = Hash(ix + 1, iz, seed), c = Hash(ix, iz + 1, seed), d = Hash(ix + 1, iz + 1, seed);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    private static double Hash(int x, int z, int seed)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
        h ^= h >> 13;
        h *= 0x85EBCA6Bu;
        h ^= h >> 16;
        return h / (double)uint.MaxValue * 2 - 1;
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
