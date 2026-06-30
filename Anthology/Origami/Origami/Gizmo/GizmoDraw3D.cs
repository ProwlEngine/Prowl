// Based on ShapeBuilder from: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.
// Ported from old Paper's GuiDraw3D to use Quill Canvas. All methods match 1:1.

using System;
using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

public struct Stroke3D
{
    public float Thickness;
    public Color32 Color;
}

/// <summary>
/// Draws 3D shapes projected to 2D screen space via MVP matrix onto a Quill canvas.
/// Replaces old Paper's GuiDraw3D. Uses viewport/MVP stack pattern.
/// </summary>
public class GizmoDraw3D
{
    private readonly Stack<Rect> _viewports = new();
    private readonly Stack<Float4x4> _mvps = new();
    private Canvas? _canvas;

    private Rect _viewport => _viewports.Peek();
    private Float4x4 _mvp => _mvps.Peek();

    private bool hasViewport => _viewports.Count > 0;
    private bool hasMVP => _mvps.Count > 0;

    /// <summary>Set the canvas for drawing. Call before any draw operations.</summary>
    public void SetCanvas(Canvas canvas) => _canvas = canvas;

    // Scope helpers (match old using() pattern)
    public ViewportScope Viewport(Rect viewport) => new ViewportScope(this, viewport);
    public MVPScope Matrix(Float4x4 mvp) => new MVPScope(this, mvp);

    public struct ViewportScope : IDisposable
    {
        private readonly GizmoDraw3D _draw;
        public ViewportScope(GizmoDraw3D draw, Rect viewport) { _draw = draw; draw._viewports.Push(viewport); }
        public void Dispose() => _draw._viewports.Pop();
    }

    public struct MVPScope : IDisposable
    {
        private readonly GizmoDraw3D _draw;
        public MVPScope(GizmoDraw3D draw, Float4x4 mvp) { _draw = draw; draw._mvps.Push(mvp); }
        public void Dispose() => _draw._mvps.Pop();
    }

    // Also support direct set (for simpler usage)
    public void Begin(Canvas canvas, Rect viewport, Float4x4 mvp)
    {
        _canvas = canvas;
        _viewports.Clear(); _viewports.Push(viewport);
        _mvps.Clear(); _mvps.Push(mvp);
    }

    public void SetMVP(Float4x4 mvp) { _mvps.Clear(); _mvps.Push(mvp); }

    // ================================================================
    //  Drawing Methods (1:1 match with old GuiDraw3D)
    // ================================================================

    public void Arc(float radius, float startAngleDeg, float endAngleDeg, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP) return;
        var startRad = startAngleDeg * GizmoUtils.Deg2Rad;
        var endRad = endAngleDeg * GizmoUtils.Deg2Rad;
        var points = ArcPoints(radius, startRad, endRad);
        if (points.Count < 2) return;

