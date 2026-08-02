// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>
/// Radial hierarchical chart. Every level of the forest becomes one ring, and a node takes the share of
/// its ring the value it aggregated is of the forest total, so a child always sits inside the angular
/// span of its parent. The rings run inward-out from <see cref="InnerRadius"/> unless
/// <see cref="HierarchicalCore{TSelf, T}.Invert"/> flips them, and the wedges are the one piece of
/// canvas geometry in this family - everything around them is still Paper layout.
/// </summary>
public sealed class SunburstChart<T> : HierarchicalCore<SunburstChart<T>, T>
{
    internal SunburstChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private float _startAngle = -90f;
    private float _innerRadius = 0.25f;

    private const float HighlightWidth = 1.5f;
    private const float MinLabelSweepDegrees = 12f;
    private const float MinLabelRingWidth = 14f;
    private const float LabelBoxWidth = 64f;
    private const float LabelBoxHeight = 12f;
    private const float MaxGapFraction = 0.25f;
    private const float DimAlpha = 0.35f;

    // --- API ---

    /// <summary>Angle, in degrees clockwise from three o'clock, the first node starts at. Defaults to
    /// -90, which puts it at twelve o'clock.</summary>
    public SunburstChart<T> StartAngle(float degrees) { _startAngle = degrees; return this; }

    /// <summary>Radius of the centre hole as a fraction of the chart's radius, clamped into [0, 0.9].
    /// The rings share whatever radius is left over. Defaults to 0.25.</summary>
    public SunburstChart<T> InnerRadius(float fraction) { _innerRadius = Math.Clamp(fraction, 0f, 0.9f); return this; }

    // --- Geometry ---

    /// <summary>Centre, radius and ring pitch of the current frame's rings, in plot-local pixels. Zoom
    /// scales the radius and pan slides the centre, both read straight off the view window so the wedges,
    /// the labels and the hit test all agree on where the circle is.</summary>
    private readonly struct RadialLayout
    {
        public readonly float Cx, Cy, Radius, Hole, RingW;

        public RadialLayout(float cx, float cy, float radius, float hole, float ringW)
        {
            Cx = cx; Cy = cy; Radius = radius; Hole = hole; RingW = ringW;
        }
    }

    private RadialLayout Layout(in HierarchicalContext ctx)
    {
        float scaleX = ctx.ViewW > 0f ? 1f / ctx.ViewW : 1f;
        float scaleY = ctx.ViewH > 0f ? 1f / ctx.ViewH : 1f;
        float scale = MathF.Min(scaleX, scaleY);

        float radius = MathF.Min(ctx.Width, ctx.Height) * 0.5f * scale;
        float hole = radius * _innerRadius;
        float ringW = (radius - hole) / (ctx.MaxDepth + 1);

        return new RadialLayout(ctx.MapX(0.5d), ctx.MapY(0.5d), radius, hole, ringW);
    }

    /// <summary>Which ring a node's depth lands on, counted outward from the hole. Inverting hands depth
    /// zero the outermost ring, so the leaves end up nearest the centre.</summary>
    private int RingOf(int depth, int maxDepth) => Inverted ? maxDepth - depth : depth;

    private static void RingRadii(in RadialLayout layout, int ring, out float innerR, out float outerR)
    {
        innerR = layout.Hole + ring * layout.RingW;
        outerR = innerR + layout.RingW;
    }

    /// <summary>The node's untrimmed span in radians, laid out clockwise from
    /// <see cref="StartAngle"/>.</summary>
    private void NodeArc(HierarchicalNode<T> node, double total, out float a0, out float a1)
    {
        a0 = (_startAngle + (float)(node.Start / total * 360d)) * DegToRad;
        a1 = a0 + (float)(node.Value / total * 360d) * DegToRad;
    }

    /// <summary>Trims the node's span and outer radius by <see cref="HierarchicalCore{TSelf, T}.Padding"/>
    /// so neighbouring wedges read as separate. The angular trim is the gap measured at the ring's mid
    /// radius, capped at a quarter of the sweep on each side so a thin wedge on a wide ring still gets
    /// drawn instead of closing up to nothing.</summary>
    private bool TrimWedge(in RadialLayout layout, float innerR, float outerR, ref float a0, ref float a1,
        out float paddedOuterR)
    {
        paddedOuterR = MathF.Max(innerR + 0.5f, outerR - MathF.Min(CellGap, layout.RingW * 0.4f));

        float sweep = a1 - a0;
        if (sweep <= 0f) return false;

        float midR = MathF.Max((innerR + paddedOuterR) * 0.5f, 1f);
        float gap = MathF.Min(CellGap / midR, sweep * MaxGapFraction);

        a0 += gap;
        a1 -= gap;

        return a1 > a0;
    }

    // --- Marks ---

