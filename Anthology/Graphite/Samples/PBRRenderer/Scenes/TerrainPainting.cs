using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.Rendering.Tools;
using Prowl.Vector;

namespace Prowl.Graphite.Samples.PBRRenderer;

/// <summary>Where a layer grows in the sample: soft windows over height and slope, varied by noise.</summary>
internal sealed class LayerRule
{
    public required int Layer { get; init; }
    public double MinHeight { get; init; } = double.NegativeInfinity;
    public double MaxHeight { get; init; } = double.PositiveInfinity;
    public double HeightFade { get; init; } = 10.0;
    public double MinSlope { get; init; }
    public double MaxSlope { get; init; } = 90.0;
    public double SlopeFade { get; init; } = 5.0;
    public double Weight { get; init; } = 1.0;
    public double NoiseStrength { get; init; }
    public double NoiseSize { get; init; } = 20.0;
}

/// <summary>Where a prototype stands in the sample: per 100 square metres, kept apart by its footprint, masked by slope, height, a layer and noise.</summary>
internal sealed class PlacementRule
{
    public required int Prototype { get; init; }
    public float Density { get; init; } = 1f;
    public float Footprint { get; init; } = 1f;
    public float MaxSteepness { get; init; } = float.PositiveInfinity;
    public double MinHeight { get; init; } = double.NegativeInfinity;
    public double MaxHeight { get; init; } = double.PositiveInfinity;
    public int Layer { get; init; } = -1;
    public float MinLayerWeight { get; init; } = 0.5f;
    public float NoiseSize { get; init; }
    public float NoiseThreshold { get; init; } = 0.5f;
    public int NoiseSeed { get; init; }
    public float MinScale { get; init; } = 1f;
    public float MaxScale { get; init; } = 1f;
    public float Sink { get; init; }
}

/// <summary>Paints the sample terrain's splat weights from rules, and places its trees and rocks as a list, as a level tool would before import.</summary>
internal static class TerrainPainting
{
    /// <summary>Each layer's weight at every sample of a heightmap, from the rules.</summary>
    public static TerrainWeightMap[] Layers(TerrainHeightmap map, double spacing, double range, int layers, IReadOnlyList<LayerRule> rules)
    {
        byte[][] values = new byte[layers][];
        for (int l = 0; l < layers; l++)
            values[l] = new byte[map.Width * map.Height];
        double unit = range / 65535.0;
        Parallel.For(0, map.Height, z =>
        {
            Span<double> weights = stackalloc double[layers];
            for (int x = 0; x < map.Width; x++)
            {
                double h = map.Samples[z * map.Width + x] * unit;
                double slope = Slope(map, x, z, spacing, unit);
                weights.Clear();
                foreach (LayerRule rule in rules)
                {
                    double w = rule.Weight * Window(h, rule.MinHeight, rule.MaxHeight, rule.HeightFade) * Window(slope, rule.MinSlope, rule.MaxSlope, rule.SlopeFade);
                    if (rule.NoiseStrength > 0)
                        w *= 1 + rule.NoiseStrength * Noise(x * spacing / rule.NoiseSize, z * spacing / rule.NoiseSize, rule.Layer);
                    weights[rule.Layer] += Math.Max(w, 0);
                }
                double total = 0;
                foreach (double w in weights)
                    total += w;
                for (int l = 0; l < layers; l++)
                    values[l][z * map.Width + x] = (byte)Math.Round((total > 0 ? weights[l] / total : l == 0 ? 1 : 0) * 255);
            }
        });
        TerrainWeightMap[] result = new TerrainWeightMap[layers];
        for (int l = 0; l < layers; l++)
            result[l] = new TerrainWeightMap(map.Width, map.Height, values[l]);
        return result;
    }

    /// <summary>A density map that is the sum of some layers' weights, each times its share.</summary>
    public static TerrainWeightMap Density(TerrainWeightMap[] layers, params (int Layer, float Share)[] shares)
    {
        TerrainWeightMap first = layers[0];
        byte[] values = new byte[first.Values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double sum = 0;
            foreach ((int layer, float share) in shares)
                sum += layers[layer].Values[i] * share;
            values[i] = (byte)Math.Clamp(Math.Round(sum), 0, 255);
        }
        return new TerrainWeightMap(first.Width, first.Height, values);
    }

