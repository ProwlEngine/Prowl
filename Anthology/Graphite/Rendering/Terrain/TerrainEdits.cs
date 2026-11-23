using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public enum TerrainBrush : byte
{
    /// <summary>Lifts the ground by Strength metres at the middle, lowering it when negative.</summary>
    Raise,

    /// <summary>Pulls the ground toward Height by Strength, 0 to 1.</summary>
    Flatten,

    /// <summary>Pulls each sample toward the average of its neighbours by Strength, 0 to 1.</summary>
    Smooth,

    /// <summary>A bowl Strength metres deep with a raised rim out to half the radius again.</summary>
    Crater,

    /// <summary>Paints the material Layer over the ground by Strength, 0 to 1, fully inside seven tenths of the radius, the other layers giving way.</summary>
    Paint,

    /// <summary>Cuts out the quads whose middles lie in the circle, or with a negative Strength fills them back in.</summary>
    Hole,

    /// <summary>Adds Strength, -1 to 1, to the density of the detail prototype Layer, fully inside seven tenths of the radius.</summary>
    Detail,
}

/// <summary>One edit: a brush over a circle of world x and z, fading to nothing at its edge. Height is Flatten's target, Layer is Paint's layer or Detail's prototype.</summary>
public readonly record struct TerrainEdit(TerrainBrush Brush, Double2 Center, double Radius, double Strength, double Height = 0, int Layer = 0);

/// <summary>
/// What an edit changed: the world x and z box whose finest samples it changed, normals and occlusion included, every
/// page of every level it rewrote, and whether heights changed or only the surface's weights, holes and densities.
/// </summary>
public sealed record TerrainEdited(Double2 Min, Double2 Max, IReadOnlyList<TerrainPageKey> Pages, bool Heights = true);

public sealed partial class TerrainData
{
    private readonly ConcurrentDictionary<TerrainPageKey, byte> _edited = new();
    private readonly object _editLock = new();

    /// <summary>Raised on the editing thread after each edit, for renderers, placement and physics to refresh.</summary>
    public event Action<TerrainEdited>? Edited;

    /// <summary>Whether an edit has changed the page, so it stays loaded and is no longer what the package holds.</summary>
    public bool IsEdited(TerrainPageKey key) => _edited.ContainsKey(key);

    /// <summary>
    /// Changes the finest samples under a brush, then every level over them. Height brushes rebuild the heights, the
    /// normals, occlusion and cavity they reach and the quarter bounds, exactly as importing the edited heights would.
    /// Paint, Hole and Detail brushes change the splat weights, holes and detail densities, every level that keeps splat
    /// weights taking the finest sample it stands on, as the importer does. The same edits in the same order give the same bits. Pages are
    /// replaced rather than changed, so queries on other threads see the old page or the new one whole.
    /// </summary>
    public TerrainEdited Apply(in TerrainEdit edit)
    {
        TerrainEdited result;
        lock (_editLock)
            result = Change(edit);
        if (result.Pages.Count > 0)
            Edited?.Invoke(result);
        return result;
    }

    private TerrainEdited Change(in TerrainEdit edit)
    {
        double spacing = Header.Spacing, unit = Header.Range / 65535.0, bottom = Header.Bottom + Position.Y;
        int lastX = Header.QuadsX, lastZ = Header.QuadsZ;
        double reach = edit.Radius * (edit.Brush == TerrainBrush.Crater ? 1.5 : 1.0);
        double cx = edit.Center.X - Position.X, cz = edit.Center.Y - Position.Z;
        int i0 = Math.Max((int)Math.Floor((cx - reach) / spacing), 0), i1 = Math.Min((int)Math.Ceiling((cx + reach) / spacing), lastX);
        int j0 = Math.Max((int)Math.Floor((cz - reach) / spacing), 0), j1 = Math.Min((int)Math.Ceiling((cz + reach) / spacing), lastZ);
        if (!(edit.Radius > 0) || i0 > i1 || j0 > j1)
            return new TerrainEdited(edit.Center, edit.Center, []);
        if (edit.Brush >= TerrainBrush.Paint)
            return ChangeSurface(edit, i0, i1, j0, j1, cx, cz);

        Dictionary<TerrainPageKey, TerrainPage> pages = new();
        int width = i1 - i0 + 3, depth = j1 - j0 + 3;
        ushort[] old = new ushort[width * depth];
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
                old[z * width + x] = Finest(i0 - 1 + x, j0 - 1 + z, pages);

        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int at = (j - j0 + 1) * width + i - i0 + 1;
                double metres = bottom + old[at] * unit;
                double dx = i * spacing - cx, dz = j * spacing - cz;
                double d = Math.Sqrt(dx * dx + dz * dz) / edit.Radius;
                double around = 0;
                if (edit.Brush == TerrainBrush.Smooth)
                {
                    for (int k = -1; k <= 1; k++)
                        for (int l = -1; l <= 1; l++)
                            around += old[at + k * width + l];
                    around = bottom + around / 9 * unit;
                }
                double after = Brush(edit, metres, d, around);
                ushort value = (ushort)Math.Clamp(Math.Round((after - bottom) / unit), 0, 65535);
                if (value != old[at])
                    SetFinest(i, j, value, pages);
            }

