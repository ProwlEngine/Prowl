// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Drives the chart atlas layout: every chart is sorted by extent and fed into a bin packer at a
/// shared scale, and the largest scale that still fits is found by bisection.
/// Successful placements are baked back into each chart's UVs.
/// </summary>
internal static class AtlasPacker
{
    /// <summary>Bisection stops once the bracket is this close in relative terms.</summary>
    private const double ScaleTolerance = 0.005;

    public static void Pack(IList<UvChart> charts, double border)
    {
        if (charts.Count == 0) return;

        var slots = new AtlasSlot[charts.Count];
        for (int i = 0; i < charts.Count; ++i)
            slots[i] = AtlasSlot.Capture(charts[i], i);

        double totalArea = 0.0;
        for (int i = 0; i < slots.Length; ++i)
            totalArea += slots[i].Extent.X * slots[i].Extent.Y;
        if (!(totalArea > 0.0)) totalArea = 1.0; // every chart collapsed to a point

        // Build the placement order: deterministic centroid sort first, then stable sort by extent
        // descending so the biggest charts go in first.
        var ordering = new int[slots.Length];
        for (int i = 0; i < ordering.Length; ++i) ordering[i] = i;

        System.Array.Sort(ordering, (a, b) => CompareByOrigin3D(slots[a], slots[b]));
        StableSortByExtentDesc(ordering, slots);

        var ordered = new AtlasSlot[slots.Length];
        for (int i = 0; i < ordering.Length; ++i) ordered[i] = slots[ordering[i]];

        var rects = new BinRect[slots.Length];
        var bestRects = new BinRect[slots.Length];
        var tree = new BinPackTree(slots.Length);

        // The area-perfect scale is an upper bound no layout can beat, so halve from there until
        // something fits and bisect the resulting bracket. Beats walking a fixed ladder downwards.
        double high = 1.0 / System.Math.Sqrt(totalArea);
        double low = high;
        while (!TryPackAt(ordered, rects, tree, low, border))
        {
            high = low;
            low *= 0.5;
            if (low < 1e-12) throw new UnwrapException("Chart atlas could not be packed at any scale.");
        }

        double bestScale = low;
        System.Array.Copy(rects, bestRects, rects.Length);

        while ((high - low) / low > ScaleTolerance)
        {
            double mid = 0.5 * (low + high);
            if (TryPackAt(ordered, rects, tree, mid, border))
            {
                low = mid;
                bestScale = mid;
                System.Array.Copy(rects, bestRects, rects.Length);
            }
            else
            {
                high = mid;
            }
        }

        for (int i = 0; i < ordered.Length; ++i)
        {
            ordered[i].Rescale(bestScale);
            ordered[i].Origin = bestRects[i].Origin + 0.5 * new Double2(border, border);
        }

        for (int i = 0; i < ordered.Length; ++i)
        {
            var slot = ordered[i];
            UvChart chart = charts[slot.ChartIndex];
            for (int v = 0; v < chart.UVs.Length; ++v)
                chart.UVs[v] = slot.Origin + slot.Scale * (chart.UVs[v] - slot.SourceOrigin);
        }
    }

    /// <summary>Lay every chart out at one shared scale; false if any of them ran out of room.</summary>
    private static bool TryPackAt(AtlasSlot[] ordered, BinRect[] rects, BinPackTree tree, double scale, double border)
    {
        for (int i = 0; i < ordered.Length; ++i)
        {
            ordered[i].Rescale(scale);
            rects[i] = new BinRect
            {
                Origin = default,
                Extent = ordered[i].Extent + new Double2(border, border),
            };
        }

        tree.StartPack(0.5 * new Double2(border, border));

        for (int i = 0; i < ordered.Length; ++i)
            if (!tree.TryInsert(ref rects[i], border)) return false;

        return true;
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
