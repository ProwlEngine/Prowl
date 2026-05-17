// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// All static draw helpers for the graph canvas grid, groups, wires, nodes, ports,
/// sticky notes, drag-overlay. Stateless and Paper-free; takes a Quill <see cref="Canvas"/>
/// already transformed into graph space.
/// </summary>
/// <remarks>
/// LOD is implicit: each draw routine consults the current <c>zoom</c> argument to decide
/// whether to draw text, port labels, etc. The renderer never queries the canvas's
/// transform directly so LOD math stays predictable.
/// </remarks>
public static class GraphRendering
{
    // --- Common colors (grid / background only node colors live in DefaultNodeRenderer) -
    private static readonly Color32 BgColor  = new Color32(22, 22, 26, 255);
    private static readonly Color32 GridLine = new Color32(40, 42, 50, 255);

    // --- Grid -------------------------------------------------------------------------

    /// <summary>
    /// Draw the background grid: thin vertical + horizontal lines, plus brighter dots
    /// at every 5th intersection (the "major" grid). Grid spacing adapts so screen-pixel
    /// spacing stays between 12 and 80 px regardless of zoom level.
    /// </summary>
    public static void DrawGrid(Canvas canvas, Rect visibleGraphRect, float zoom)
    {
        float baseStep = 32f;
        while (baseStep * zoom < 12f) baseStep *= 2f;
        while (baseStep * zoom > 80f) baseStep *= 0.5f;
        float majorEvery = 5f * baseStep;

        float x0 = MathF.Floor((float)visibleGraphRect.Min.X / baseStep) * baseStep;
        float y0 = MathF.Floor((float)visibleGraphRect.Min.Y / baseStep) * baseStep;
        float x1 = (float)visibleGraphRect.Max.X;
        float y1 = (float)visibleGraphRect.Max.Y;

        canvas.SetStrokeColor(GridLine);
        canvas.SetStrokeWidth(1.0f); // pixels; doesn't scale with transform

        // Vertical lines
        for (float x = x0; x <= x1; x += baseStep)
        {
            canvas.BeginPath();
            canvas.MoveTo(x, y0);
            canvas.LineTo(x, y1);
            canvas.Stroke();
        }
        // Horizontal lines
        for (float y = y0; y <= y1; y += baseStep)
        {
            canvas.BeginPath();
            canvas.MoveTo(x0, y);
            canvas.LineTo(x1, y);
            canvas.Stroke();
        }

        // Major intersection dots disabled for now they were sliding too aggressively
        // with pan/zoom relative to the lines, looked janky.
        // float dotR = MathF.Max(1.5f / zoom, 0.5f);
        // for (float y = y0; y <= y1; y += majorEvery)
        //     for (float x = x0; x <= x1; x += majorEvery)
        //         canvas.CircleFilled(x, y, dotR, GridIntersection, 6);
    }

    // --- Background fill (full canvas area, screen-space). Drawn before the transform. -

    public static void DrawBackground(Canvas canvas, Rect screenRect)
    {
        canvas.RectFilled((float)screenRect.Min.X, (float)screenRect.Min.Y,
                          (float)screenRect.Size.X, (float)screenRect.Size.Y, BgColor);
    }

    // --- Wires (cubic bezier) --------------------------------------------------------

