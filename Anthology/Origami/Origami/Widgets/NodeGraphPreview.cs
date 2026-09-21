// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Paints a graph as a plan: one small rectangle per node, optional hairlines for the wires. The
/// minimap is one use, a sub graph node showing what is inside it is another. It reads the same model
/// the graph widget does and keeps no state, so it costs a few rectangles per frame.
/// </summary>
public static class NodeGraphPreview
{
    /// <summary>
    /// The height a node lays out to, from the same rules the graph itself uses. Handy for measuring a
    /// graph before it is drawn.
    /// </summary>
    public static float MeasureHeight(GraphNode node, OrigamiMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(metrics);

        CountSides(node, out int left, out int right, out int top, out int bottom);
        if (node.Pill) return Math.Max(NodeGraphBuilder.PillH, Math.Max(left, right) * 16f + 6f);
        // Collapsed still has to fit its sockets, so a node with many ports folds to a taller strip.
        if (node.Folded) return Math.Max(metrics.HeaderHeight, Math.Max(left, right) * NodeGraphBuilder.CollapsedPortSpacing);

        int rows = Math.Max(left, right);
        float body = rows > 0
            ? NodeGraphBuilder.BodyPadTop + rows * metrics.RowHeight + NodeGraphBuilder.BodyPadBottom
            : NodeGraphBuilder.BodyPadBottom;
        if (node.Body != null && node.BodyHeight > 0f) body += node.BodyHeight;
        return metrics.HeaderHeight + body;
    }

    /// <summary>
    /// The card width a node lays out to. A node with ports along its top or bottom edge is widened to
    /// fit them, so its authored width is not where it actually ends.
    /// </summary>
    public static float MeasureWidth(GraphNode node, OrigamiMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(metrics);

        CountSides(node, out _, out _, out int top, out int bottom);
        int topBottom = Math.Max(top, bottom);
        float spread = topBottom > 0 ? topBottom * NodeGraphBuilder.TopBotSpacing : 0f;
        if (node.Pill) return Math.Max(node.Width, Math.Max(44f, spread > 0f ? spread + 16f : 44f));
        return Math.Max(node.Width, spread > 0f ? spread + 24f : 0f);
    }

    /// <summary>The rectangle every node of a graph fits inside, in graph space.</summary>
    public static bool TryMeasure(IReadOnlyList<GraphNode> nodes, OrigamiMetrics metrics, out Float2 min, out Float2 size)
    {
        min = default; size = default;
        if (nodes == null || nodes.Count == 0) return false;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var n in nodes)
        {
            minX = Math.Min(minX, n.Position.X); minY = Math.Min(minY, n.Position.Y);
            maxX = Math.Max(maxX, n.Position.X + MeasureWidth(n, metrics));
            maxY = Math.Max(maxY, n.Position.Y + MeasureHeight(n, metrics));
        }

        min = new Float2(minX, minY);
        size = new Float2(Math.Max(maxX - minX, 1f), Math.Max(maxY - minY, 1f));
        return true;
    }

    /// <summary>
    /// Draws the graph scaled to fit <paramref name="area"/>. <paramref name="wires"/> may be null to
    /// draw the nodes alone, which is usually enough at thumbnail size.
    /// </summary>
    public static void Paint(
        Canvas canvas,
        Rect area,
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphConnection>? wires,
        OrigamiTheme theme,
        float padding = 5f,
        float minBoxSize = 2f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(theme);
        if (!TryMeasure(nodes, theme.Metrics, out Float2 min, out Float2 size)) return;

        float w = (float)area.Size.X - padding * 2f, h = (float)area.Size.Y - padding * 2f;
        if (w <= 1f || h <= 1f) return;

        // One scale for both axes, so the plan keeps the graph's shape.
        float scale = Math.Min(w / size.X, h / size.Y);
        float ox = (float)area.Min.X + padding + (w - size.X * scale) * 0.5f;
        float oy = (float)area.Min.Y + padding + (h - size.Y * scale) * 0.5f;

        Float2 Place(Float2 graphPoint)
            => new(ox + (graphPoint.X - min.X) * scale, oy + (graphPoint.Y - min.Y) * scale);

        if (wires is { Count: > 0 })
        {
            canvas.SaveState();
            canvas.SetStrokeWidth(1f);
            canvas.SetStrokeColor(ToColor32(theme.BorderStrong, 1f));
            foreach (var c in wires)
            {
                if (!TryCentre(nodes, c.FromNode, theme.Metrics, out Float2 a) ||
                    !TryCentre(nodes, c.ToNode, theme.Metrics, out Float2 b)) continue;

                Float2 pa = Place(a), pb = Place(b);
                canvas.BeginPath();
                canvas.MoveTo(pa.X, pa.Y);
                canvas.LineTo(pb.X, pb.Y);
                canvas.Stroke();
            }
            canvas.RestoreState();
        }

        foreach (var n in nodes)
        {
            Float2 p = Place(n.Position);
            float bw = Math.Max(minBoxSize, MeasureWidth(n, theme.Metrics) * scale);
            float bh = Math.Max(minBoxSize, MeasureHeight(n, theme.Metrics) * scale);
            canvas.RectFilled(p.X, p.Y, bw, bh, ToColor32(n.Accent ?? theme.Primary.C500, n.Pinned ? 0.55f : 0.9f));
        }
    }

    private static bool TryCentre(IReadOnlyList<GraphNode> nodes, string id, OrigamiMetrics metrics, out Float2 centre)
    {
        foreach (var n in nodes)
        {
            if (n.Id != id) continue;
            centre = new Float2(n.Position.X + MeasureWidth(n, metrics) * 0.5f, n.Position.Y + MeasureHeight(n, metrics) * 0.5f);
            return true;
        }
        centre = default;
        return false;
    }

    private static void CountSides(GraphNode n, out int left, out int right, out int top, out int bottom)
    {
        int l = 0, r = 0, t = 0, b = 0;
        foreach (var p in n.Inputs) Count(p, false);
        foreach (var p in n.Outputs) Count(p, true);
        left = l; right = r; top = t; bottom = b;

        void Count(GraphPort p, bool output)
        {
            switch (p.Side ?? (output ? PortSide.Right : PortSide.Left))
            {
                case PortSide.Left: l++; break;
                case PortSide.Right: r++; break;
                case PortSide.Top: t++; break;
                default: b++; break;
            }
        }
    }

    private static Color32 ToColor32(Color c, float alpha)
        => Color32.FromArgb((int)(c.A * alpha), c.R, c.G, c.B);
}
