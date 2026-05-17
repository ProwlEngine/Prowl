// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
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

        using (_paper.Row(_id).Height(rowH).RowBetween(8).Enter())
        {
            using (_paper.Box($"{_id}_glyph")
                .Width(diameter).Height(rowH)
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

    /// <summary>Three dots bouncing on a sine wave, staggered by phase.</summary>
    private static void PaintDots(Canvas canvas, float cx, float cy, float r, Color color, float time)
    {
        float dotR = r * 0.30f;
        float gap = r * 0.55f;
        float xs = cx - gap;

        for (int i = 0; i < 3; i++)
        {
            float phase = i * (Maths.PI / 3f);
            float bounce = MathF.Sin(time * Maths.PI * 2.4f - phase);
            // ramp 0..1 with mid above center, ease out
            float t = MathF.Max(0f, bounce);
            float dy = -t * r * 0.5f;
            byte alpha = (byte)Math.Clamp((int)(160 + 95 * t), 0, 255);

            canvas.BeginPath();
            canvas.Circle(xs + i * gap, cy + dy, dotR);
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
