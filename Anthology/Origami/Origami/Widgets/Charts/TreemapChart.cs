// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

/// <summary>
/// Squarified treemap. Every level of the tree fills the rectangle its parent left for it, split into
/// tiles whose areas are their share of that level's total and whose shapes are kept as close to square
/// as the standard worst-aspect-ratio row algorithm can manage. A parent is drawn before its children so
/// the children paint over it, leaving the parent visible only as the gap and header strip around them.
/// </summary>
public sealed class TreemapChart<T> : HierarchicalCore<TreemapChart<T>, T>
{
    internal TreemapChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private const float HeaderHeight = 14f;
    private const float MinCellSide = 1f;

    protected override void BuildMarks(Paper paper, in HierarchicalContext ctx)
    {
        float x0 = ctx.MapX(0d), x1 = ctx.MapX(1d);
        float y0 = ctx.MapY(0d), y1 = ctx.MapY(1d);

        Layout(paper, in ctx, ctx.Roots, x0, y0, x1 - x0, y1 - y0);
    }

    // --- Layout ---

    /// <summary>Fills one rectangle with one level of the tree. Hidden and weightless siblings drop out
    /// first, so the level's total is only what is actually drawn and the tiles always fill the rect.</summary>
    private void Layout(Paper paper, in HierarchicalContext ctx, IReadOnlyList<HierarchicalNode<T>> siblings,
        float x, float y, float w, float h)
    {
        if (siblings.Count == 0 || w < MinCellSide || h < MinCellSide) return;

        var tiles = new List<HierarchicalNode<T>>(siblings.Count);
        double total = 0d;

        foreach (HierarchicalNode<T> node in siblings)
        {
            if (!node.Visible) continue;
            tiles.Add(node);
            total += node.Value;
        }

        if (tiles.Count == 0 || total <= 0d) return;

        Squarify(paper, in ctx, tiles, total, x, y, w, h);
    }

    /// <summary>The squarified treemap proper: take tiles off the front of the level into a row laid along
    /// the shorter side of the remaining rect for as long as adding one more improves the row's worst
    /// aspect ratio, place that row, then repeat on what is left of the rect.</summary>
    private void Squarify(Paper paper, in HierarchicalContext ctx, List<HierarchicalNode<T>> tiles,
        double total, float x, float y, float w, float h)
    {
        double scale = (double)w * h / total;
        if (scale <= 0d) return;

        float rx = x, ry = y, rw = w, rh = h;
        int start = 0;

        while (start < tiles.Count && rw >= MinCellSide && rh >= MinCellSide)
        {
            double side = Math.Min(rw, rh);

            double first = tiles[start].Value * scale;
            double sum = first, min = first, max = first;
            int count = 1;

            while (start + count < tiles.Count)
            {
                double area = tiles[start + count].Value * scale;
                double nextSum = sum + area;
                double nextMin = Math.Min(min, area);
                double nextMax = Math.Max(max, area);

                if (Worst(nextSum, nextMin, nextMax, side) > Worst(sum, min, max, side)) break;

                sum = nextSum;
                min = nextMin;
                max = nextMax;
                count++;
            }

            if (rw >= rh)
            {
                float band = (float)Math.Min(sum / rh, rw);
                float cursor = ry;

                for (int i = 0; i < count; i++)
                {
                    HierarchicalNode<T> node = tiles[start + i];
                    float tileH = i == count - 1
                        ? ry + rh - cursor
                        : (float)(node.Value * scale / band);

                    Place(paper, in ctx, node, rx, cursor, band, tileH);
                    cursor += tileH;
                }

                rx += band;
                rw -= band;
            }
            else
            {
                float band = (float)Math.Min(sum / rw, rh);
                float cursor = rx;

                for (int i = 0; i < count; i++)
                {
                    HierarchicalNode<T> node = tiles[start + i];
                    float tileW = i == count - 1
                        ? rx + rw - cursor
                        : (float)(node.Value * scale / band);

                    Place(paper, in ctx, node, cursor, ry, tileW, band);
                    cursor += tileW;
                }

                ry += band;
                rh -= band;
            }

            start += count;
        }
    }

    /// <summary>Emits one node's cell and lays its children out inside it. The children's rect is inset by
    /// the cell gap on every side and by a header strip along the top, which is the band of the parent's
    /// own cell its label is read off once the children have painted over the rest of it.</summary>
    private void Place(Paper paper, in HierarchicalContext ctx, HierarchicalNode<T> node,
        float x, float y, float w, float h)
    {
        if (w < MinCellSide || h < MinCellSide) return;

        NodeCell(paper, in ctx, node, x, y, w, h);

        if (node.Children.Count == 0) return;

        float gap = CellGap;
        float header = LabelsEnabled ? HeaderHeight : 0f;

        Layout(paper, in ctx, node.Children,
            x + gap, y + gap + header,
            w - gap * 2f, h - gap * 2f - header);
    }

    /// <summary>Worst aspect ratio of a row of the given total area laid along <paramref name="side"/>,
    /// which is what the row growth loop minimises.</summary>
    private static double Worst(double sum, double min, double max, double side)
    {
        if (sum <= 0d || min <= 0d || side <= 0d) return double.MaxValue;

        double sum2 = sum * sum;
        double side2 = side * side;

        return Math.Max(side2 * max / sum2, sum2 / (side2 * min));
    }
}
