using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>Generated tiling textures for the prop generators: bark, rock, and leaf cluster and needle cards.</summary>
public static class PropTextures
{
    /// <summary>Furrowed bark, its height field for <see cref="BarkNormal"/>.</summary>
    public static PropImage Bark(int size, Float3 color, int seed = 1) =>
        PropImage.Generate(size, true, (u, v) =>
        {
            double h = BarkHeight(u, v, seed);
            double tone = PropNoise.Fbm(u * 4, v * 2, 3, 4, 2, seed + 3) * 0.5 + 0.5;
            float shade = (float)(0.35 + 0.65 * h) * (float)(0.8 + 0.4 * tone);
            return new Float4(color.X * shade, color.Y * shade, color.Z * shade, 1f);
        });

    public static PropImage BarkNormal(int size, int seed = 1) => PropImage.NormalMap(size, (u, v) => BarkHeight(u, v, seed), size / 48.0);

    /// <summary>Speckled, cracked stone, its height field for <see cref="RockNormal"/>.</summary>
    public static PropImage Rock(int size, Float3 color, int seed = 2) =>
        PropImage.Generate(size, true, (u, v) =>
        {
            double h = RockHeight(u, v, seed);
            double speckle = PropNoise.Noise(u * 96, v * 96, 96, 96, seed + 7);
            double tone = PropNoise.Fbm(u * 3, v * 3, 3, 3, 3, seed + 8) * 0.5 + 0.5;
            float shade = (float)(0.55 + 0.45 * h + speckle * 0.08) * (float)(0.85 + 0.3 * tone);
            return new Float4(color.X * shade, color.Y * shade, color.Z * shade, 1f);
        });

    public static PropImage RockNormal(int size, int seed = 2) => PropImage.NormalMap(size, (u, v) => RockHeight(u, v, seed), size / 64.0);

    /// <summary>
    /// A cluster of leaves on a stem from the bottom middle, on clear, for broadleaf and bush cards. Grey to white so
    /// the material's base colour sets the hue, each leaf a little lighter or darker.
    /// </summary>
    public static PropImage Leaves(int size, int seed = 3)
    {
        PropNoise.Random random = new(seed);
        const int Count = 13;
        (double X, double Y, double Angle, double Length, double Width, double Tone)[] leaves = new (double, double, double, double, double, double)[Count];
        for (int i = 0; i < Count; i++)
        {
            double t = (i + 0.5) / Count;
            double side = i % 2 == 0 ? 1 : -1;
            double y = 0.92 - t * 0.78;
            leaves[i] = (0.5 + side * 0.02, y, side * random.Range(0.5, 1.1) - Math.PI / 2 + (i == Count - 1 ? -side * 0.9 : 0), random.Range(0.26, 0.36) * (1.1 - t * 0.35),
                random.Range(0.32, 0.4), random.Range(0.75, 1.0));
        }
        return PropImage.Generate(size, true, (u, v) =>
        {
            float best = 0f, alpha = 0f;
            double stem = Math.Abs(u - 0.5 - (0.92 - v) * 0.03);
            if (v > 0.12 && v < 0.98 && stem < 0.008)
                (best, alpha) = (0.45f, 1f);
            foreach ((double x, double y, double angle, double length, double width, double tone) in leaves)
            {
                double dx = u - x, dy = v - y;
                double ca = Math.Cos(angle), sa = Math.Sin(angle);
                double s = (dx * ca + dy * sa) / length, w = (-dx * sa + dy * ca) / length;
                if (s < 0 || s > 1)
                    continue;
                double edge = width * Math.Pow(Math.Sin(Math.PI * Math.Pow(s, 0.8)), 0.9) * 0.5;
                if (Math.Abs(w) > edge)
                    continue;
                double rib = Math.Abs(w) < 0.012 ? 0.8 : 1.0;
                double vein = 0.92 + 0.08 * Math.Cos((s * 9 + Math.Abs(w) * 14) * Math.PI);
                double grad = 0.8 + 0.2 * (1 - Math.Abs(w) / Math.Max(edge, 1e-4));
                (best, alpha) = ((float)(tone * rib * vein * grad), 1f);
            }
            return new Float4(best, best, best, alpha);
        }, cutout: true);
    }

    /// <summary>A sprig of needles either side of a stem from the bottom middle, on clear, for conifer cards.</summary>
    public static PropImage Needles(int size, int seed = 4) =>
        PropImage.Generate(size, true, (u, v) =>
        {
            double along = 1 - v;
            double stem = u - 0.5;
            if (along < 0.02 || along > 0.98)
                return Float4.Zero;
            if (Math.Abs(stem) < 0.01)
                return new Float4(0.5f, 0.5f, 0.5f, 1f);
            double side = Math.Sign(stem), reach = Math.Abs(stem);
            double spacing = 0.022;
            double root = along - reach * 0.55;
            double k = Math.Round(root / spacing);
            double offset = root - k * spacing;
            double length = 0.34 * Math.Sin(Math.PI * Math.Clamp(k * spacing, 0, 1)) + 0.06;
            double jitter = PropNoise.Unit(PropNoise.Hash((int)k, side > 0 ? 1 : 0, 0, seed));
            length *= 0.75 + 0.35 * jitter;
            if (Math.Abs(offset) > 0.0045 || reach > length)
                return Float4.Zero;
            float tone = (float)(0.7 + 0.3 * (1 - reach / length) * (0.8 + 0.2 * jitter));
            return new Float4(tone, tone, tone, 1f);
        }, cutout: true);

    private static double BarkHeight(double u, double v, int seed)
    {
        double ridges = 1 - Math.Abs(PropNoise.Fbm(u * 6, v * 2, 3, 6, 2, seed) * 1.6);
        double plates = PropNoise.Cells(u * 5, v * 2, 5, 2, seed + 1);
        double grain = PropNoise.Noise(u * 64, v * 8, 64, 8, seed + 2) * 0.1;
        return Math.Clamp(Math.Pow(Math.Max(ridges, 0), 2.5) * 0.75 + plates * 0.3 + grain, 0, 1);
    }

    private static double RockHeight(double u, double v, int seed)
    {
        double broad = PropNoise.Fbm(u * 4, v * 4, 5, 4, 4, seed) * 0.5 + 0.5;
        double cracks = Math.Pow(Math.Min(PropNoise.Cells(u * 7, v * 7, 7, 7, seed + 1) * 1.6, 1), 0.35);
        return Math.Clamp(broad * 0.8 * cracks + 0.15, 0, 1);
    }
}
