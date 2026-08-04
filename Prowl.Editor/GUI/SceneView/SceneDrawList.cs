// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Vector;

using GizmoDraw3D = Prowl.OrigamiUI.Gizmo.GizmoDraw3D;
using GizmoUtils = Prowl.OrigamiUI.Gizmo.GizmoUtils;
using Stroke3D = Prowl.OrigamiUI.Gizmo.Stroke3D;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>Grab-point shapes for <see cref="SceneDrawList.Dot"/>.</summary>
public enum HandleCap
{
    Dot,
    Square,
    Diamond,
    Circle,
}

/// <summary>
/// Immediate-mode 3D drawing for scene tools and handles, projected onto the viewport's 2D overlay.
///
/// <para>Tools run during the input pass, but the Quill canvas only exists inside the overlay
/// callback, so calls are recorded here and replayed in <see cref="Replay"/>. Recording also means
/// every command still knows its 3D geometry at replay time, which is where per-command depth will
/// be resolved once depth-aware handle rendering lands - individual tools will not have to change.</para>
///
/// <para>This is the thick, anti-aliased canvas path. Use <see cref="Prowl.Runtime.Debug"/> gizmos
/// instead when the geometry should be depth-tested against the scene.</para>
/// </summary>
public sealed class SceneDrawList
{
    private enum Kind { Line, Polyline, Polygon, Circle, Arc, Sector, Arrow, Dot, Label, ScreenRect }

    private struct Command
    {
        public Kind Kind;
        public Float3 A, B, Normal;
        public Float2 ScreenA, ScreenB;
        public float Radius, Thickness, Start, End, SizePx;
        public Color32 Color;
        public bool Closed, Filled;
        public HandleCap Cap;
        public string? Text;
        public int PointStart, PointCount;
    }

    private readonly List<Command> _commands = new();
    private readonly List<Float3> _points = new();
    private readonly GizmoDraw3D _draw = new();

    private HandleContext _ctx = null!;

    /// <summary>Default stroke width for handle geometry, in pixels.</summary>
    public float DefaultThickness { get; set; } = 2f;

    /// <summary>Text size for <see cref="Label"/>, in pixels.</summary>
    public float LabelPixelSize { get; set; } = 12f;

    internal void Begin(HandleContext ctx)
    {
        _ctx = ctx;
        _commands.Clear();
        _points.Clear();
    }

    // ================================================================
    //  3D primitives
    // ================================================================

    public void Line(Float3 a, Float3 b, Color32 color, float thickness = 0f)
        => _commands.Add(new Command { Kind = Kind.Line, A = a, B = b, Color = color, Thickness = Thick(thickness) });

    public void Polyline(ReadOnlySpan<Float3> points, Color32 color, float thickness = 0f, bool closed = false)
        => AddPoints(Kind.Polyline, points, color, Thick(thickness), closed, filled: false);

    /// <summary>Filled convex polygon. Use for face highlights.</summary>
    public void Polygon(ReadOnlySpan<Float3> points, Color32 color)
        => AddPoints(Kind.Polygon, points, color, 0f, closed: true, filled: true);

    public void Circle(Float3 center, Float3 normal, float radius, Color32 color, float thickness = 0f)
        => _commands.Add(new Command
        {
            Kind = Kind.Circle, A = center, Normal = normal, Radius = radius,
            Color = color, Thickness = Thick(thickness),
        });

    public void Arc(Float3 center, Float3 normal, float radius, float startDegrees, float endDegrees,
                    Color32 color, float thickness = 0f)
        => _commands.Add(new Command
        {
            Kind = Kind.Arc, A = center, Normal = normal, Radius = radius,
            Start = startDegrees, End = endDegrees, Color = color, Thickness = Thick(thickness),
        });

    /// <summary>Filled pie slice, for rotation feedback.</summary>
    public void Sector(Float3 center, Float3 normal, float radius, float startDegrees, float endDegrees, Color32 color)
        => _commands.Add(new Command
        {
            Kind = Kind.Sector, A = center, Normal = normal, Radius = radius,
            Start = startDegrees, End = endDegrees, Color = color,
        });

    public void Arrow(Float3 from, Float3 to, Color32 color, float thickness = 0f)
        => _commands.Add(new Command { Kind = Kind.Arrow, A = from, B = to, Color = color, Thickness = Thick(thickness) });