        bool closed = points.Count > 0 && Float2.Length(points[0] - points[^1]) < 1e-2f;
        DrawPolyline(points, stroke, closed ? points.Count - 1 : points.Count, closed);
    }

    public void Circle(float radius, Stroke3D stroke) => Arc(radius, 0, 360, stroke);

    public void Quad(float radius, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP) return;
        var points = QuadPoints(radius * 2f);
        if (points.Count < 4) return;
        DrawPolyline(points, stroke, points.Count, true);
    }

    public void FilledCircle(float radius, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP) return;
        var points = ArcPoints(radius, 0, MathF.Tau);
        if (points.Count < 3) return;
        DrawFilledPoly(points, points.Count - 1, stroke.Color);
    }

    public void LineSegment(Float3 from, Float3 to, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP || _canvas == null) return;
        if (!WorldToScreen(from, out var a) || !WorldToScreen(to, out var b)) return;

        _canvas.SetStrokeColor(stroke.Color);
        _canvas.SetStrokeWidth(stroke.Thickness);
        _canvas.BeginPath();
        _canvas.MoveTo(a.X, a.Y);
        _canvas.LineTo(b.X, b.Y);
        _canvas.Stroke();
    }

    public void Arrow(Float3 from, Float3 to, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP || _canvas == null) return;
        if (!WorldToScreen(from, out var start) || !WorldToScreen(to, out var end)) return;

        Float2 dir = Float2.Normalize(end - start);
        Float2 cross = new Float2(-dir.Y, dir.X) * stroke.Thickness / 2f;

        _canvas.SetFillColor(stroke.Color);
        _canvas.BeginPath();
        _canvas.MoveTo(start.X - cross.X, start.Y - cross.Y);
        _canvas.LineTo(start.X + cross.X, start.Y + cross.Y);
        _canvas.LineTo(end.X, end.Y);
        _canvas.ClosePath();
        _canvas.Fill();
    }

    public void Polygon(IEnumerable<Float3> points3D, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP) return;
        var sp = ProjectPoints(points3D);
        if (sp.Count > 2) DrawFilledPoly(sp, sp.Count, stroke.Color);
    }

    public void Polyline(IEnumerable<Float3> points3D, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP) return;
        var sp = ProjectPoints(points3D);
        if (sp.Count > 1) DrawPolyline(sp, stroke, sp.Count, false);
    }

    public void Sector(float radius, float startAngleDeg, float endAngleDeg, Stroke3D stroke)
    {
        if (!hasViewport || !hasMVP) return;
        var startRad = startAngleDeg * GizmoUtils.Deg2Rad;
        var endRad = endAngleDeg * GizmoUtils.Deg2Rad;

        float angleDelta = endRad - startRad;
        int stepCount = Steps(MathF.Abs(angleDelta));
        if (stepCount < 2) return;

        float stepSize = angleDelta / (stepCount - 1);
        if (MathF.Abs(MathF.Abs(startRad - endRad) - MathF.Tau) < MathF.Abs(stepSize))
        {
            FilledCircle(radius, stroke);
            return;
        }

        var points = new List<Float2>();
        if (WorldToScreen(Float3.Zero, out var center)) points.Add(center);

        float sinAngle = MathF.Sin(startRad), cosAngle = MathF.Cos(startRad);
        float sinStep = MathF.Sin(stepSize), cosStep = MathF.Cos(stepSize);

        for (int i = 0; i < stepCount; i++)
        {
            if (WorldToScreen(new Float3(cosAngle * radius, 0, sinAngle * radius), out var pt))
                points.Add(pt);
            float ns = sinAngle * cosStep + cosAngle * sinStep;
            float nc = cosAngle * cosStep - sinAngle * sinStep;
            sinAngle = ns; cosAngle = nc;
        }

        if (points.Count > 2) DrawFilledPoly(points, points.Count, stroke.Color);
    }

    // ================================================================
    //  Internals
    // ================================================================

    private List<Float2> ArcPoints(float radius, float startRad, float endRad)
    {
        float angle = Math.Clamp(endRad - startRad, -MathF.Tau, MathF.Tau);
        int stepCount = Steps(MathF.Abs(angle));
        var points = new List<Float2>();
        float stepSize = angle / (stepCount - 1);

        for (int i = 0; i < stepCount; i++)
        {
            float step = stepSize * i;
            if (WorldToScreen(new Float3(MathF.Cos(startRad + step) * radius, 0, MathF.Sin(startRad + step) * radius), out var pt))
                points.Add(pt);
        }
        return points;
    }

    private List<Float2> QuadPoints(float size)
    {
        float h = size / 2f;
        var points = new List<Float2>();
        Float3[] corners = [new(-h, 0, -h), new(h, 0, -h), new(h, 0, h), new(-h, 0, h)];
        foreach (var c in corners)
            if (WorldToScreen(c, out var pt)) points.Add(pt);
        return points;
    }

    private List<Float2> ProjectPoints(IEnumerable<Float3> points3D)
    {
        var result = new List<Float2>();
        foreach (var p in points3D)
            if (WorldToScreen(p, out var sp)) result.Add(sp);
        return result;
    }

    private void DrawPolyline(List<Float2> points, Stroke3D stroke, int count, bool closed)
    {
        if (_canvas == null || count < 2) return;
        _canvas.SetStrokeColor(stroke.Color);
        _canvas.SetStrokeWidth(stroke.Thickness);
        _canvas.BeginPath();
        _canvas.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < count; i++)
            _canvas.LineTo(points[i].X, points[i].Y);
        if (closed) _canvas.ClosePath();
        _canvas.Stroke();
    }

    private void DrawFilledPoly(List<Float2> points, int count, Color32 color)
    {
        if (_canvas == null || count < 3) return;
        _canvas.SetFillColor(color);
        _canvas.BeginPath();
        _canvas.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < count; i++)
            _canvas.LineTo(points[i].X, points[i].Y);
        _canvas.ClosePath();
        _canvas.Fill();
    }

    private bool WorldToScreen(Float3 pos, out Float2 screenPos)
    {
        var result = GizmoUtils.WorldToScreen(_viewport, _mvp, pos);
        if (result.HasValue) { screenPos = result.Value; return true; }
        screenPos = Float2.Zero;
        return false;
    }

    private static int Steps(float angle) => Math.Max(1, (int)MathF.Ceiling(20f * MathF.Abs(angle)));
}