        for (int level = 0; level < Levels; level++)
            Rebuild(level, i0, i1, j0, j1, pages);

        List<TerrainPageKey> keys = new(pages.Keys);
        keys.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        foreach (TerrainPageKey key in keys)
            Bound(key, pages);
        foreach (TerrainPageKey key in keys)
        {
            _edited[key] = 0;
            _pages[key] = pages[key];
        }

        int margin = TerrainBake.Reach + 1;
        Double2 min = new(Position.X + Math.Max(i0 - margin, 0) * spacing, Position.Z + Math.Max(j0 - margin, 0) * spacing);
        Double2 max = new(Position.X + Math.Min(i1 + margin, lastX) * spacing, Position.Z + Math.Min(j1 + margin, lastZ) * spacing);
        return new TerrainEdited(min, max, keys);
    }

    private static double Brush(in TerrainEdit edit, double metres, double d, double around)
    {
        double fade = d < 1 ? (1 - d * d) * (1 - d * d) : 0;
        switch (edit.Brush)
        {
            case TerrainBrush.Raise:
                return metres + edit.Strength * fade;
            case TerrainBrush.Flatten:
                return metres + (edit.Height - metres) * Math.Clamp(edit.Strength, 0, 1) * fade;
            case TerrainBrush.Smooth:
                return metres + (around - metres) * Math.Clamp(edit.Strength, 0, 1) * fade;
            default:
                double bowl = d < 1 ? 1 - d * d : 0;
                double u = (d - 0.75) / 0.75;
                double rim = u > 0 && u < 1 ? u * (1 - u) : 0;
                return metres - edit.Strength * bowl + edit.Strength * rim;
        }
    }

    // A finest sample, held at the world's edge, from the page that owns it, an edit's copy where it has one
    private ushort Finest(int i, int j, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        i = Math.Clamp(i, 0, Header.QuadsX);
        j = Math.Clamp(j, 0, Header.QuadsZ);
        (int pagesX, int pagesZ) = PagesAt(0);
        int px = Math.Min(i / TerrainPage.Quads, pagesX - 1), pz = Math.Min(j / TerrainPage.Quads, pagesZ - 1);
        TerrainPageKey key = new(0, px, pz);
        TerrainPage page = pages.TryGetValue(key, out TerrainPage? copy) ? copy : GetPage(key)!;
        return page.Heights[TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads)];
    }

    private void SetFinest(int i, int j, ushort value, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        (int pagesX, int pagesZ) = PagesAt(0);
        int px = Math.Min(i / TerrainPage.Quads, pagesX - 1), pz = Math.Min(j / TerrainPage.Quads, pagesZ - 1);
        Editable(new TerrainPageKey(0, px, pz), pages).Heights[TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads)] = value;
    }

    // A copy of the page to change, given the layers a surface brush writes where its level keeps them and it has none
    private TerrainPage Editable(TerrainPageKey key, Dictionary<TerrainPageKey, TerrainPage> pages, bool surface = false)
    {
        if (!pages.TryGetValue(key, out TerrainPage? copy))
        {
            copy = GetPage(key)!.Clone();
            pages[key] = copy;
        }
        if (!surface)
            return copy;
        bool splat = key.Level < Header.SplatLevels;
        bool missing = splat && Header.SplatMaps > 0 && copy.Splat == null || splat && copy.Holes == null || key.Level == 0 && Header.Details > 0 && copy.Details == null;
        if (missing)
        {
            byte[]? weights = copy.Splat;
            if (splat && Header.SplatMaps > 0 && weights == null)
            {
                weights = new byte[TerrainPage.Samples * 4 * Header.SplatMaps];
                for (int i = 0; i < TerrainPage.Samples; i++)
                    weights[i * 4] = 255;
            }
            byte[]? holes = copy.Holes ?? (splat ? new byte[TerrainPage.Samples] : null);
            byte[]? details = copy.Details ?? (key.Level == 0 && Header.Details > 0 ? new byte[TerrainPage.Samples * Header.Details] : null);
            copy = new TerrainPage(key, copy.Heights, copy.NormalAo, weights, holes, details);
            pages[key] = copy;
        }
        return copy;
    }

    private TerrainEdited ChangeSurface(in TerrainEdit edit, int i0, int i1, int j0, int j1, double cx, double cz)
    {
        if (edit.Brush == TerrainBrush.Paint && (Header.SplatMaps == 0 || (uint)edit.Layer >= (uint)Header.Layers) ||
            edit.Brush == TerrainBrush.Detail && (uint)edit.Layer >= (uint)Header.Details)
            return new TerrainEdited(edit.Center, edit.Center, []);
        double spacing = Header.Spacing;
        double middle = edit.Brush == TerrainBrush.Hole ? 0.5 : 0.0;
        (int pagesX, int pagesZ) = PagesAt(0);
        Dictionary<TerrainPageKey, TerrainPage> pages = new();
        bool changed = false;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double dx = (i + middle) * spacing - cx, dz = (j + middle) * spacing - cz;
                double d = Math.Sqrt(dx * dx + dz * dz) / edit.Radius;
                if (d >= 1)
                    continue;
                int px = Math.Min(i / TerrainPage.Quads, pagesX - 1), pz = Math.Min(j / TerrainPage.Quads, pagesZ - 1);
                TerrainPage page = Editable(new TerrainPageKey(0, px, pz), pages, surface: true);
                int index = TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads);
                if (Paint(page, index, edit, Soft(d) * edit.Strength))
                {
                    changed = true;
                    ShareSample(page, index, i, j, pages);
                }
            }
        if (!changed)
            return new TerrainEdited(edit.Center, edit.Center, []);

        if (edit.Brush != TerrainBrush.Detail)
            for (int level = 1; level < Math.Min(Header.SplatLevels, Levels); level++)
                RebuildSurface(level, i0, i1, j0, j1, pages);

        List<TerrainPageKey> keys = new(pages.Keys);
        keys.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        foreach (TerrainPageKey key in keys)
        {
            _edited[key] = 0;
            _pages[key] = pages[key];
        }
        Double2 min = new(Position.X + i0 * spacing, Position.Z + j0 * spacing);
        Double2 max = new(Position.X + (i1 + 1) * spacing, Position.Z + (j1 + 1) * spacing);
        return new TerrainEdited(min, max, keys, Heights: false);
    }

    // Copies a finest sample's surface layers into the edges and aprons of the other finest pages that hold it
    private void ShareSample(TerrainPage owner, int index, int i, int j, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        (int pagesX, int pagesZ) = PagesAt(0);
        int px0 = Math.Max(FloorDiv(i - TerrainPage.Apron - 1, TerrainPage.Quads), 0), px1 = Math.Min(FloorDiv(i + TerrainPage.Apron, TerrainPage.Quads), pagesX - 1);
        int pz0 = Math.Max(FloorDiv(j - TerrainPage.Apron - 1, TerrainPage.Quads), 0), pz1 = Math.Min(FloorDiv(j + TerrainPage.Apron, TerrainPage.Quads), pagesZ - 1);
        for (int pz = pz0; pz <= pz1; pz++)
            for (int px = px0; px <= px1; px++)
            {
                TerrainPageKey key = new(0, px, pz);
                if (key == owner.Key || !_entries.ContainsKey(key))
                    continue;
                TerrainPage page = Editable(key, pages, surface: true);
                int to = TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads);
                if (owner.Holes != null)
                    page.Holes![to] = owner.Holes[index];
                if (owner.Splat != null)
                    for (int map = 0; map < owner.SplatMaps; map++)
                        for (int c = 0; c < 4; c++)
                            page.Splat![(map * TerrainPage.Samples + to) * 4 + c] = owner.Splat[(map * TerrainPage.Samples + index) * 4 + c];
                if (owner.Details != null)
                    for (int d = 0; d < owner.DetailCount; d++)
                        page.Details![d * TerrainPage.Samples + to] = owner.Details[d * TerrainPage.Samples + index];
            }
    }

    // Full inside seven tenths of the radius, easing to nothing at its edge
    private static double Soft(double d)
    {
        if (d >= 1)
            return 0;
        double t = Math.Clamp((1 - d) / 0.3, 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Applies a surface brush to one finest sample, true when it changed. Paint mixes the layer in by the amount, every
    // other layer giving way in proportion, so the weights still add to 1.
    private bool Paint(TerrainPage page, int index, in TerrainEdit edit, double amount)
    {
        switch (edit.Brush)
        {
            case TerrainBrush.Hole:
                byte hole = edit.Strength > 0 ? (byte)1 : (byte)0;
                if (edit.Strength == 0 || page.Holes![index] == hole)
                    return false;
                page.Holes[index] = hole;
                return true;
            case TerrainBrush.Detail:
            {
                int at = edit.Layer * TerrainPage.Samples + index;
                byte after = (byte)Math.Clamp((int)Math.Round(page.Details![at] + amount * 255), 0, 255);
                bool differs = after != page.Details[at];
                page.Details[at] = after;
                return differs;
            }
            default:
            {
                double a = Math.Clamp(amount, 0, 1);
                if (a <= 0)
                    return false;
                byte[] splat = page.Splat!;
                bool differs = false;
                for (int layer = 0; layer < Header.Layers; layer++)
                {
                    int at = ((layer >> 2) * TerrainPage.Samples + index) * 4 + (layer & 3);
                    double weight = splat[at] / 255.0 * (1 - a) + (layer == edit.Layer ? a : 0);
                    byte value = (byte)Math.Round(weight * 255);
                    differs |= value != splat[at];
                    splat[at] = value;
                }
                return differs;
            }
        }
    }

    // A coarser level's splat weights and holes over the edited finest box, aprons included, each the finest sample it stands on
    private void RebuildSurface(int level, int i0, int i1, int j0, int j1, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        (int pagesX, int pagesZ) = PagesAt(level);
        (int finestX, int finestZ) = PagesAt(0);
        int edgeX = pagesX * TerrainPage.Quads + TerrainPage.Apron, edgeZ = pagesZ * TerrainPage.Quads + TerrainPage.Apron;
        int li0 = Math.Max((i0 >> level) - 1, -TerrainPage.Apron), lj0 = Math.Max((j0 >> level) - 1, -TerrainPage.Apron);
        int li1 = Math.Min(i1 == Header.QuadsX ? edgeX : ((i1 + (1 << level) - 1) >> level) + 1, edgeX);
        int lj1 = Math.Min(j1 == Header.QuadsZ ? edgeZ : ((j1 + (1 << level) - 1) >> level) + 1, edgeZ);
        int px0 = Math.Max(FloorDiv(li0 - TerrainPage.Quads - TerrainPage.Apron, TerrainPage.Quads) + 1, 0), px1 = Math.Min(FloorDiv(li1 + TerrainPage.Apron, TerrainPage.Quads), pagesX - 1);
        int pz0 = Math.Max(FloorDiv(lj0 - TerrainPage.Quads - TerrainPage.Apron, TerrainPage.Quads) + 1, 0), pz1 = Math.Min(FloorDiv(lj1 + TerrainPage.Apron, TerrainPage.Quads), pagesZ - 1);
        for (int pz = pz0; pz <= pz1; pz++)
            for (int px = px0; px <= px1; px++)
            {
                TerrainPageKey key = new(level, px, pz);
                if (!_entries.ContainsKey(key))
                    continue;
                int ox = px * TerrainPage.Quads, oz = pz * TerrainPage.Quads;
                int x0 = Math.Max(li0 - ox, -TerrainPage.Apron), x1 = Math.Min(li1 - ox, TerrainPage.Quads + TerrainPage.Apron);
                int z0 = Math.Max(lj0 - oz, -TerrainPage.Apron), z1 = Math.Min(lj1 - oz, TerrainPage.Quads + TerrainPage.Apron);
                if (x0 > x1 || z0 > z1)
                    continue;
                TerrainPage page = Editable(key, pages, surface: true);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = Math.Clamp((ox + x) << level, 0, Header.QuadsX), j = Math.Clamp((oz + z) << level, 0, Header.QuadsZ);
                        int fx = Math.Min(i / TerrainPage.Quads, finestX - 1), fz = Math.Min(j / TerrainPage.Quads, finestZ - 1);
                        TerrainPageKey finestKey = new(0, fx, fz);
                        TerrainPage finest = pages.TryGetValue(finestKey, out TerrainPage? edited) ? edited : GetPage(finestKey)!;
                        int from = TerrainPage.Index(i - fx * TerrainPage.Quads, j - fz * TerrainPage.Quads), to = TerrainPage.Index(x, z);
                        page.Holes![to] = finest.Holes?[from] ?? 0;
                        for (int map = 0; map < page.SplatMaps; map++)
                            for (int c = 0; c < 4; c++)
                                page.Splat![(map * TerrainPage.Samples + to) * 4 + c] = finest.Splat?[(map * TerrainPage.Samples + from) * 4 + c] ?? (byte)(map == 0 && c == 0 ? 255 : 0);
                    }
            }
    }

    // A level's samples over the edited finest box: heights copied from the finest samples they stand on, then every
    // sample whose normal, occlusion or cavity reads one of them baked again, aprons included
    private void Rebuild(int level, int i0, int i1, int j0, int j1, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        (int pagesX, int pagesZ) = PagesAt(level);
        int edgeX = pagesX * TerrainPage.Quads + TerrainPage.Apron, edgeZ = pagesZ * TerrainPage.Quads + TerrainPage.Apron;
        int margin = TerrainBake.Reach + 1;
        int li0 = (i0 >> level) - margin, lj0 = (j0 >> level) - margin;
        int li1 = i1 == Header.QuadsX ? edgeX : ((i1 + (1 << level) - 1) >> level) + margin;
        int lj1 = j1 == Header.QuadsZ ? edgeZ : ((j1 + (1 << level) - 1) >> level) + margin;
        li0 = Math.Max(li0, -TerrainPage.Apron);
        lj0 = Math.Max(lj0, -TerrainPage.Apron);
        li1 = Math.Min(li1, edgeX);
        lj1 = Math.Min(lj1, edgeZ);

        Grid grid = new(li0 - margin, lj0 - margin, li1 - li0 + 1 + 2 * margin, lj1 - lj0 + 1 + 2 * margin);
        double unit = Header.Range / 65535.0;
        for (int z = 0; z < grid.Depth; z++)
            for (int x = 0; x < grid.Width; x++)
                grid.Samples[z * grid.Width + x] = Finest((grid.X + x) << level, (grid.Z + z) << level, pages);
        grid.Unit = unit;

        double spacing = SpacingAt(level);
        int px0 = Math.Max(FloorDiv(li0 - TerrainPage.Quads - TerrainPage.Apron, TerrainPage.Quads) + 1, 0), px1 = Math.Min(FloorDiv(li1 + TerrainPage.Apron, TerrainPage.Quads), pagesX - 1);
        int pz0 = Math.Max(FloorDiv(lj0 - TerrainPage.Quads - TerrainPage.Apron, TerrainPage.Quads) + 1, 0), pz1 = Math.Min(FloorDiv(lj1 + TerrainPage.Apron, TerrainPage.Quads), pagesZ - 1);
        for (int pz = pz0; pz <= pz1; pz++)
            for (int px = px0; px <= px1; px++)
            {
                TerrainPageKey key = new(level, px, pz);
                if (!_entries.ContainsKey(key))
                    continue;
                int ox = px * TerrainPage.Quads, oz = pz * TerrainPage.Quads;
                int x0 = Math.Max(li0 - ox, -TerrainPage.Apron), x1 = Math.Min(li1 - ox, TerrainPage.Quads + TerrainPage.Apron);
                int z0 = Math.Max(lj0 - oz, -TerrainPage.Apron), z1 = Math.Min(lj1 - oz, TerrainPage.Quads + TerrainPage.Apron);
                if (x0 > x1 || z0 > z1)
                    continue;
                TerrainPage page = Editable(key, pages);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int index = TerrainPage.Index(x, z);
                        page.Heights[index] = grid.Samples[(oz + z - grid.Z) * grid.Width + ox + x - grid.X];
                        TerrainBake.Sample(grid, ox + x, oz + z, spacing, page.NormalAo.AsSpan(index * 4, 4));
                    }
            }
    }

    // Rewrites a page's quarter bounds, the finest from its samples and coarser ones from the pages below, done finest first
    private void Bound(TerrainPageKey key, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        TerrainPageEntry entry = _entries[key];
        ushort[] bounds = new ushort[8];
        if (key.Level == 0)
            TerrainBake.FinestBounds(pages[key], bounds);
        else
            TerrainBake.CoarseBounds(key, Header, (x, z) => _entries.TryGetValue(new TerrainPageKey(key.Level - 1, x, z), out TerrainPageEntry below) ? below.Bounds : null, bounds);
        Array.Copy(bounds, entry.Bounds, 8);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    // A box of a level's samples in metres above the bottom, which a bake reads in place of the pages
    private struct Grid(int x, int z, int width, int depth) : ITerrainSamples
    {
        public readonly int X = x, Z = z, Width = width, Depth = depth;
        public readonly ushort[] Samples = new ushort[width * depth];
        public double Unit;

        public readonly double Metres(int i, int j) => Samples[(j - Z) * Width + i - X] * Unit;
    }
}
