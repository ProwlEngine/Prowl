// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Visual shape for an Origami skeleton placeholder.</summary>
public enum SkeletonShape
{
    /// <summary>Slightly rounded rectangle (text line, card surface).</summary>
    Rect,

    /// <summary>Fully rounded pill (button, chip).</summary>
    Pill,

    /// <summary>Circle (avatar, status dot).</summary>
    Circle,
}

/// <summary>
/// Fluent builder for an Origami skeleton placeholder. Renders a neutral base
/// shape with a soft shimmer band sweeping across it. Use to fill space while
/// real content loads.
///
/// Construct via <see cref="Origami.Skeleton"/>; chain shape + size; call
/// <see cref="Show"/> to render.
/// </summary>
public sealed class SkeletonBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private SkeletonShape _shape = SkeletonShape.Rect;
    private float _width = 120f;
    private float _height = 14f;
    private float? _radiusOverride;
    private bool _shimmer = true;
    private float _shimmerSpeed = 1f;
    private Color? _baseColorOverride;
    private Color? _shimmerColorOverride;

    internal SkeletonBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _theme = theme;
    }

    // ── Shape ────────────────────────────────────────────────────

    public SkeletonBuilder Shape(SkeletonShape shape) { _shape = shape; return this; }
    public SkeletonBuilder Rect() => Shape(SkeletonShape.Rect);
    public SkeletonBuilder Pill() => Shape(SkeletonShape.Pill);
    public SkeletonBuilder Circle() => Shape(SkeletonShape.Circle);

    // ── Size ─────────────────────────────────────────────────────

    public SkeletonBuilder Size(float width, float height)
    {
        _width = MathF.Max(1f, width);
        _height = MathF.Max(1f, height);
        return this;
    }

    public SkeletonBuilder Width(float width) { _width = MathF.Max(1f, width); return this; }
    public SkeletonBuilder Height(float height) { _height = MathF.Max(1f, height); return this; }

    /// <summary>Convenience for a one-line text placeholder at the theme's font size.</summary>
    public SkeletonBuilder TextLine(float width)
        => Shape(SkeletonShape.Rect).Size(width, MathF.Max(8f, _theme.Metrics.FontSize * 0.9f));

    /// <summary>Convenience for an avatar circle of the given diameter.</summary>
    public SkeletonBuilder Avatar(float diameter)
        => Shape(SkeletonShape.Circle).Size(diameter, diameter);

    /// <summary>Override the corner radius for Rect shape. Ignored for Pill/Circle.</summary>
    public SkeletonBuilder Rounding(float radius) { _radiusOverride = MathF.Max(0f, radius); return this; }

    // ── Shimmer ──────────────────────────────────────────────────

    public SkeletonBuilder Shimmer(bool shimmer = true) { _shimmer = shimmer; return this; }
    public SkeletonBuilder ShimmerSpeed(float speed) { _shimmerSpeed = MathF.Max(0.05f, speed); return this; }

    public SkeletonBuilder BaseColor(Color color) { _baseColorOverride = color; return this; }
    public SkeletonBuilder ShimmerColor(Color color) { _shimmerColorOverride = color; return this; }

    // ── Terminator ───────────────────────────────────────────────

    public void Show()
    {
        // Nebula "w2skel": translucent white base, shimmering to a brighter translucent white.
        Color baseColor = _baseColorOverride ?? Color.FromArgb(13, 255, 255, 255);    // rgba(255,255,255,0.05)
        Color shimmerColor = _shimmerColorOverride ?? Color.FromArgb(28, 255, 255, 255); // rgba(255,255,255,0.11)
        float radius = ResolveRadius();

        var snap = new SkeletonSnapshot
        {
            Shape = _shape,
            W = _width,
            H = _height,
            Radius = radius,
            BaseColor = baseColor,
            ShimmerColor = shimmerColor,
            Shimmer = _shimmer,
            Time = (float)_paper.Time * _shimmerSpeed,
        };

        using (_paper.Box(_id).Width(_width).Height(_height).IsNotInteractable().Enter())
        {
            _paper.Draw((canvas, rect) => Paint(canvas, rect, in snap));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private float ResolveRadius()
    {
        return _shape switch
        {
            SkeletonShape.Pill   => MathF.Min(_width, _height) * 0.5f,
            SkeletonShape.Circle => MathF.Min(_width, _height) * 0.5f,
            _                    => _radiusOverride ?? 6f,
        };
    }

    // ── Paint snapshot ───────────────────────────────────────────

    private struct SkeletonSnapshot
    {
        public SkeletonShape Shape;
        public float W;
        public float H;
        public float Radius;
        public Color BaseColor;
        public Color ShimmerColor;
        public bool Shimmer;
        public float Time;
    }

    private static void Paint(Canvas canvas, Rect rect, in SkeletonSnapshot s)
    {
        float x = (float)rect.Min.X;
        float y = (float)rect.Min.Y;

        // Base shape
        DrawShapePath(canvas, x, y, s);
        canvas.SetFillColor(s.BaseColor);
        canvas.Fill();

        if (!s.Shimmer) return;

        // Shimmer: a soft light band swept left → right over ~1.3s with an ease-in-out feel,
        // clipped to the shape and drawn additively over the base with a feathered box brush.
        float t = (s.Time % 1.3f) / 1.3f;           // 1.3s sweep cycle
        float ease = SmoothStep01(t);
        float bandW = MathF.Max(s.W * 0.6f, s.H * 2.5f);
        float travel = s.W + bandW;
        float bandCx = x - bandW * 0.5f + travel * ease;
        float bandCy = y + s.H * 0.5f;

        var bright = new Color32(s.ShimmerColor.R, s.ShimmerColor.G, s.ShimmerColor.B, s.ShimmerColor.A);
        var fade = new Color32(s.ShimmerColor.R, s.ShimmerColor.G, s.ShimmerColor.B, (byte)0);

        canvas.SaveState();
        DrawShapePath(canvas, x, y, s);
        canvas.SetBoxBrush(
            bandCx, bandCy,
            bandW * 0.35f, s.H * 4f,
            0f, bandW * 0.5f,
            bright, fade);
        canvas.Fill();
        canvas.RestoreState();
    }

    private static void DrawShapePath(Canvas canvas, float x, float y, in SkeletonSnapshot s)
    {
        switch (s.Shape)
        {
            case SkeletonShape.Circle:
                canvas.BeginPath();
                canvas.Circle(x + s.W * 0.5f, y + s.H * 0.5f, MathF.Min(s.W, s.H) * 0.5f);
                break;
            default:
                canvas.RoundedRect(x, y, s.W, s.H, s.Radius, s.Radius, s.Radius, s.Radius);
                break;
        }
    }

    private static float SmoothStep01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
