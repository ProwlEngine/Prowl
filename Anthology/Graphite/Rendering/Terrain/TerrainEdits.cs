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

    /// <summary>Paints the material Layer over the ground by Strength, 0 to 1, fully inside seven tenths of the radius.</summary>
    Paint,

    /// <summary>Cuts out the quads whose middles lie in the circle, or with a negative Strength fills them back in.</summary>
    Hole,

    /// <summary>Cuts the grass back by Strength, 0 to 1, fully inside seven tenths of the radius, letting it grow back when negative.</summary>
    Grass,

    /// <summary>Cuts scattered detail back as Grass cuts grass.</summary>
    Detail,
}

/// <summary>One edit: a brush over a circle of world x and z, fading to nothing at its edge. Height is Flatten's target, Layer is Paint's.</summary>
public readonly record struct TerrainEdit(TerrainBrush Brush, Double2 Center, double Radius, double Strength, double Height = 0, int Layer = 0);

/// <summary>What an edit changed: the world x and z box whose finest samples it changed, normals and occlusion included, and every page of every level it rewrote.</summary>
public sealed record TerrainEdited(Double2 Min, Double2 Max, IReadOnlyList<TerrainPageKey> Pages);

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
    /// Control brushes change the layers, holes and grass and detail cuts, every level that keeps control taking the
    /// finest sample it stands on, as the importer does. The same edits in the same order give the same bits. Pages are
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
            return ChangeControl(edit, i0, i1, j0, j1, cx, cz);

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

    // A copy of the page to change, with its control copied too when asked, or made where a level keeping control has none
    private TerrainPage Editable(TerrainPageKey key, Dictionary<TerrainPageKey, TerrainPage> pages, bool control = false)
    {
        if (!pages.TryGetValue(key, out TerrainPage? copy))
        {
            TerrainPage page = GetPage(key)!;
            uint[]? controls = page.Control;
            if (control)
                controls = (uint[]?)page.Control?.Clone() ?? (key.Level < Header.ControlLevels ? new uint[TerrainPage.Samples] : null);
            copy = new TerrainPage(key, (ushort[])page.Heights.Clone(), (byte[])page.NormalAo.Clone(), controls);
            pages[key] = copy;
        }
        else if (control && copy.Control == null && key.Level < Header.ControlLevels)
        {
            copy = new TerrainPage(key, copy.Heights, copy.NormalAo, new uint[TerrainPage.Samples]);
            pages[key] = copy;
        }
        return copy;
    }

    /// <summary>How much of the scattered detail is cut at a world x and z, 0 to 1, blended across the quad.</summary>
    public bool TryGetDetailCut(Double2 xz, out float cut)
    {
        cut = 0f;
        if (!TryCell(xz, 0, out Cell cell))
            return false;
        uint[]? control = cell.Page.Control;
        if (control == null)
            return true;
        double Cut(int x, int z) => TerrainControl.DetailCut(control[TerrainPage.Index(x, z)]) / (double)TerrainControl.MaxDetailCut;
        double a = Cut(cell.Lx, cell.Lz), b = Cut(cell.Lx + 1, cell.Lz), c = Cut(cell.Lx, cell.Lz + 1), e = Cut(cell.Lx + 1, cell.Lz + 1);
        cut = (float)((a + (b - a) * cell.Fx) * (1 - cell.Fz) + (c + (e - c) * cell.Fx) * cell.Fz);
        return true;
    }

    private TerrainEdited ChangeControl(in TerrainEdit edit, int i0, int i1, int j0, int j1, double cx, double cz)
    {
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
                uint before = FinestControl(i, j, pages);
                uint after = Control(edit, before, d);
                if (after == before)
                    continue;
                int px = Math.Min(i / TerrainPage.Quads, pagesX - 1), pz = Math.Min(j / TerrainPage.Quads, pagesZ - 1);
                Editable(new TerrainPageKey(0, px, pz), pages, control: true).Control![TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads)] = after;
                changed = true;
            }
        if (!changed)
            return new TerrainEdited(edit.Center, edit.Center, []);

        for (int level = 0; level < Math.Min(Header.ControlLevels, Levels); level++)
            RebuildControl(level, i0, i1, j0, j1, pages);

        List<TerrainPageKey> keys = new(pages.Keys);
        keys.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        foreach (TerrainPageKey key in keys)
        {
            _edited[key] = 0;
            _pages[key] = pages[key];
        }
        Double2 min = new(Position.X + i0 * spacing, Position.Z + j0 * spacing);
        Double2 max = new(Position.X + (i1 + 1) * spacing, Position.Z + (j1 + 1) * spacing);
        return new TerrainEdited(min, max, keys);
    }

    // Full inside seven tenths of the radius, easing to nothing at its edge
    private static double Soft(double d)
    {
        if (d >= 1)
            return 0;
        double t = Math.Clamp((1 - d) / 0.3, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static uint Control(in TerrainEdit edit, uint control, double d)
    {
        double amount = Soft(d) * edit.Strength;
        return edit.Brush switch
        {
            TerrainBrush.Hole => d < 1 && edit.Strength != 0 ? TerrainControl.WithHole(control, edit.Strength > 0) : control,
            TerrainBrush.Grass => TerrainControl.WithGrassCut(control, (int)Math.Round(TerrainControl.GrassCut(control) + amount * TerrainControl.MaxGrassCut)),
            TerrainBrush.Detail => TerrainControl.WithDetailCut(control, (int)Math.Round(TerrainControl.DetailCut(control) + amount * TerrainControl.MaxDetailCut)),
            _ => Paint(control, edit.Layer, Math.Clamp(amount, 0, 1)),
        };
    }

    // Mixes the layer into the sample's two by the amount and keeps the strongest two, the base first, ties to the earlier
    private static uint Paint(uint control, int layer, double amount)
    {
        if (amount <= 0 || layer < 0 || layer > 63)
            return control;
        double blend = TerrainControl.OverlayBlend(control) / 255.0;
        Span<int> layers = stackalloc int[3];
        Span<double> weights = stackalloc double[3];
        int count = 0;
        void Add(Span<int> ls, Span<double> ws, int l, double w)
        {
            for (int k = 0; k < count; k++)
                if (ls[k] == l)
                {
                    ws[k] += w;
                    return;
                }
            ls[count] = l;
            ws[count++] = w;
        }
        Add(layers, weights, TerrainControl.BaseLayer(control), (1 - blend) * (1 - amount));
        Add(layers, weights, TerrainControl.OverlayLayer(control), blend * (1 - amount));
        Add(layers, weights, layer, amount);

        int a = 0;
        for (int k = 1; k < count; k++)
            if (weights[k] > weights[a])
                a = k;
        int b = -1;
        for (int k = 0; k < count; k++)
            if (k != a && (b < 0 || weights[k] > weights[b]))
                b = k;
        if (b < 0 || weights[b] <= 0)
            return TerrainControl.Layers(layers[a], layers[a], 0, control);
        return TerrainControl.Layers(layers[a], layers[b], (int)Math.Round(weights[b] / (weights[a] + weights[b]) * 255), control);
    }

    // A finest control sample, held at the world's edge, 0 where its page keeps none
    private uint FinestControl(int i, int j, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        i = Math.Clamp(i, 0, Header.QuadsX);
        j = Math.Clamp(j, 0, Header.QuadsZ);
        (int pagesX, int pagesZ) = PagesAt(0);
        int px = Math.Min(i / TerrainPage.Quads, pagesX - 1), pz = Math.Min(j / TerrainPage.Quads, pagesZ - 1);
        TerrainPageKey key = new(0, px, pz);
        TerrainPage page = pages.TryGetValue(key, out TerrainPage? copy) ? copy : GetPage(key)!;
        return page.Control?[TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads)] ?? 0u;
    }

    // A control level's samples over the edited finest box, aprons included, each the finest sample it stands on
    private void RebuildControl(int level, int i0, int i1, int j0, int j1, Dictionary<TerrainPageKey, TerrainPage> pages)
    {
        (int pagesX, int pagesZ) = PagesAt(level);
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
                uint[] control = Editable(key, pages, control: true).Control!;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        control[TerrainPage.Index(x, z)] = FinestControl((ox + x) << level, (oz + z) << level, pages);
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