    /// <summary>
    /// Draw a wire between two ports using the given <paramref name="style"/>.
    /// Bezier = horizontal-tangent cubic, Linear = straight,
    /// Rectilinear = right-angle Z route. Stroke width stays constant in screen
    /// pixels regardless of zoom (Quill's stroke is in screen units).
    /// </summary>
    public static void DrawWire(Canvas canvas, Float2 from, Float2 to, Color32 color, float zoom, float thickness = 2.5f, WireRoutingStyle style = WireRoutingStyle.Bezier)
    {
        canvas.BeginPath();
        canvas.MoveTo(from.X, from.Y);

        switch (style)
        {
            case WireRoutingStyle.Linear:
                canvas.LineTo(to.X, to.Y);
                break;

            case WireRoutingStyle.Rectilinear:
                {
                    // Z-route: half-x out, full-y across, then half-x into target.
                    float midX = (from.X + to.X) * 0.5f;
                    canvas.LineTo(midX, from.Y);
                    canvas.LineTo(midX, to.Y);
                    canvas.LineTo(to.X, to.Y);
                }
                break;

            case WireRoutingStyle.Bezier:
            default:
                {
                    float dx = MathF.Abs(to.X - from.X);
                    float tangent = MathF.Max(40f, dx * 0.5f);
                    Float2 c1 = new Float2(from.X + tangent, from.Y);
                    Float2 c2 = new Float2(to.X - tangent, to.Y);
                    canvas.BezierCurveTo(c1.X, c1.Y, c2.X, c2.Y, to.X, to.Y);
                }
                break;
        }

        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(thickness);
        canvas.SetStrokeStartCap(EndCapStyle.Round);
        canvas.SetStrokeEndCap(EndCapStyle.Round);
        canvas.Stroke();
    }

    // --- Groups (back-most layer) ----------------------------------------------------

    public static void DrawGroup(Canvas canvas, NodeGroup group, float zoom, Prowl.Scribe.FontFile? font, bool isSelected = false)
    {
        const float titleHeight = 24f;

        var baseColor = new Color32(group.PackedColor);
        var fill = baseColor; fill.A = 40;            // faint body tint
        var titleFill = baseColor; titleFill.A = 180; // solid title bar
        var border = baseColor; border.A = 200;

        float x = group.Position.X, y = group.Position.Y;
        float w = group.Size.X, h = group.Size.Y;

        // Body.
        canvas.RoundedRectFilled(x, y, w, h, 8f, 8f, 8f, 8f, fill);

        // Title strip rounded corners match the body's top.
        canvas.RoundedRectFilled(x, y, w, titleHeight, 8f, 8f, 0f, 0f, titleFill);

        // Outline.
        canvas.BeginPath();
        canvas.RoundedRect(x, y, w, h, 8f);
        canvas.SetStrokeColor(isSelected ? new Color32(255, 200, 80, 255) : border);
        canvas.SetStrokeWidth(isSelected ? 2.5f : 2f);
        canvas.Stroke();

        // Title text.
        if (font != null && zoom > 0.35f && !string.IsNullOrEmpty(group.Title))
        {
            canvas.DrawText(group.Title, x + 8, y + 5,
                new Color32(238, 240, 245, 255), 14f, font);
        }

        // Resize grip (bottom-right). Matches the sticky pattern so handle discovery
        // stays consistent across element types.
        float hx1 = x + w, hy1 = y + h;
        float hx0 = hx1 - 14f, hy0 = hy1 - 14f;
        var gripCol = new Color32(
            (byte)Math.Max(0, baseColor.R - 60),
            (byte)Math.Max(0, baseColor.G - 60),
            (byte)Math.Max(0, baseColor.B - 60),
            220);
        canvas.BeginPath();
        canvas.MoveTo(hx1, hy0);
        canvas.LineTo(hx1, hy1);
        canvas.LineTo(hx0, hy1);
        canvas.ClosePath();
        canvas.SetFillColor(gripCol);
        canvas.Fill();
    }

    // --- Sticky notes ----------------------------------------------------------------