    protected override void BuildMarks(Paper paper, in HierarchicalContext ctx)
    {
        RadialLayout layout = Layout(in ctx);
        if (layout.Radius <= 1f || layout.RingW <= 0.5f) return;

        IReadOnlyList<HierarchicalNode<T>> flat = ctx.Flat;
        double total = ctx.Total;
        int maxDepth = ctx.MaxDepth;
        int hover = ctx.HoverIndex;
        Color32 highlight = ToC32(_theme.Ink.C700);

        paper.Draw((canvas, rect) =>
        {
            float cx = layout.Cx + (float)rect.Min.X;
            float cy = layout.Cy + (float)rect.Min.Y;

            for (int i = 0; i < flat.Count; i++)
            {
                HierarchicalNode<T> node = flat[i];
                if (!node.Visible) continue;

                RingRadii(in layout, RingOf(node.Depth, maxDepth), out float innerR, out float outerR);
                if (outerR <= 0f) continue;

                NodeArc(node, total, out float a0, out float a1);
                if (!TrimWedge(in layout, innerR, outerR, ref a0, ref a1, out float paddedOuterR)) continue;

                Color32 fill = node.Dimmed ? ToC32(node.Color, DimAlpha) : ToC32(node.Color);
                ChartGeometry.PaintWedge(canvas, cx, cy, innerR, paddedOuterR, a0, a1, fill);

                if (node.Selected || hover == node.Index)
                    StrokeWedge(canvas, cx, cy, innerR, paddedOuterR, a0, a1, highlight);
            }
        });

        if (LabelsEnabled)
            DrawNodeLabels(paper, in ctx, in layout);
    }

    /// <summary>Strokes the outline of a wedge, which is how a hovered or selected node is picked out.
    /// The fill primitive has no stroked variant, so the same path is walked again here.</summary>
    private static void StrokeWedge(Canvas canvas, float cx, float cy, float innerR, float outerR,
        float a0, float a1, Color32 stroke)
    {
        if (outerR <= 0f || MathF.Abs(a1 - a0) < 1e-5f) return;

        int segments = ChartGeometry.ArcSegments(outerR, a1 - a0);

        canvas.BeginPath();
        for (int i = 0; i <= segments; i++)
        {
            float a = a0 + (a1 - a0) * i / segments;
            float x = cx + MathF.Cos(a) * outerR, y = cy + MathF.Sin(a) * outerR;
            if (i == 0) canvas.MoveTo(x, y); else canvas.LineTo(x, y);
        }
        for (int i = segments; i >= 0; i--)
        {
            float a = a0 + (a1 - a0) * i / segments;
            canvas.LineTo(cx + MathF.Cos(a) * innerR, cy + MathF.Sin(a) * innerR);
        }

        canvas.ClosePath();
        canvas.SetStrokeColor(stroke);
        canvas.SetStrokeWidth(HighlightWidth);
        canvas.Stroke();
    }

    // --- Labels ---

    /// <summary>One label per node with room for one: the wedge has to be wide enough in angle to read
    /// across and its ring thick enough to hold a line of text, otherwise the text would only clip against
    /// its neighbours.</summary>
    private void DrawNodeLabels(Paper paper, in HierarchicalContext ctx, in RadialLayout layout)
    {
        if (layout.RingW < MinLabelRingWidth) return;

        for (int i = 0; i < ctx.Flat.Count; i++)
        {
            HierarchicalNode<T> node = ctx.Flat[i];
            if (!node.Visible) continue;

            float sweepDegrees = (float)(node.Value / ctx.Total * 360d);
            if (sweepDegrees < MinLabelSweepDegrees) continue;

            string text = CellText(node);
            if (text.Length == 0) continue;

            RingRadii(in layout, RingOf(node.Depth, ctx.MaxDepth), out float innerR, out float outerR);
            NodeArc(node, ctx.Total, out float a0, out float a1);

            float mid = (a0 + a1) * 0.5f;
            float midR = (innerR + outerR) * 0.5f;
            float x = layout.Cx + MathF.Cos(mid) * midR;
            float y = layout.Cy + MathF.Sin(mid) * midR;

            using (paper.Box($"{_id}_node_label_{node.Index}")
                .PositionType(PositionType.SelfDirected)
                .Position(x - LabelBoxWidth * 0.5f, y - LabelBoxHeight * 0.5f)
                .Size(LabelBoxWidth, LabelBoxHeight)
                .Enter())
            {
                Origami.Label(paper, $"{_id}_node_label_txt_{node.Index}", text)
                    .XS()
                    .AlignCenter()
                    .Ellipsis()
                    .Width(LabelBoxWidth)
                    .Height(LabelBoxHeight)
                    .Show();
            }
        }
    }

    // --- Hit testing ---

    protected override int HitTest(in HierarchicalContext ctx, Float2 pointer)
    {
        RadialLayout layout = Layout(in ctx);
        if (layout.Radius <= 1f || layout.RingW <= 0.5f) return -1;

        float dx = pointer.X - layout.Cx, dy = pointer.Y - layout.Cy;
        float radius = MathF.Sqrt(dx * dx + dy * dy);
        if (radius < layout.Hole || radius > layout.Radius) return -1;

        int ring = Math.Clamp((int)((radius - layout.Hole) / layout.RingW), 0, ctx.MaxDepth);
        int depth = Inverted ? ctx.MaxDepth - ring : ring;

        float angle = MathF.Atan2(dy, dx);

        for (int i = 0; i < ctx.Flat.Count; i++)
        {
            HierarchicalNode<T> node = ctx.Flat[i];
            if (node.Depth != depth || !node.Visible) continue;

            NodeArc(node, ctx.Total, out float a0, out float a1);
            if (InSweep(angle, a0, a1 - a0)) return node.Index;
        }

        return -1;
    }

    /// <summary>Whether <paramref name="angle"/> falls inside the span starting at <paramref name="a0"/>,
    /// with both wrapped into the same turn so a span crossing three o'clock still matches.</summary>
    private static bool InSweep(float angle, float a0, float span)
    {
        const float TwoPi = MathF.PI * 2f;

        float delta = angle - a0;
        delta -= MathF.Floor(delta / TwoPi) * TwoPi;

        return delta <= span;
    }
}
