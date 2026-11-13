using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Where a ray met the terrain.</summary>
public readonly record struct TerrainHit(Double3 Position, Float3 Normal, double Distance);

/// <summary>A collision triangle of the finest level in world space, its corners counter clockwise seen from above, and its quad's base layer.</summary>
public readonly record struct TerrainTriangle(Double3 A, Double3 B, Double3 C, int Layer);

/// <summary>
/// A terrain package's pages and the queries over them, with no GPU types so a server can use it. Pages load on first
/// use and stay until <see cref="Unload"/>. Heights between samples follow <see cref="TerrainTriangles"/>, so every
/// query agrees with what the renderer draws at the finest level. Safe to query from several threads.
/// </summary>
public sealed partial class TerrainData : IDisposable
{
    private readonly Stream _stream;
    private readonly object _streamLock = new();
    private readonly Dictionary<TerrainPageKey, TerrainPageEntry> _entries;
    private readonly ConcurrentDictionary<TerrainPageKey, TerrainPage?> _pages = new();

    private TerrainData(Stream stream)
    {
        _stream = stream;
        (Header, _entries) = TerrainPackage.ReadDirectory(stream);
    }

    /// <summary>Opens a package file, keeping it open to read pages from.</summary>
    public static TerrainData Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess));

    /// <summary>Reads a package from a seekable stream positioned at its start, which the terrain then owns.</summary>
    public static TerrainData Load(Stream stream) => new(stream);

    public TerrainHeader Header { get; }

    /// <summary>The world position of the finest sample at x 0 and z 0, and the height every sample is lifted by.</summary>
    public Double3 Position { get; set; }

    public int Levels => Header.Levels;
    public double SizeX => Header.QuadsX * Header.Spacing;
    public double SizeZ => Header.QuadsZ * Header.Spacing;

    public double SpacingAt(int level) => Header.Spacing * (1 << level);
    public (int X, int Z) PagesAt(int level) => (Header.PagesX(level), Header.PagesZ(level));

    /// <summary>The page, loading it if needed, or null where the package has none.</summary>
    public TerrainPage? GetPage(TerrainPageKey key) => _pages.GetOrAdd(key, Read);

    /// <summary>Lets go of a loaded page, unless an edit changed it.</summary>
    public void Unload(TerrainPageKey key)
    {
        if (!_edited.ContainsKey(key))
            _pages.TryRemove(key, out _);
    }

    public bool IsLoaded(TerrainPageKey key) => _pages.TryGetValue(key, out TerrainPage? page) && page != null;

    /// <summary>
    /// Keeps the finest pages within a radius of the points loaded, for physics and queries around players and vehicles,
    /// and lets go of the finest pages outside it. Loads in the background, the task finishing once every page is in.
    /// Pages a query needs outside the window still load on demand.
    /// </summary>
    public Task SetInterest(IReadOnlyList<Double3> points, double radius)
    {
        HashSet<TerrainPageKey> wanted = new();
        double size = TerrainPage.Quads * Header.Spacing;
        (int pagesX, int pagesZ) = PagesAt(0);
        foreach (Double3 point in points)
        {
            double x = point.X - Position.X, z = point.Z - Position.Z;
            int x0 = Math.Max((int)Math.Floor((x - radius) / size), 0), x1 = Math.Min((int)Math.Floor((x + radius) / size), pagesX - 1);
            int z0 = Math.Max((int)Math.Floor((z - radius) / size), 0), z1 = Math.Min((int)Math.Floor((z + radius) / size), pagesZ - 1);
            for (int pz = z0; pz <= z1; pz++)
                for (int px = x0; px <= x1; px++)
                {
                    double dx = Math.Max(Math.Max(px * size - x, x - (px + 1) * size), 0);
                    double dz = Math.Max(Math.Max(pz * size - z, z - (pz + 1) * size), 0);
                    if (dx * dx + dz * dz <= radius * radius && _entries.ContainsKey(new TerrainPageKey(0, px, pz)))
                        wanted.Add(new TerrainPageKey(0, px, pz));
                }
        }
        foreach (TerrainPageKey key in _pages.Keys)
        {
            if (key.Level == 0 && !wanted.Contains(key))
                Unload(key);
        }
        return Task.WhenAll(wanted.Where(key => !IsLoaded(key)).Select(key => Task.Run(() => GetPage(key))));
    }

    /// <summary>
    /// Adds the finest level's triangles of every quad whose square overlaps the box in x and z and whose corners reach
    /// into it in y, leaving out holes. They follow <see cref="TerrainTriangles"/>, so they are the ground the renderer
    /// draws up close and the queries answer for.
    /// </summary>
    public void GetTriangles(Double3 min, Double3 max, List<TerrainTriangle> triangles)
    {
        double spacing = Header.Spacing;
        int cx0 = Math.Max((int)Math.Floor((min.X - Position.X) / spacing), 0), cx1 = Math.Min((int)Math.Ceiling((max.X - Position.X) / spacing) - 1, Header.QuadsX - 1);
        int cz0 = Math.Max((int)Math.Floor((min.Z - Position.Z) / spacing), 0), cz1 = Math.Min((int)Math.Ceiling((max.Z - Position.Z) / spacing) - 1, Header.QuadsZ - 1);
        for (int cz = cz0; cz <= cz1; cz++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int px = cx / TerrainPage.Quads, pz = cz / TerrainPage.Quads;
                if (GetPage(new TerrainPageKey(0, px, pz)) is not { } page)
                    continue;
                int lx = cx - px * TerrainPage.Quads, lz = cz - pz * TerrainPage.Quads;
                uint control = page.Control?[TerrainPage.Index(lx, lz)] ?? 0;
                if (TerrainControl.IsHole(control))
                    continue;
                double h00 = Metres(page.Heights[TerrainPage.Index(lx, lz)]), h10 = Metres(page.Heights[TerrainPage.Index(lx + 1, lz)]);
                double h01 = Metres(page.Heights[TerrainPage.Index(lx, lz + 1)]), h11 = Metres(page.Heights[TerrainPage.Index(lx + 1, lz + 1)]);
                if (Math.Max(Math.Max(h00, h10), Math.Max(h01, h11)) < min.Y || Math.Min(Math.Min(h00, h10), Math.Min(h01, h11)) > max.Y)
                    continue;
                double x0 = Position.X + cx * spacing, z0 = Position.Z + cz * spacing;
                Double3 p00 = new(x0, h00, z0), p10 = new(x0 + spacing, h10, z0), p01 = new(x0, h01, z0 + spacing), p11 = new(x0 + spacing, h11, z0 + spacing);
                int layer = TerrainControl.BaseLayer(control);
                triangles.Add(new TerrainTriangle(p00, p11, p10, layer));
                triangles.Add(new TerrainTriangle(p00, p01, p11, layer));
            }
        }
    }

    /// <summary>Reads and decodes the page without keeping it, for streaming it elsewhere. Any thread.</summary>
    public TerrainPage? ReadPage(TerrainPageKey key) => _pages.TryGetValue(key, out TerrainPage? kept) ? kept : Read(key);

    /// <summary>The keys of every page the package holds.</summary>
    public IEnumerable<TerrainPageKey> PageKeys => _entries.Keys;

    /// <summary>The lowest and highest height in metres under a quarter of the page, 0 to 3 for -x -z, +x -z, -x +z, +x +z.</summary>
    public bool TryGetQuarterBounds(TerrainPageKey key, int quarter, out double min, out double max)
    {
        min = max = 0;
        if (!_entries.TryGetValue(key, out TerrainPageEntry entry))
            return false;
        min = Metres(entry.Bounds[quarter * 2]);
        max = Metres(entry.Bounds[quarter * 2 + 1]);
        return true;
    }

    /// <summary>The lowest and highest height in metres over the finest samples under the page.</summary>
    public bool TryGetPageBounds(TerrainPageKey key, out double min, out double max)
    {
        min = max = 0;
        if (!_entries.TryGetValue(key, out TerrainPageEntry entry))
            return false;
        (ushort low, ushort high) = entry.PageBounds();
        min = Metres(low);
        max = Metres(high);
        return true;
    }

    /// <summary>The ground height at a world x and z, false outside the world or over a hole.</summary>
    public bool TryGetHeight(Double2 xz, out double height, int level = 0)
    {
        height = 0;
        if (!TryCell(xz, level, out Cell cell) || cell.Hole)
            return false;
        height = Metres(TerrainTriangles.Height(cell.H00, cell.H10, cell.H01, cell.H11, cell.Fx, cell.Fz));
        return true;
    }

    /// <summary>The ground's normal at a world x and z: the triangle's own, or with <paramref name="smooth"/> the baked smooth normal.</summary>
    public bool TryGetNormal(Double2 xz, out Float3 normal, bool smooth = false, int level = 0)
    {
        normal = new Float3(0f, 1f, 0f);
        if (!TryCell(xz, level, out Cell cell) || cell.Hole)
            return false;
        if (smooth)
        {
            double nx = Bilinear(cell, 0) / 127.5 - 1.0, nz = Bilinear(cell, 1) / 127.5 - 1.0;
            normal = Float3.Normalize(new Float3((float)nx, (float)Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - nz * nz)), (float)nz));
            return true;
        }
        double scale = Header.Range / 65535.0;
        (double x, double y, double z) = TerrainTriangles.Normal(cell.H00 * scale, cell.H10 * scale, cell.H01 * scale, cell.H11 * scale, cell.Fx, cell.Fz, SpacingAt(level));
        normal = new Float3((float)x, (float)y, (float)z);
        return true;
    }

    /// <summary>The baked ambient occlusion at a world x and z, 1 for open sky.</summary>
    public bool TryGetAmbientOcclusion(Double2 xz, out float occlusion, int level = 0)
    {
        occlusion = 1f;
        if (!TryCell(xz, level, out Cell cell))
            return false;
        occlusion = (float)(Bilinear(cell, 2) / 255.0);
        return true;
    }

    /// <summary>How much of a material layer covers the ground at a world x and z, 0 to 1, blended across the quad. A page without control is all layer 0.</summary>
    public bool TryGetLayerWeight(Double2 xz, int layer, out float weight)
    {
        weight = 0f;
        if (!TryCell(xz, 0, out Cell cell))
            return false;
        uint[]? control = cell.Page.Control;
        if (control == null)
        {
            weight = layer == 0 ? 1f : 0f;
            return true;
        }
        double Weight(int lx, int lz)
        {
            uint c = control[TerrainPage.Index(lx, lz)];
            double overlay = TerrainControl.OverlayBlend(c) / 255.0;
            return (TerrainControl.BaseLayer(c) == layer ? 1.0 - overlay : 0.0) + (TerrainControl.OverlayLayer(c) == layer ? overlay : 0.0);
        }
        double a = Weight(cell.Lx, cell.Lz), b = Weight(cell.Lx + 1, cell.Lz), c = Weight(cell.Lx, cell.Lz + 1), e = Weight(cell.Lx + 1, cell.Lz + 1);
        weight = (float)((a + (b - a) * cell.Fx) * (1 - cell.Fz) + (c + (e - c) * cell.Fx) * cell.Fz);
        return true;
    }

    /// <summary>Whether the finest quad under a world x and z is cut out.</summary>
    public bool IsHole(Double2 xz) => TryCell(xz, 0, out Cell cell) && cell.Hole;

    /// <summary>The first point a ray meets the finest level's triangles within a distance, skipping holes.</summary>
    public bool Raycast(Double3 origin, Float3 direction, double maxDistance, out TerrainHit hit)
    {
        hit = default;
        Double3 d = Double3.Normalize(new Double3(direction.X, direction.Y, direction.Z));
        Double3 o = origin - Position;
        double t0 = 0, t1 = maxDistance;
        if (!Clip(o.X, d.X, 0, SizeX, ref t0, ref t1) || !Clip(o.Z, d.Z, 0, SizeZ, ref t0, ref t1) ||
            !Clip(o.Y, d.Y, Header.Bottom, Header.Bottom + Header.Range, ref t0, ref t1))
            return false;

        double spacing = Header.Spacing;
        double pageSize = TerrainPage.Quads * spacing;
        (int pagesX, int pagesZ) = PagesAt(0);
        foreach ((int px, int pz, double pageEnter, double pageExit) in Walk(o, d, pageSize, pagesX, pagesZ, t0, t1))
        {
            TerrainPageKey key = new(0, px, pz);
            if (!TryGetPageBounds(key, out double min, out double max))
                continue;
            double y0 = o.Y + d.Y * pageEnter, y1 = o.Y + d.Y * pageExit;
            if (Math.Max(y0, y1) < min - Position.Y || Math.Min(y0, y1) > max - Position.Y)
                continue;
            TerrainPage? page = GetPage(key);
            if (page == null)
                continue;

            foreach ((int cx, int cz, double _, double _) in Walk(o, d, spacing, Header.QuadsX, Header.QuadsZ, pageEnter, pageExit))
            {
                int lx = cx - px * TerrainPage.Quads, lz = cz - pz * TerrainPage.Quads;
                if (lx < 0 || lz < 0 || lx >= TerrainPage.Quads || lz >= TerrainPage.Quads)
                    continue;
                if (page.Control != null && TerrainControl.IsHole(page.Control[TerrainPage.Index(lx, lz)]))
                    continue;
                double h00 = Metres(page.Heights[TerrainPage.Index(lx, lz)]) - Position.Y, h10 = Metres(page.Heights[TerrainPage.Index(lx + 1, lz)]) - Position.Y;
                double h01 = Metres(page.Heights[TerrainPage.Index(lx, lz + 1)]) - Position.Y, h11 = Metres(page.Heights[TerrainPage.Index(lx + 1, lz + 1)]) - Position.Y;
                Double3 p00 = new(cx * spacing, h00, cz * spacing), p10 = new((cx + 1) * spacing, h10, cz * spacing);
                Double3 p01 = new(cx * spacing, h01, (cz + 1) * spacing), p11 = new((cx + 1) * spacing, h11, (cz + 1) * spacing);
                double best = double.MaxValue;
                if (Triangle(o, d, p00, p10, p11, out double ta) && ta >= t0 && ta <= t1)
                    best = ta;
                if (Triangle(o, d, p00, p11, p01, out double tb) && tb >= t0 && tb <= t1 && tb < best)
                    best = tb;
                if (best == double.MaxValue)
                    continue;

                Double3 local = o + d * best;
                double fx = Math.Clamp(local.X / spacing - cx, 0, 1), fz = Math.Clamp(local.Z / spacing - cz, 0, 1);
                (double nx, double ny, double nz) = TerrainTriangles.Normal(h00, h10, h01, h11, fx, fz, spacing);
                hit = new TerrainHit(local + Position, new Float3((float)nx, (float)ny, (float)nz), best);
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        lock (_streamLock)
            _stream.Dispose();
    }

    private TerrainPage? Read(TerrainPageKey key)
    {
        if (!_entries.TryGetValue(key, out TerrainPageEntry entry))
            return null;
        return TerrainPackage.DecodePage(key, ReadBytes, entry);
    }

    private byte[] ReadBytes(long offset, int length)
    {
        byte[] bytes = new byte[length];
        lock (_streamLock)
        {
            _stream.Position = offset;
            _stream.ReadExactly(bytes);
        }
        return bytes;
    }

    private double Metres(double sample) => Header.Bottom + Position.Y + sample / 65535.0 * Header.Range;

    private struct Cell
    {
        public TerrainPage Page;
        public int Lx, Lz;
        public double H00, H10, H01, H11, Fx, Fz;
        public bool Hole;
    }

    private bool TryCell(Double2 xz, int level, out Cell cell)
    {
        cell = default;
        if (level < 0 || level >= Levels)
            return false;
        double spacing = SpacingAt(level);
        double u = (xz.X - Position.X) / spacing, v = (xz.Y - Position.Z) / spacing;
        double maxU = SizeX / spacing, maxV = SizeZ / spacing;
        if (!(u >= 0 && v >= 0 && u <= maxU && v <= maxV))
            return false;

        int cx = Math.Clamp((int)Math.Floor(u), 0, Math.Max(0, (int)Math.Ceiling(maxU) - 1));
        int cz = Math.Clamp((int)Math.Floor(v), 0, Math.Max(0, (int)Math.Ceiling(maxV) - 1));
        (int pagesX, int pagesZ) = PagesAt(level);
        int px = Math.Min(cx / TerrainPage.Quads, pagesX - 1), pz = Math.Min(cz / TerrainPage.Quads, pagesZ - 1);
        TerrainPage? page = GetPage(new TerrainPageKey(level, px, pz));
        if (page == null)
            return false;

        int lx = cx - px * TerrainPage.Quads, lz = cz - pz * TerrainPage.Quads;
        cell = new Cell
        {
            Page = page,
            Lx = lx,
            Lz = lz,
            H00 = page.Heights[TerrainPage.Index(lx, lz)],
            H10 = page.Heights[TerrainPage.Index(lx + 1, lz)],
            H01 = page.Heights[TerrainPage.Index(lx, lz + 1)],
            H11 = page.Heights[TerrainPage.Index(lx + 1, lz + 1)],
            Fx = u - cx,
            Fz = v - cz,
            Hole = page.Control != null && TerrainControl.IsHole(page.Control[TerrainPage.Index(lx, lz)]),
        };
        return true;
    }

    private static double Bilinear(in Cell cell, int channel)
    {
        byte[] values = cell.Page.NormalAo;
        double a = values[TerrainPage.Index(cell.Lx, cell.Lz) * 4 + channel], b = values[TerrainPage.Index(cell.Lx + 1, cell.Lz) * 4 + channel];
        double c = values[TerrainPage.Index(cell.Lx, cell.Lz + 1) * 4 + channel], e = values[TerrainPage.Index(cell.Lx + 1, cell.Lz + 1) * 4 + channel];
        return (a + (b - a) * cell.Fx) * (1 - cell.Fz) + (c + (e - c) * cell.Fx) * cell.Fz;
    }

    // Narrows t0 and t1 to where the ray's coordinate lies between low and high
    private static bool Clip(double origin, double direction, double low, double high, ref double t0, ref double t1)
    {
        if (Math.Abs(direction) < 1e-12)
            return origin >= low && origin <= high && t0 <= t1;
        double a = (low - origin) / direction, b = (high - origin) / direction;
        if (a > b)
            (a, b) = (b, a);
        t0 = Math.Max(t0, a);
        t1 = Math.Min(t1, b);
        return t0 <= t1;
    }

    // The grid cells of the given size the ray crosses in x and z between t0 and t1, in order, with the t it enters and leaves each
    private static IEnumerable<(int X, int Z, double Enter, double Exit)> Walk(Double3 o, Double3 d, double size, int countX, int countZ, double t0, double t1)
    {
        double px = o.X + d.X * t0, pz = o.Z + d.Z * t0;
        int x = Math.Clamp((int)Math.Floor(px / size), 0, countX - 1), z = Math.Clamp((int)Math.Floor(pz / size), 0, countZ - 1);
        int stepX = Math.Sign(d.X), stepZ = Math.Sign(d.Z);
        double nextX = stepX == 0 ? double.PositiveInfinity : t0 + ((x + (stepX > 0 ? 1 : 0)) * size - px) / d.X;
        double nextZ = stepZ == 0 ? double.PositiveInfinity : t0 + ((z + (stepZ > 0 ? 1 : 0)) * size - pz) / d.Z;
        double deltaX = stepX == 0 ? double.PositiveInfinity : size / Math.Abs(d.X), deltaZ = stepZ == 0 ? double.PositiveInfinity : size / Math.Abs(d.Z);
        double enter = t0;
        while (true)
        {
            double exit = Math.Min(Math.Min(nextX, nextZ), t1);
            yield return (x, z, enter, exit);
            if (exit >= t1)
                yield break;
            if (nextX < nextZ)
            {
                x += stepX;
                nextX += deltaX;
            }
            else
            {
                z += stepZ;
                nextZ += deltaZ;
            }
            if (x < 0 || z < 0 || x >= countX || z >= countZ)
                yield break;
            enter = exit;
        }
    }

    // Moller and Trumbore, both faces
    private static bool Triangle(Double3 o, Double3 d, Double3 a, Double3 b, Double3 c, out double t)
    {
        t = 0;
        Double3 e1 = b - a, e2 = c - a;
        Double3 p = Double3.Cross(d, e2);
        double det = Double3.Dot(e1, p);
        if (Math.Abs(det) < 1e-14)
            return false;
        double inv = 1.0 / det;
        Double3 s = o - a;
        double u = Double3.Dot(s, p) * inv;
        if (u < -1e-9 || u > 1 + 1e-9)
            return false;
        Double3 q = Double3.Cross(s, e1);
        double v = Double3.Dot(d, q) * inv;
        if (v < -1e-9 || u + v > 1 + 1e-9)
            return false;
        t = Double3.Dot(e2, q) * inv;
        return t >= 0;
    }
}
