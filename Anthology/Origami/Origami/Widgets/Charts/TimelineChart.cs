// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

/// <summary>
/// Timeline. Every item becomes one horizontal bar spanning its start and end against a shared time axis,
/// dropped into the row its thread and track put it in, so reading down the chart is reading across the
/// machine and reading across it is reading through time. Spans that overlap inside one track stack into
/// extra rows rather than covering each other. The view is horizontal only: zoom and pan work along the
/// time axis, while the rows keep their fixed height and clip against the bottom of the plot.
/// </summary>
public sealed class TimelineChart<T> : PerformanceCore<TimelineChart<T>, T>
{
    internal TimelineChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private const float RowHeight = 18f;
    private const float RulerHeight = 16f;
    private const float MinBarWidth = 2f;

    protected override void BuildMarks(Paper paper, in HierarchicalContext ctx)
    {
        if (ctx.Total <= 0d) return;

        DrawTimeRuler(paper, in ctx, RulerHeight);

        float laneTop = ctx.PlotT + RulerHeight;
        DrawLaneHeaders(paper, in ctx, laneTop, RowHeight);

        float barHeight = MathF.Max(1f, RowHeight - CellGap);

        for (int i = 0; i < ctx.Flat.Count; i++)
        {
            HierarchicalNode<T> node = ctx.Flat[i];

            float y = laneTop + node.Depth * RowHeight;
            if (y >= ctx.PlotB || y + barHeight <= laneTop) continue;

            float left = ctx.MapX(node.Start / ctx.Total);
            float right = ctx.MapX((node.Start + node.Value) / ctx.Total);
            if (right < left) right = left;
            if (right < ctx.PlotL || left > ctx.PlotR) continue;

            left = MathF.Max(left, ctx.PlotL);
            right = MathF.Min(MathF.Max(right, left + MinBarWidth), ctx.PlotR);

            float width = right - left;
            if (width < MinBarWidth) continue;

            NodeCell(paper, in ctx, node, left, y, width, barHeight);
        }
    }
}
