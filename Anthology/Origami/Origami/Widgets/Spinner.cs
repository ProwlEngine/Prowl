// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Animation style for an Origami spinner.</summary>
public enum SpinnerStyle
{
    /// <summary>3/4 arc rotating once per second, with a soft gradient trail.</summary>
    Arc,

    /// <summary>Three circles bouncing in a sequenced wave.</summary>
    Dots,

    /// <summary>Single circle scaling + alpha pulse.</summary>
    Pulse,

    /// <summary>Two counter-rotating arcs, one inner and one outer.</summary>
    DualArc,

    /// <summary>Four vertical bars pulsing in a sequenced wave (equaliser style).</summary>
    Bars,

    /// <summary>Faint full ring with an accent arc at the top, rotating (prototype .w2spin).</summary>
    Ring,
}

/// <summary>Preset diameter for an Origami spinner.</summary>
public enum SpinnerSize
{
    XS,
    SM,
    MD,
    LG,
    XL,
}

/// <summary>
/// Fluent builder for an Origami spinner. Canvas-painted, time-driven animation,
/// variant colouring, optional label.
///
/// Construct via <see cref="Origami.Spinner"/>; chain modifiers; call
/// <see cref="Show"/> to render.
/// </summary>
public sealed class SpinnerBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private SpinnerStyle _style = SpinnerStyle.Arc;
    private SpinnerSize _size = SpinnerSize.MD;
    private float? _diameterOverride;
    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private Color? _colorOverride;
    private string? _label;
    private float _speed = 1f;

    internal SpinnerBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _theme = theme;
    }

    // ── Style ────────────────────────────────────────────────────

    public SpinnerBuilder Style(SpinnerStyle style) { _style = style; return this; }
    public SpinnerBuilder Arc() => Style(SpinnerStyle.Arc);
    public SpinnerBuilder Dots() => Style(SpinnerStyle.Dots);
    public SpinnerBuilder Pulse() => Style(SpinnerStyle.Pulse);
    public SpinnerBuilder DualArc() => Style(SpinnerStyle.DualArc);
    public SpinnerBuilder Bars() => Style(SpinnerStyle.Bars);
    public SpinnerBuilder Ring() => Style(SpinnerStyle.Ring);

    // ── Variant / colour ─────────────────────────────────────────

    public SpinnerBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public SpinnerBuilder Primary() => Variant(OrigamiVariant.Primary);
    public SpinnerBuilder Success() => Variant(OrigamiVariant.Success);
    public SpinnerBuilder Warning() => Variant(OrigamiVariant.Warning);
    public SpinnerBuilder Danger() => Variant(OrigamiVariant.Danger);
    public SpinnerBuilder Info() => Variant(OrigamiVariant.Info);
    public SpinnerBuilder Subtle() => Variant(OrigamiVariant.Subtle);

    public SpinnerBuilder Tint(Color color) { _colorOverride = color; return this; }

    // ── Size ─────────────────────────────────────────────────────

    public SpinnerBuilder Size(SpinnerSize s) { _size = s; return this; }
    public SpinnerBuilder XS() => Size(SpinnerSize.XS);
    public SpinnerBuilder SM() => Size(SpinnerSize.SM);
    public SpinnerBuilder MD() => Size(SpinnerSize.MD);
    public SpinnerBuilder LG() => Size(SpinnerSize.LG);
    public SpinnerBuilder XL() => Size(SpinnerSize.XL);

    public SpinnerBuilder Diameter(float px) { _diameterOverride = MathF.Max(4f, px); return this; }

    /// <summary>Multiplier on the default animation speed. Default 1.0.</summary>
    public SpinnerBuilder Speed(float speed) { _speed = MathF.Max(0.05f, speed); return this; }

    /// <summary>Render the given text to the right of the spinner.</summary>
    public SpinnerBuilder Label(string text) { _label = text; return this; }

    // ── Terminator ───────────────────────────────────────────────

    public void Show()
    {
        var font = _theme.Font;
        float diameter = ResolveDiameter();
        float rowH = MathF.Max(diameter, _theme.Metrics.HeaderHeight);
        Color color = _colorOverride ?? ResolveVariantColor();
        bool hasLabel = !string.IsNullOrEmpty(_label) && font != null;

        var snap = new SpinnerSnapshot
        {
            Style = _style,
            Diameter = diameter,
            Color = color,
            Time = (float)_paper.Time * _speed,
        };

        // Dots/Bars are wider than tall — reserve the real footprint so callers (e.g. a chat bubble)
        // size around it instead of clipping.
        float glyphW = _style switch
        {
            SpinnerStyle.Dots => diameter * 1.6f,
            SpinnerStyle.Bars => diameter * 1.4f,
            _ => diameter,
        };

        using (_paper.Row(_id).Width(UnitValue.Auto).Height(rowH).RowBetween(8).Enter())
        {
            using (_paper.Box($"{_id}_glyph")
                .Width(glyphW).Height(rowH)
                .IsNotInteractable().Enter())
            {
                _paper.Draw((canvas, rect) => Paint(canvas, rect, in snap));
            }

            if (hasLabel)
            {
                _paper.Box($"{_id}_lbl")
                    .Height(rowH)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft).IsNotInteractable()
                    .Text(_label!, font!).TextColor(_theme.Ink.C500).FontSize(_theme.Metrics.FontSize);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private float ResolveDiameter()
    {
        if (_diameterOverride.HasValue) return _diameterOverride.Value;
        return _size switch
        {
            SpinnerSize.XS => 10f,
            SpinnerSize.SM => 14f,
            SpinnerSize.LG => 28f,
            SpinnerSize.XL => 40f,
            _ => 20f,
        };
    }

    private Color ResolveVariantColor()
    {
        if (_variant == OrigamiVariant.Subtle) return _theme.Ink.C300;
        if (_variant == OrigamiVariant.Default) return _theme.Ink.C500;
        return _theme.Get(_variant).C500;
    }

    // ── Paint snapshot ───────────────────────────────────────────

    private struct SpinnerSnapshot
    {
        public SpinnerStyle Style;
        public float Diameter;
        public Color Color;
        public float Time;
    }

    private static void Paint(Canvas canvas, Rect rect, in SpinnerSnapshot s)
    {
        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);
        float r = s.Diameter * 0.5f;

        switch (s.Style)
        {
            case SpinnerStyle.Arc:     PaintArc(canvas, cx, cy, r, s.Color, s.Time); break;
            case SpinnerStyle.Dots:    PaintDots(canvas, cx, cy, r, s.Color, s.Time); break;
            case SpinnerStyle.Pulse:   PaintPulse(canvas, cx, cy, r, s.Color, s.Time); break;
            case SpinnerStyle.DualArc: PaintDualArc(canvas, cx, cy, r, s.Color, s.Time); break;
            case SpinnerStyle.Bars:    PaintBars(canvas, cx, cy, r, s.Color, s.Time); break;
            case SpinnerStyle.Ring:    PaintRing(canvas, cx, cy, r, s.Color, s.Time); break;
        }
    }

    /// <summary>Faint full ring + a rotating accent arc at the top (prototype .w2spin, spinSlow 0.8s).</summary>
    private static void PaintRing(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        float thick = MathF.Max(1.5f, r * 0.23f);   // 2.5px border @ r=11
        float rr = r - thick * 0.5f;
        canvas.SaveState();
        canvas.BeginPath();
        canvas.Arc(cx, cy, rr, 0f, Maths.PI * 2f);
        canvas.SetStrokeColor(Color32.FromArgb(31, 255, 255, 255)); // rgba(255,255,255,0.12)
        canvas.SetStrokeWidth(thick);
        canvas.Stroke();

        float rot = time * (Maths.PI * 2f / 0.8f);
        float top = -Maths.PI * 0.5f + rot;
        canvas.BeginPath();
        canvas.Arc(cx, cy, rr, top - 0.85f, top + 0.85f);
        canvas.SetStrokeColor(Color32.FromArgb(255, color.R, color.G, color.B));
        canvas.SetStrokeWidth(thick);
        canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.Stroke();
        canvas.RestoreState();
    }

    /// <summary>Four bars pulsing scaleY+opacity (prototype .w2bars / w2barpulse 1s, delays .12).</summary>
    private static void PaintBars(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        const int n = 4;
        float bw = r * 0.36f;          // 4px @ r=11
        float gap = r * 0.27f;         // 1.5px margins each side
        float step = bw + gap;
        float x0 = cx - (n * step - gap) * 0.5f;
        float maxH = r * 1.64f;        // 18px @ r=11
        const float period = 1f;

        for (int i = 0; i < n; i++)
        {
            float t = ((time - 0.12f * i) / period) % 1f;
            if (t < 0f) t += 1f;
            // w2barpulse: 0/100% -> scaleY .4 op .5; 50% -> scaleY 1 op 1
            float p = 0.5f - 0.5f * MathF.Cos(t * Maths.PI * 2f);
            float h = maxH * (0.4f + 0.6f * p);
            byte alpha = (byte)Math.Clamp((int)((0.5f + 0.5f * p) * 255f), 0, 255);
            float bx = x0 + i * step;
            canvas.RoundedRectFilled(bx, cy - h * 0.5f, bw, h, bw * 0.5f, Color.FromArgb(alpha, color.R, color.G, color.B));
        }
    }

    /// <summary>
    /// Rotating arc with a faded "tail" effect achieved by drawing multiple short
    /// arc segments at decreasing alpha. The head is opaque, the tail trails off.
    /// </summary>
    private static void PaintArc(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        canvas.SaveState();
        float rot = time * 360f;                    // one revolution per second (degrees)
        canvas.TransformBy(Transform2D.CreateTranslation(cx, cy));
        canvas.TransformBy(Transform2D.CreateRotation(rot));

        const int segments = 8;
        const float arcLen = Maths.PI * 1.4f;       // ~252° total spread
        float segLen = arcLen / segments;
        float thickness = MathF.Max(1.5f, r * 0.20f);

        for (int i = 0; i < segments; i++)
        {
            // Tail starts faint, head is full alpha.
            byte alpha = (byte)Math.Clamp((int)(255 * ((i + 1) / (float)segments)), 32, 255);
            var col = Color32.FromArgb(alpha, color.R, color.G, color.B);

            float a0 = i * segLen;
            float a1 = a0 + segLen + 0.02f;          // tiny overlap to avoid gaps
            canvas.BeginPath();
            canvas.Arc(0, 0, r, a0, a1);
            canvas.SetStrokeColor(col);
            canvas.SetStrokeWidth(thickness);
            canvas.Stroke();
        }
        canvas.RestoreState();
    }

    /// <summary>Three dots pulsing scale+opacity (prototype .w2dots / w2bounce 1.2s, delays .15/.3).</summary>
    private static void PaintDots(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        float dotR = r * 0.32f;        // 7px dia @ r=11
        float spacing = r * 0.75f;
        float baseX = cx - spacing;
        const float period = 1.2f;
        System.ReadOnlySpan<float> delays = stackalloc float[] { 0f, 0.15f, 0.3f };

        for (int i = 0; i < 3; i++)
        {
            float t = ((time - delays[i]) / period) % 1f;
            if (t < 0f) t += 1f;
            // w2bounce: 0/80/100% -> (opacity .3, scale .7); 40% -> (opacity 1, scale 1)
            float p = t < 0.4f ? t / 0.4f : (t < 0.8f ? 1f - (t - 0.4f) / 0.4f : 0f);
            p = p * p * (3f - 2f * p); // ease-in-out
            float scale = 0.7f + 0.3f * p;
            byte alpha = (byte)Math.Clamp((int)((0.3f + 0.7f * p) * 255f), 0, 255);

            canvas.BeginPath();
            canvas.Circle(baseX + i * spacing, cy, dotR * scale);
            canvas.SetFillColor(Color.FromArgb(alpha, color.R, color.G, color.B));
            canvas.Fill();
        }
    }

    /// <summary>Single dot scaling up and fading out in a loop, like a radar ping.</summary>
    private static void PaintPulse(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        // Two pings 180° out of phase so the spinner never has a "dead" frame.
        for (int i = 0; i < 2; i++)
        {
            float t = ((time * 0.9f) + i * 0.5f) % 1f;
            float radius = r * (0.35f + 0.65f * t);
            byte alpha = (byte)Math.Clamp((int)(220 * (1f - t)), 0, 255);

            canvas.BeginPath();
            canvas.Circle(cx, cy, radius);
            canvas.SetFillColor(Color.FromArgb(alpha, color.R, color.G, color.B));
            canvas.Fill();
        }
    }

    /// <summary>Outer arc rotates clockwise; inner arc counter-rotates.</summary>
    private static void PaintDualArc(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        float t1 = time * 360f;                     // degrees, one rev/s
        float t2 = -time * 360f * 1.4f;
        float thickness = MathF.Max(1.5f, r * 0.16f);

        // Outer arc — ~210°
        canvas.SaveState();
        canvas.TransformBy(Transform2D.CreateTranslation(cx, cy));
        canvas.TransformBy(Transform2D.CreateRotation(t1));
        canvas.BeginPath();
        canvas.Arc(0, 0, r, 0, Maths.PI * 1.15f);
        canvas.SetStrokeColor(Color32.FromArgb(255, color.R, color.G, color.B));
        canvas.SetStrokeWidth(thickness);
        canvas.Stroke();
        canvas.RestoreState();

        // Inner arc — ~150°, dimmer
        canvas.SaveState();
        canvas.TransformBy(Transform2D.CreateTranslation(cx, cy));
        canvas.TransformBy(Transform2D.CreateRotation(t2));
        canvas.BeginPath();
        canvas.Arc(0, 0, r * 0.55f, 0, Maths.PI * 0.85f);
        canvas.SetStrokeColor(Color32.FromArgb(170, color.R, color.G, color.B));
        canvas.SetStrokeWidth(MathF.Max(1.5f, r * 0.12f));
        canvas.Stroke();
        canvas.RestoreState();
    }
}