    /// <summary>
    /// Trees from the rules, from the terrain's corner: each rule's jittered grid, its points kept by its masks, then
    /// dropped where an earlier rule's or point's footprint already stands.
    /// </summary>
    public static List<TerrainTree> Trees(TerrainHeightmap map, TerrainWeightMap[] layers, double spacing, double range, IReadOnlyList<PlacementRule> rules, ulong seed)
    {
        double unit = range / 65535.0;
        double sizeX = (map.Width - 1) * spacing, sizeZ = (map.Height - 1) * spacing;
        List<TerrainTree> trees = new();
        const double Cell = 4.0;
        int gridX = (int)Math.Ceiling(sizeX / Cell) + 1, gridZ = (int)Math.Ceiling(sizeZ / Cell) + 1;
        List<(double X, double Z, float Radius)>[] grid = new List<(double, double, float)>[gridX * gridZ];
        for (int r = 0; r < rules.Count; r++)
        {
            PlacementRule rule = rules[r];
            double step = Math.Sqrt(100.0 / Math.Max(rule.Density, 1e-3));
            int countX = (int)(sizeX / step), countZ = (int)(sizeZ / step);
            for (int gz = 0; gz < countZ; gz++)
                for (int gx = 0; gx < countX; gx++)
                {
                    ulong h = Mix((ulong)(uint)gx * 0x9E3779B97F4A7C15ul ^ (ulong)(uint)gz * 0xC2B2AE3D27D4EB4Ful ^ (ulong)r * 0xD1B54A32D192ED03ul ^ seed);
                    double x = (gx + Unit(h)) * step, z = (gz + Unit(Mix(h ^ 1))) * step;
                    int sx = Math.Clamp((int)Math.Round(x / spacing), 0, map.Width - 1), sz = Math.Clamp((int)Math.Round(z / spacing), 0, map.Height - 1);
                    double ground = Height(map, x / spacing, z / spacing) * unit;
                    double slope = Slope(map, sx, sz, spacing, unit);
                    double steepness = 1 - Math.Cos(slope * Math.PI / 180);
                    if (steepness > rule.MaxSteepness || ground < rule.MinHeight || ground > rule.MaxHeight)
                        continue;
                    if (rule.Layer >= 0 && layers[rule.Layer].Values[sz * map.Width + sx] < rule.MinLayerWeight * 255)
                        continue;
                    if (rule.NoiseSize > 0 && Noise(x / rule.NoiseSize, z / rule.NoiseSize, rule.NoiseSeed) * 0.5 + 0.5 < rule.NoiseThreshold)
                        continue;
                    if (Crowded(grid, gridX, gridZ, x, z, rule.Footprint, Cell))
                        continue;
                    grid[(int)(z / Cell) * gridX + (int)(x / Cell)] ??= new();
                    grid[(int)(z / Cell) * gridX + (int)(x / Cell)].Add((x, z, rule.Footprint));
                    float scale = rule.MinScale + (rule.MaxScale - rule.MinScale) * (float)Unit(Mix(h ^ 2));
                    trees.Add(new TerrainTree(rule.Prototype, new Double3(x, ground - rule.Sink * scale, z), (float)(Unit(Mix(h ^ 3)) * Math.Tau), scale, scale, TerrainTree.White));
                }
        }
        return trees;
    }

    private static bool Crowded(List<(double X, double Z, float Radius)>[] grid, int gridX, int gridZ, double x, double z, float radius, double cell)
    {
        int cx = (int)(x / cell), cz = (int)(z / cell), reach = (int)Math.Ceiling(6 / cell);
        for (int j = Math.Max(cz - reach, 0); j <= Math.Min(cz + reach, gridZ - 1); j++)
            for (int i = Math.Max(cx - reach, 0); i <= Math.Min(cx + reach, gridX - 1); i++)
                if (grid[j * gridX + i] is { } placed)
                    foreach ((double px, double pz, float r) in placed)
                    {
                        double apart = Math.Max(r, radius);
                        if ((px - x) * (px - x) + (pz - z) * (pz - z) < apart * apart)
                            return true;
                    }
        return false;
    }

    private static double Height(TerrainHeightmap map, double u, double v)
    {
        int x0 = Math.Clamp((int)u, 0, map.Width - 2), z0 = Math.Clamp((int)v, 0, map.Height - 2);
        double fx = Math.Clamp(u - x0, 0, 1), fz = Math.Clamp(v - z0, 0, 1);
        double a = map.Samples[z0 * map.Width + x0], b = map.Samples[z0 * map.Width + x0 + 1];
        double c = map.Samples[(z0 + 1) * map.Width + x0], d = map.Samples[(z0 + 1) * map.Width + x0 + 1];
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
    }

    // Degrees from flat by central differences
    private static double Slope(TerrainHeightmap map, int x, int z, double spacing, double unit)
    {
        double dx = (map.Samples[z * map.Width + Math.Min(x + 1, map.Width - 1)] - map.Samples[z * map.Width + Math.Max(x - 1, 0)]) * unit / (2 * spacing);
        double dz = (map.Samples[Math.Min(z + 1, map.Height - 1) * map.Width + x] - map.Samples[Math.Max(z - 1, 0) * map.Width + x]) * unit / (2 * spacing);
        return Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180.0 / Math.PI;
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

    // Smooth value noise from -1 to 1
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

    private static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9ul;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBul;
        return x ^ x >> 31;
    }

    private static double Unit(ulong x) => (x >> 11) * (1.0 / (1ul << 53));
}