    public static void DrawStickyNote(Canvas canvas, StickyNote note, float zoom, Prowl.Scribe.FontFile? font, bool isSelected = false)
    {
        var bg = new Color32(note.PackedColor);
        float x = note.Position.X, y = note.Position.Y;
        float w = note.Size.X, h = note.Size.Y;

        canvas.RoundedRectFilled(x, y, w, h, 6f, 6f, 6f, 6f, bg);

        // Title strip darker tint of the body colour.
        var titleBg = new Color32(
            (byte)Math.Max(0, bg.R - 30),
            (byte)Math.Max(0, bg.G - 30),
            (byte)Math.Max(0, bg.B - 30),
            bg.A);
        canvas.RoundedRectFilled(x, y, w, 22f, 6f, 6f, 0f, 0f, titleBg);

        // Dark text reads well on a light amber body. Title is always shown above a
        // modest zoom threshold; body appears a bit later for LOD but doesn't require
        // non-empty text so users see the "empty body" area during editing.
        var ink = new Color32(40, 40, 50, 255);
        if (font != null && zoom > 0.35f)
        {
            canvas.DrawText(note.Title, x + 6, y + 4, ink, 14f, font);
            if (zoom > 0.55f && !string.IsNullOrEmpty(note.Body))
                canvas.DrawText(note.Body, x + 6, y + 28, ink, 12f, font);
        }

        // Selection border matches the amber outline used on selected nodes so the
        // selection convention stays consistent across element types.
        if (isSelected)
        {
            canvas.BeginPath();
            canvas.RoundedRect(x, y, w, h, 6f);
            canvas.SetStrokeColor(new Color32(255, 200, 80, 255));
            canvas.SetStrokeWidth(2.0f);
            canvas.Stroke();
        }

        // Resize handle small triangular grip in the bottom-right corner. Drawn on
        // top of the body so it's always visible. The hit-test uses the same 16x16
        // corner region (see GraphEditorWindow.IsOverStickyResizeHandle).
        float hx1 = x + w, hy1 = y + h;
        float hx0 = hx1 - 14f, hy0 = hy1 - 14f;
        var gripCol = new Color32(
            (byte)Math.Max(0, bg.R - 60),
            (byte)Math.Max(0, bg.G - 60),
            (byte)Math.Max(0, bg.B - 60),
            bg.A);
        canvas.BeginPath();
        canvas.MoveTo(hx1, hy0);
        canvas.LineTo(hx1, hy1);
        canvas.LineTo(hx0, hy1);
        canvas.ClosePath();
        canvas.SetFillColor(gripCol);
        canvas.Fill();
    }

    // --- Nodes -----------------------------------------------------------------------

    /// <summary>
    /// Dispatch into the node's registered <see cref="NodeRenderer"/> (or the default
    /// card-style renderer). Node-type authors can plug in a custom renderer via
    /// <c>[NodeRenderer(typeof(MyNode))]</c> on a <see cref="NodeRenderer"/> subclass.
    /// </summary>
    public static void DrawNode(Canvas canvas, Prowl.Runtime.GraphTools.Graph graph,
        Node node, bool isSelected, bool isHovered,
        (string portName, PortDirection direction)? hoveredPort,
        float zoom, Prowl.Scribe.FontFile? font)
    {
        node.EnsureDefined();
        NodeRendererRegistry.GetRenderer(node)
            .Draw(canvas, graph, node, isSelected, isHovered, hoveredPort, zoom, font);
    }

    // --- In-progress drag wire (overlay) ---------------------------------------------

    public static void DrawDragWire(Canvas canvas, Float2 from, Float2 to, Color32 color, float zoom, WireRoutingStyle style = WireRoutingStyle.Bezier)
        => DrawWire(canvas, from, to, color, zoom, thickness: 2.0f, style: style);

    // --- Marquee selection rectangle -------------------------------------------------

    public static void DrawMarquee(Canvas canvas, Rect r, float zoom)
    {
        var fill = new Color32(120, 160, 220, 30);
        var stroke = new Color32(120, 160, 220, 200);
        canvas.RectFilled((float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y, fill);
        canvas.BeginPath();
        canvas.Rect((float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y);
        canvas.SetStrokeColor(stroke);
        canvas.SetStrokeWidth(1.0f);
        canvas.Stroke();
    }
}
