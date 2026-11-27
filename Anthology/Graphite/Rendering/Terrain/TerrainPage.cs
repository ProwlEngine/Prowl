using System;

namespace Prowl.Graphite.Rendering;

/// <summary>A page's level, 0 for the finest, and its column and row among that level's pages.</summary>
public readonly record struct TerrainPageKey(int Level, int X, int Z);

/// <summary>
/// One page of a level: <see cref="Quads"/> quads a side at the level's spacing, stored with a one sample apron on
/// every side, so <see cref="Size"/> samples a side with sample (-1, -1) first.
/// </summary>
public sealed class TerrainPage
{
    public const int Quads = 128;
    public const int Apron = 1;
    public const int Size = Quads + 1 + 2 * Apron;
    public const int Samples = Size * Size;

    public TerrainPage(TerrainPageKey key, ushort[] heights, byte[] normalAo, byte[]? splat = null, byte[]? holes = null, byte[]? details = null)
    {
        if (heights.Length != Samples || normalAo.Length != Samples * 4 || splat != null && (splat.Length == 0 || splat.Length % (Samples * 4) != 0) ||
            holes != null && holes.Length != Samples || details != null && (details.Length == 0 || details.Length % Samples != 0))
            throw new ArgumentException("Page layers must hold one value per sample.");
        Key = key;
        Heights = heights;
        NormalAo = normalAo;
        Splat = splat;
        Holes = holes;
        Details = details;
    }

    public TerrainPageKey Key { get; }

    /// <summary>Heights over the terrain's range, 0 at its bottom and 65535 at its top.</summary>
    public ushort[] Heights { get; }

    /// <summary>Four bytes a sample: the smooth normal's x and z from -1 at 0 to 1 at 255, ambient occlusion, cavity.</summary>
    public byte[] NormalAo { get; }

    /// <summary>
    /// The layers' weights, one RGBA map a slice of four layers: layer l of sample i at (l / 4 * Samples + i) * 4 + l % 4,
    /// 0 to 255. Null where the level keeps none, where the ground is all layer 0.
    /// </summary>
    public byte[]? Splat { get; }

    /// <summary>Per sample 1 where the quad whose lowest corner it is is cut out, or null for none.</summary>
    public byte[]? Holes { get; }

    /// <summary>Each detail prototype's density, a plane each: prototype d of sample i at d * Samples + i, 0 to 255. Finest level only.</summary>
    public byte[]? Details { get; }

    public int SplatMaps => Splat == null ? 0 : Splat.Length / (Samples * 4);
    public int DetailCount => Details == null ? 0 : Details.Length / Samples;

    /// <summary>The index of the sample at x and z from the page's corner, -1 to 129.</summary>
    public static int Index(int x, int z) => (z + Apron) * Size + x + Apron;

    /// <summary>A layer's weight at a sample, 0 to 1.</summary>
    public float Weight(int layer, int index)
    {
        if (Splat == null)
            return layer == 0 ? 1f : 0f;
        int map = layer >> 2;
        return map < SplatMaps ? Splat[(map * Samples + index) * 4 + (layer & 3)] / 255f : 0f;
    }

    public bool IsHole(int index) => Holes != null && Holes[index] != 0;

    /// <summary>A detail prototype's density at a sample, 0 to 1.</summary>
    public float Density(int prototype, int index) =>
        Details != null && (uint)prototype < (uint)DetailCount ? Details[prototype * Samples + index] / 255f : 0f;

    /// <summary>A copy whose layers can change without touching this one.</summary>
    public TerrainPage Clone() => new(Key, (ushort[])Heights.Clone(), (byte[])NormalAo.Clone(), (byte[]?)Splat?.Clone(), (byte[]?)Holes?.Clone(), (byte[]?)Details?.Clone());
}

/// <summary>
/// The one triangle split the renderer, physics and placement share: every quad splits along its diagonal from the
/// lowest x and z corner to the highest.
/// </summary>
public static class TerrainTriangles
{
    /// <summary>The height at fx and fz across a quad with corners h00, h10 (+x), h01 (+z) and h11.</summary>
    public static double Height(double h00, double h10, double h01, double h11, double fx, double fz) =>
        fx >= fz ? h00 + fx * (h10 - h00) + fz * (h11 - h10) : h00 + fz * (h01 - h00) + fx * (h11 - h01);

    /// <summary>The upward normal of the triangle at fx and fz, for corners in metres over a quad of the given spacing.</summary>
    public static (double X, double Y, double Z) Normal(double h00, double h10, double h01, double h11, double fx, double fz, double spacing)
    {
        (double dx, double dz) = fx >= fz ? (h10 - h00, h11 - h10) : (h11 - h01, h01 - h00);
        double length = Math.Sqrt(dx * dx + dz * dz + spacing * spacing);
        return (-dx / length, spacing / length, -dz / length);
    }
}
