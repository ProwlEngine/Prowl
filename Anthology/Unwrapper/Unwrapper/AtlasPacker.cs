using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Drives the chart atlas layout: each chart starts at a heuristic scale, gets sorted by extent,
/// and is fed into a bin packer. On failure the scale is reduced and the attempt is retried.
/// Successful placements are baked back into each chart's UVs.
/// </summary>
internal static class AtlasPacker
{
    /// <summary>Shrink factor between failed pack attempts.</summary>
    private const double ShrinkFactor = 0.99;

    public static void Pack(IList<UvChart> charts, double border)
    {
        if (charts.Count == 0) return;

        var slots = new AtlasSlot[charts.Count];
        for (int i = 0; i < charts.Count; ++i)
            slots[i] = AtlasSlot.Capture(charts[i], i);

        double totalArea = 0.0;
        for (int i = 0; i < slots.Length; ++i)
            totalArea += slots[i].Extent.X * slots[i].Extent.Y;

        // Build the placement order: deterministic centroid sort first, then stable sort by extent
        // descending so the biggest charts go in first.
        var ordering = new int[slots.Length];
        for (int i = 0; i < ordering.Length; ++i) ordering[i] = i;

        System.Array.Sort(ordering, (a, b) => CompareByOrigin3D(slots[a], slots[b]));
        StableSortByExtentDesc(ordering, slots);

        var ordered = new AtlasSlot[slots.Length];
        for (int i = 0; i < ordering.Length; ++i) ordered[i] = slots[ordering[i]];

        var rects = new BinRect[slots.Length];
        var tree = new BinPackTree(slots.Length);

        bool packed = false;
        double curScale = 1.0 / System.Math.Sqrt(totalArea);

        while (!packed)
        {
            for (int i = 0; i < ordered.Length; ++i)
            {
                ordered[i].Rescale(curScale);
                rects[i] = new BinRect
                {
                    Origin = default,
                    Extent = ordered[i].Extent + new Double2(border, border),
                };
            }

            tree.StartPack(0.5 * new Double2(border, border));

            packed = true;
            for (int i = 0; i < ordered.Length; ++i)
                packed &= tree.TryInsert(ref rects[i], border);

            if (!packed) curScale *= ShrinkFactor;
        }

        for (int i = 0; i < ordered.Length; ++i)
            ordered[i].Origin = rects[i].Origin + 0.5 * new Double2(border, border);

        for (int i = 0; i < ordered.Length; ++i)
        {
            var slot = ordered[i];
            UvChart chart = charts[slot.ChartIndex];
            for (int v = 0; v < chart.UVs.Length; ++v)
                chart.UVs[v] = slot.Origin + slot.Scale * (chart.UVs[v] - slot.SourceOrigin);
        }
    }

    /// <summary>
    /// Lexicographic by 3D centroid, descending — only matters as a tiebreaker when two charts have
    /// identical extents and need a deterministic ordering.
    /// </summary>
    private static int CompareByOrigin3D(AtlasSlot a, AtlasSlot b)
    {
        const double eps = 1e-6;
        if (System.Math.Abs(a.Origin3D.X - b.Origin3D.X) > eps) return a.Origin3D.X > b.Origin3D.X ? -1 : 1;
        if (System.Math.Abs(a.Origin3D.Y - b.Origin3D.Y) > eps) return a.Origin3D.Y > b.Origin3D.Y ? -1 : 1;
        if (System.Math.Abs(a.Origin3D.Z - b.Origin3D.Z) > eps) return a.Origin3D.Z > b.Origin3D.Z ? -1 : 1;
        return 0;
    }

    /// <summary>Stable sort by (Extent.X, Extent.Y) descending; uses original position as the tiebreaker.</summary>
    private static void StableSortByExtentDesc(int[] indices, AtlasSlot[] slots)
    {
        var pairs = new (int Index, int Original)[indices.Length];
        for (int i = 0; i < indices.Length; ++i) pairs[i] = (indices[i], i);

        System.Array.Sort(pairs, (a, b) =>
        {
            const double eps = 1e-6;
            var ea = slots[a.Index].Extent;
            var eb = slots[b.Index].Extent;
            if (System.Math.Abs(ea.X - eb.X) > eps) return ea.X > eb.X ? -1 : 1;
            if (System.Math.Abs(ea.Y - eb.Y) > eps) return ea.Y > eb.Y ? -1 : 1;
            return a.Original.CompareTo(b.Original);
        });

        for (int i = 0; i < indices.Length; ++i) indices[i] = pairs[i].Index;
    }
}