    /// <summary>A screen-constant grab point at a world position.</summary>
    public void Dot(Float3 center, Color32 color, float sizePixels = 7f, HandleCap cap = HandleCap.Square)
        => _commands.Add(new Command { Kind = Kind.Dot, A = center, Color = color, SizePx = sizePixels, Cap = cap });

    /// <summary>A wire box, axis-aligned in the given frame.</summary>
    public void WireCube(Float3 center, Float3 halfExtents, Color32 color, float thickness = 0f)
    {
        Span<Float3> c = stackalloc Float3[8];
        for (int i = 0; i < 8; i++)
            c[i] = center + new Float3(
                (i & 1) == 0 ? -halfExtents.X : halfExtents.X,
                (i & 2) == 0 ? -halfExtents.Y : halfExtents.Y,
                (i & 4) == 0 ? -halfExtents.Z : halfExtents.Z);

        Polyline([c[0], c[1], c[3], c[2]], color, thickness, closed: true);
        Polyline([c[4], c[5], c[7], c[6]], color, thickness, closed: true);
        Line(c[0], c[4], color, thickness);
        Line(c[1], c[5], color, thickness);
        Line(c[2], c[6], color, thickness);
        Line(c[3], c[7], color, thickness);
    }

    // ================================================================
    //  Text and screen space
    // ================================================================

    /// <summary>A text label anchored to a world position. The staple of dimension readouts.</summary>
    public void Label(Float3 world, string text, Color32 color)
        => _commands.Add(new Command { Kind = Kind.Label, A = world, Text = text, Color = color });

    /// <summary>Label at the midpoint of a segment, showing its length.</summary>
    public void LengthLabel(Float3 a, Float3 b, Color32 color, string format = "0.###")
        => Label((a + b) * 0.5f, Float3.Distance(a, b).ToString(format), color);

    /// <summary>A rectangle in viewport-absolute screen pixels. Used for marquee selection.</summary>
    public void ScreenRect(Float2 min, Float2 max, Color32 fill, Color32 outline, float thickness = 1f)
        => _commands.Add(new Command
        {
            Kind = Kind.ScreenRect, ScreenA = min, ScreenB = max,
            Color = fill, Thickness = thickness, Filled = true,
            Start = outline.R, End = outline.G, Radius = outline.B, SizePx = outline.A,
        });

    // ================================================================
    //  Replay
    // ================================================================

    /// <summary>Draw everything recorded this frame. Called by the viewport from its overlay pass.</summary>
    public void Replay(Canvas canvas)
    {
        if (_commands.Count == 0) return;

        _draw.Begin(canvas, _ctx.Viewport, _ctx.ViewProjection);

        foreach (Command cmd in _commands)
        {
            var stroke = new Stroke3D { Color = cmd.Color, Thickness = cmd.Thickness };
            switch (cmd.Kind)
            {
                case Kind.Line:
                    _draw.LineSegment(cmd.A, cmd.B, stroke);
                    break;

                case Kind.Arrow:
                    _draw.Arrow(cmd.A, cmd.B, stroke);
                    break;

                case Kind.Polyline:
                    DrawPoly(cmd, stroke, filled: false);
                    break;

                case Kind.Polygon:
                    DrawPoly(cmd, stroke, filled: true);
                    break;

                case Kind.Circle:
                case Kind.Arc:
                case Kind.Sector:
                    DrawRadial(cmd, stroke);
                    break;

                case Kind.Dot:
                    DrawDot(canvas, cmd);
                    break;

                case Kind.Label:
                    DrawLabel(canvas, cmd);
                    break;

                case Kind.ScreenRect:
                    DrawScreenRect(canvas, cmd);
                    break;
            }
        }

        _commands.Clear();
        _points.Clear();
    }

    private void DrawPoly(in Command cmd, in Stroke3D stroke, bool filled)
    {
        var span = _points.GetRange(cmd.PointStart, cmd.PointCount);
        if (filled) _draw.Polygon(span, stroke);
        else if (cmd.Closed) _draw.Polyline(Close(span), stroke);
        else _draw.Polyline(span, stroke);
    }

    private static List<Float3> Close(List<Float3> pts)
    {
        if (pts.Count > 0) pts.Add(pts[0]);
        return pts;
    }

