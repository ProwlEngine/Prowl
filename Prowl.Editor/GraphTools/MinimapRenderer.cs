// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Bottom-right corner overlay showing the whole graph at thumbnail scale plus a
/// rectangle for the current viewport. Drawn in screen space (not affected by the
/// canvas's pan/zoom transform). Phase 3 will add click-to-recenter.
/// </summary>
public static class MinimapRenderer
{
    // Sized at ~65% of earlier dimensions — users found the larger minimap too dominant
    // on smaller windows. Kept the padding/margin proportional so corner spacing still
    // reads cleanly.
    public const float MinimapWidth  = 143f;
    public const float MinimapHeight = 98f;
    public const float MinimapMargin = 8f;
    public const float MinimapPadding = 4f;

    private static readonly Color32 BgColor       = new Color32(28, 30, 36, 220);
    private static readonly Color32 BorderColor   = new Color32(80, 84, 96, 255);
    private static readonly Color32 NodeDot       = new Color32(170, 180, 200, 220);
    private static readonly Color32 ViewportFill  = new Color32(255, 200, 80, 30);
    private static readonly Color32 ViewportStroke = new Color32(255, 200, 80, 220);

    /// <summary>
    /// Render the minimap into <paramref name="canvasViewport"/> (screen-space rect of the
    /// whole canvas Box). Returns the minimap's screen-space rect so callers can wire
    /// hit-testing later.
    /// </summary>
    public static Rect Draw(Canvas canvas, Rect canvasViewport, Graph graph, GraphCanvasView view)
    {
        // Anchor to bottom-right.
        float mx = (float)canvasViewport.Max.X - MinimapWidth - MinimapMargin;
        float my = (float)canvasViewport.Max.Y - MinimapHeight - MinimapMargin;
        var miniRect = new Rect(mx, my, mx + MinimapWidth, my + MinimapHeight);

        // Backing
        canvas.RoundedRectFilled(mx, my, MinimapWidth, MinimapHeight, 6f, 6f, 6f, 6f, BgColor);
        canvas.BeginPath();
        canvas.RoundedRect(mx, my, MinimapWidth, MinimapHeight, 6f);
        canvas.SetStrokeColor(BorderColor);
        canvas.SetStrokeWidth(1.0f);
        canvas.Stroke();

        if (!GraphLayout.ComputeGraphBounds(graph, out var minG, out var maxG))
            return miniRect; // empty graph — just the box

        // Pad graph bounds slightly so dots don't sit right on the minimap edge.
        float padG = 50f;
        minG -= new Float2(padG);
        maxG += new Float2(padG);
        Float2 graphSize = maxG - minG;

        // Inner content rect (after padding)
        float innerX = mx + MinimapPadding;
        float innerY = my + MinimapPadding;
        float innerW = MinimapWidth - MinimapPadding * 2;
        float innerH = MinimapHeight - MinimapPadding * 2;

        // Uniform-scale fit so nodes don't squish at extreme aspect ratios.
        float scale = MathF.Min(innerW / graphSize.X, innerH / graphSize.Y);
        float drawnW = graphSize.X * scale;
        float drawnH = graphSize.Y * scale;
        float ox = innerX + (innerW - drawnW) * 0.5f;
        float oy = innerY + (innerH - drawnH) * 0.5f;

        Float2 GraphToMini(Float2 g)
            => new Float2(ox + (g.X - minG.X) * scale, oy + (g.Y - minG.Y) * scale);

        // Clip everything from here on to the minimap's inner area so node dots and the
        // viewport rectangle never spill onto the minimap border / outside the panel.
        canvas.SaveState();
        canvas.IntersectScissor(innerX, innerY, innerW, innerH);

        // Groups — outline only (no fill) so node dots inside are still visible. The
        // outline uses the group's full-alpha colour so it stays legible against the
        // dark minimap background.
        foreach (var g in graph.Groups)
        {
            var p1 = GraphToMini(g.Position);
            var p2 = GraphToMini(g.Position + g.Size);
            float w = MathF.Max(2f, p2.X - p1.X);
            float h = MathF.Max(2f, p2.Y - p1.Y);
            var c = new Color32(g.PackedColor); c.A = 220;
            canvas.BeginPath();
            canvas.Rect(p1.X, p1.Y, w, h);
            canvas.SetStrokeColor(c);
            canvas.SetStrokeWidth(1.0f);
            canvas.Stroke();
        }

        // Sticky notes — tinted by their packed colour, slightly more opaque than
        // groups so they stay visible against group fills.
        foreach (var s in graph.StickyNotes)
        {
            var p1 = GraphToMini(s.Position);
            var p2 = GraphToMini(s.Position + s.Size);
            float w = MathF.Max(2f, p2.X - p1.X);
            float h = MathF.Max(2f, p2.Y - p1.Y);
            var c = new Color32(s.PackedColor); c.A = 180;
            canvas.RectFilled(p1.X, p1.Y, w, h, c);
        }

        // Node dots — drawn last so they sit on top of groups/stickies, matching
        // the main canvas layer order.
        foreach (var n in graph.Nodes)
        {
            var rect = GraphLayout.GetNodeRect(n);
            var p1 = GraphToMini(new Float2((float)rect.Min.X, (float)rect.Min.Y));
            var p2 = GraphToMini(new Float2((float)rect.Max.X, (float)rect.Max.Y));
            float w = MathF.Max(2f, p2.X - p1.X);
            float h = MathF.Max(2f, p2.Y - p1.Y);
            var c = (Color32)n.AccentColor;
            c.A = 220;
            canvas.RectFilled(p1.X, p1.Y, w, h, c);
        }

        // Viewport rectangle: where the user's currently looking. When the user is
        // zoomed out far the viewport rect can extend past the inner area — the
        // scissor above clips it cleanly to the minimap edge.
        Float2 vp0 = view.CanvasToGraph(Float2.Zero);
        Float2 vp1 = view.CanvasToGraph(new Float2((float)canvasViewport.Size.X, (float)canvasViewport.Size.Y));
        var vpA = GraphToMini(vp0);
        var vpB = GraphToMini(vp1);
        float vx = MathF.Min(vpA.X, vpB.X);
        float vy = MathF.Min(vpA.Y, vpB.Y);
        float vw = MathF.Abs(vpB.X - vpA.X);
        float vh = MathF.Abs(vpB.Y - vpA.Y);
        canvas.RectFilled(vx, vy, vw, vh, ViewportFill);
        canvas.BeginPath();
        canvas.Rect(vx, vy, vw, vh);
        canvas.SetStrokeColor(ViewportStroke);
        canvas.SetStrokeWidth(1.5f);
        canvas.Stroke();

        canvas.RestoreState();
        return miniRect;
    }
}
