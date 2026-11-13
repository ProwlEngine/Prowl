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

    public TerrainPage(TerrainPageKey key, ushort[] heights, byte[] normalAo, uint[]? control)
    {
        if (heights.Length != Samples || normalAo.Length != Samples * 4 || control != null && control.Length != Samples)
            throw new ArgumentException("Page layers must hold one value per sample.");
        Key = key;
        Heights = heights;
        NormalAo = normalAo;
        Control = control;
    }

    public TerrainPageKey Key { get; }

    /// <summary>Heights over the terrain's range, 0 at its bottom and 65535 at its top.</summary>
    public ushort[] Heights { get; }

    /// <summary>Four bytes a sample: the smooth normal's x and z from -1 at 0 to 1 at 255, ambient occlusion, cavity.</summary>
    public byte[] NormalAo { get; }

    /// <summary>Material and hole bits a sample, see <see cref="TerrainControl"/>, or null where the level keeps none.</summary>
    public uint[]? Control { get; }

    /// <summary>The index of the sample at x and z from the page's corner, -1 to 129.</summary>
    public static int Index(int x, int z) => (z + Apron) * Size + x + Apron;
}

/// <summary>
/// The bits of a control sample: base layer 0 to 5, overlay layer 6 to 11, overlay weight 12 to 19, hole 20, how much
/// grass is cut 21 to 28 (0 full, 255 none) and how much detail is cut 29 to 31 (0 full, 7 none).
/// </summary>
public static class TerrainControl
{
    /// <summary>Set when the quad whose lowest corner is this sample is cut out.</summary>
    public const uint Hole = 1u << 20;

    public const int MaxGrassCut = 255, MaxDetailCut = 7;

    public static int BaseLayer(uint control) => (int)(control & 63);
    public static int OverlayLayer(uint control) => (int)(control >> 6 & 63);
    public static int OverlayBlend(uint control) => (int)(control >> 12 & 255);
    public static bool IsHole(uint control) => (control & Hole) != 0;
    public static int GrassCut(uint control) => (int)(control >> 21 & 255);
    public static int DetailCut(uint control) => (int)(control >> 29);

    public static uint Layers(int baseLayer, int overlayLayer, int blend, uint control) =>
        control & ~0xFFFFFu | (uint)(baseLayer & 63) | (uint)(overlayLayer & 63) << 6 | (uint)Math.Clamp(blend, 0, 255) << 12;

    public static uint WithHole(uint control, bool hole) => hole ? control | Hole : control & ~Hole;
    public static uint WithGrassCut(uint control, int cut) => control & ~(255u << 21) | (uint)Math.Clamp(cut, 0, MaxGrassCut) << 21;
    public static uint WithDetailCut(uint control, int cut) => control & ~(7u << 29) | (uint)Math.Clamp(cut, 0, MaxDetailCut) << 29;
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
