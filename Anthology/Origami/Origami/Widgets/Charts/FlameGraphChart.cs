// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

/// <summary>
/// Flame graph. Every node becomes one horizontal bar whose width is its share of the forest total and
/// whose row is its depth, growing downward from the top of the plot, so a child always sits directly
/// under the parent it was sampled from and a wide bar is a heavy branch. The view is horizontal only:
/// zoom and pan work along the value axis, while the rows keep their fixed height and simply clip
/// against the bottom of the plot.
/// </summary>
public sealed class FlameGraphChart<T> : HierarchicalCore<FlameGraphChart<T>, T>
{
    internal FlameGraphChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private float _rowHeight = DefaultRowHeight;

    private const float DefaultRowHeight = 18f;
    private const float MinRowHeight = 6f;
    private const float MinBarWidth = 1f;

    // --- API ---

    /// <summary>Height in pixels of one depth level, gap included. The drawn bar is this less the
    /// <c>Padding</c> gap, so the rows read as separate stripes.
    /// Defaults to 18 and is clamped to a height that can still carry a label.</summary>
    public FlameGraphChart<T> RowHeight(float height) { _rowHeight = MathF.Max(MinRowHeight, height); return this; }

    // --- View ---

    /// <summary>Rows are placed by depth rather than by the data, so a vertical view would only slide them
    /// out of sight. Vertical overflow is clipped by the plot box instead.</summary>
    protected override bool DefaultPanY => false;

    // --- Marks ---

    protected override void BuildMarks(Paper paper, in HierarchicalContext ctx)
    {
        if (ctx.Total <= 0d) return;

        float barHeight = MathF.Max(MinBarWidth, _rowHeight - CellGap);

        for (int i = 0; i < ctx.Flat.Count; i++)
        {
            HierarchicalNode<T> node = ctx.Flat[i];
            if (!node.Visible) continue;

            float y = ctx.PlotT + node.Depth * _rowHeight;
            if (y >= ctx.PlotB || y + barHeight <= ctx.PlotT) continue;

            float left = ctx.MapX(node.Start / ctx.Total);
            float right = ctx.MapX((node.Start + node.Value) / ctx.Total);
            if (right <= ctx.PlotL || left >= ctx.PlotR) continue;

            left = MathF.Max(left, ctx.PlotL);
            right = MathF.Min(right, ctx.PlotR);

            float width = right - left;
            if (width < MinBarWidth) continue;

            NodeCell(paper, in ctx, node, left, y, width, barHeight);
        }
    }
}
