using System;

namespace Prowl.Graphite.Rendering;

/// <summary>A level's heights in metres above the terrain's bottom by sample, held at the world's edge.</summary>
internal interface ITerrainSamples
{
    double Metres(int i, int j);
}

/// <summary>What the importer and runtime edits both bake from heights, so an edit gives what importing it would.</summary>
internal static class TerrainBake
{
    /// <summary>The farthest sample, in a level's own samples, the bake of one sample reads.</summary>
    public const int Reach = 32;

    // Horizon search for ambient occlusion: eight directions, out to these distances in samples
    private static readonly int[] s_horizonSteps = [1, 2, 3, 4, 6, 8, 12, 16, 24, Reach];
    private static readonly (double X, double Z)[] s_directions = Directions();

    /// <summary>Normal x and z from central differences, horizon based ambient occlusion and a cavity mask from the four neighbours.</summary>
    public static void Sample<T>(in T source, int i, int j, double spacing, Span<byte> output) where T : struct, ITerrainSamples
    {
        double s = spacing;
        double h = source.Metres(i, j);
        double left = source.Metres(i - 1, j), right = source.Metres(i + 1, j);
        double back = source.Metres(i, j - 1), front = source.Metres(i, j + 1);
        double dx = (right - left) / (2 * s), dz = (front - back) / (2 * s);
        double length = Math.Sqrt(dx * dx + dz * dz + 1);
        output[0] = Unit(-dx / length);
        output[1] = Unit(-dz / length);

        double open = 0;
        foreach ((double ox, double oz) in s_directions)
        {
            double horizon = 0;
            foreach (int step in s_horizonSteps)
            {
                int si = i + (int)Math.Round(ox * step), sj = j + (int)Math.Round(oz * step);
                double distance = Math.Sqrt((si - i) * (si - i) + (sj - j) * (sj - j)) * s;
                double rise = source.Metres(si, sj) - h;
                if (rise > 0)
                    horizon = Math.Max(horizon, rise / Math.Sqrt(rise * rise + distance * distance));
            }
            open += 1 - horizon;
        }
        output[2] = (byte)Math.Round(open / s_directions.Length * 255);

        double cavity = 0.5 + ((left + right + back + front) * 0.25 - h) / s * 0.5;
        output[3] = (byte)Math.Round(Math.Clamp(cavity, 0, 1) * 255);
    }

    /// <summary>Each quarter's lowest and highest sample of a finest level page.</summary>
    public static void FinestBounds(TerrainPage page, Span<ushort> bounds)
    {
        const int Half = TerrainPage.Quads / 2;
        for (int q = 0; q < 4; q++)
        {
            int qx = q & 1, qz = q >> 1;
            ushort min = ushort.MaxValue, max = 0;
            for (int z = qz * Half; z <= (qz + 1) * Half; z++)
                for (int x = qx * Half; x <= (qx + 1) * Half; x++)
                {
                    ushort h = page.Heights[TerrainPage.Index(x, z)];
                    min = Math.Min(min, h);
                    max = Math.Max(max, h);
                }
            bounds[q * 2] = min;
            bounds[q * 2 + 1] = max;
        }
    }

    /// <summary>A coarser page's quarter bounds, each covering exactly one page of the level below, given that page's bounds or null where it has none.</summary>
    public static void CoarseBounds(TerrainPageKey key, TerrainHeader header, Func<int, int, ushort[]?> below, Span<ushort> bounds)
    {
        for (int q = 0; q < 4; q++)
        {
            int qx = q & 1, qz = q >> 1;
            int childX = Math.Min(key.X * 2 + qx, header.PagesX(key.Level - 1) - 1);
            int childZ = Math.Min(key.Z * 2 + qz, header.PagesZ(key.Level - 1) - 1);
            ushort[]? child = below(childX, childZ);
            ushort min = ushort.MaxValue, max = 0;
            for (int c = 0; child != null && c < 4; c++)
            {
                min = Math.Min(min, child[c * 2]);
                max = Math.Max(max, child[c * 2 + 1]);
            }
            bounds[q * 2] = min;
            bounds[q * 2 + 1] = max;
        }
    }

    private static byte Unit(double value) => (byte)Math.Round((Math.Clamp(value, -1, 1) + 1) * 127.5);

    private static (double, double)[] Directions()
    {
        (double, double)[] directions = new (double, double)[8];
        for (int d = 0; d < 8; d++)
            directions[d] = (Math.Cos(d * Math.PI / 4), Math.Sin(d * Math.PI / 4));
        return directions;
    }
}