    // Radial shapes are authored in the XY plane at the origin, so the MVP carries the placement.
    private void DrawRadial(in Command cmd, in Stroke3D stroke)
    {
        Float4x4 frame = PlaneFrame(cmd.A, cmd.Normal);
        using (_draw.Matrix(_ctx.ViewProjection * frame))
        {
            switch (cmd.Kind)
            {
                case Kind.Circle: _draw.Circle(cmd.Radius, stroke); break;
                case Kind.Arc: _draw.Arc(cmd.Radius, cmd.Start, cmd.End, stroke); break;
                case Kind.Sector: _draw.Sector(cmd.Radius, cmd.Start, cmd.End, stroke); break;
            }
        }
    }

    private static Float4x4 PlaneFrame(Float3 origin, Float3 normal)
    {
        Float3 n = Float3.LengthSquared(normal) < 1e-12f ? Float3.UnitZ : Float3.Normalize(normal);
        Float3 helper = MathF.Abs(Float3.Dot(n, Float3.UnitY)) > 0.99f ? Float3.UnitX : Float3.UnitY;
        Float3 x = Float3.Normalize(Float3.Cross(helper, n));
        Float3 y = Float3.Cross(n, x);
        return new Float4x4(
            x.X, y.X, n.X, origin.X,
            x.Y, y.Y, n.Y, origin.Y,
            x.Z, y.Z, n.Z, origin.Z,
            0, 0, 0, 1);
    }

    private void DrawDot(Canvas canvas, in Command cmd)
    {
        Float2? p = _ctx.WorldToScreen(cmd.A);
        if (p is null) return;
        float x = p.Value.X, y = p.Value.Y, r = cmd.SizePx * 0.5f;

        canvas.SetFillColor(cmd.Color);
        canvas.BeginPath();
        switch (cmd.Cap)
        {
            case HandleCap.Circle:
            case HandleCap.Dot:
                canvas.Circle(x, y, r);
                break;
            case HandleCap.Diamond:
                canvas.MoveTo(x, y - r); canvas.LineTo(x + r, y);
                canvas.LineTo(x, y + r); canvas.LineTo(x - r, y);
                canvas.ClosePath();
                break;
            default:
                canvas.MoveTo(x - r, y - r); canvas.LineTo(x + r, y - r);
                canvas.LineTo(x + r, y + r); canvas.LineTo(x - r, y + r);
                canvas.ClosePath();
                break;
        }
        canvas.Fill();
    }

    private void DrawLabel(Canvas canvas, in Command cmd)
    {
        Float2? p = _ctx.WorldToScreen(cmd.A);
        if (p is null || string.IsNullOrEmpty(cmd.Text)) return;

        var font = Theming.EditorTheme.DefaultFont;
        if (font == null) return;
        canvas.DrawText(cmd.Text, (float)p.Value.X, (float)p.Value.Y, cmd.Color, LabelPixelSize, font);
    }

    private static void DrawScreenRect(Canvas canvas, in Command cmd)
    {
        float x = MathF.Min(cmd.ScreenA.X, cmd.ScreenB.X);
        float y = MathF.Min(cmd.ScreenA.Y, cmd.ScreenB.Y);
        float w = MathF.Abs(cmd.ScreenB.X - cmd.ScreenA.X);
        float h = MathF.Abs(cmd.ScreenB.Y - cmd.ScreenA.Y);

        canvas.RectFilled(x, y, w, h, cmd.Color);

        canvas.BeginPath();
        canvas.Rect(x, y, w, h);
        canvas.SetStrokeColor(Color32.FromArgb((byte)cmd.SizePx, (byte)cmd.Start, (byte)cmd.End, (byte)cmd.Radius));
        canvas.SetStrokeWidth(cmd.Thickness);
        canvas.Stroke();
    }

    private void AddPoints(Kind kind, ReadOnlySpan<Float3> points, Color32 color, float thickness, bool closed, bool filled)
    {
        if (points.Length < 2) return;
        int start = _points.Count;
        foreach (Float3 p in points) _points.Add(p);
        _commands.Add(new Command
        {
            Kind = kind, Color = color, Thickness = thickness, Closed = closed, Filled = filled,
            PointStart = start, PointCount = points.Length,
        });
    }

    private float Thick(float thickness) => thickness > 0f ? thickness : DefaultThickness;
}
