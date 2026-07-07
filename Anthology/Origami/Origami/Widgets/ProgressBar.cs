// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Preset thickness for an Origami progress bar.</summary>
public enum ProgressSize
{
    XS,
    SM,
    MD,
    LG,
    XL,
}

/// <summary>
/// Fluent builder for an Origami progress bar. Determinate (value 0..1) or
/// indeterminate (sliding band). Variant colour, optional label/percentage,
/// optional animated diagonal stripes, optional leading-edge glow.
///
/// Construct via <see cref="Origami.ProgressBar"/>; chain modifiers; call
/// <see cref="Show"/> to render.
/// </summary>
public sealed class ProgressBarBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private float _value;
    private bool _indeterminate;
    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private ProgressSize _size = ProgressSize.MD;
    private float? _trackThicknessOverride;

    private string? _label;
    private float? _labelWidth;
    private bool _showPercent;
    private string? _percentFormatOverride;
    private string? _customRightText;

    private bool _glow = false;
    private bool _square;

    private bool _thin;
    private bool _striped;
    private bool _ring;
    private float? _width;

    private Color? _trackColorOverride;
    private Color? _fillColorOverride;

    internal ProgressBarBuilder(Paper paper, string id, float value, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _theme = theme;
        _value = Math.Clamp(value, 0f, 1f);
    }

    // ── Mode ─────────────────────────────────────────────────────

    /// <summary>Switch to indeterminate mode: a sliding band loops across the track.</summary>
    public ProgressBarBuilder Indeterminate(bool indeterminate = true) { _indeterminate = indeterminate; return this; }

    /// <summary>Update the progress value at render time. Clamped to [0,1].</summary>
    public ProgressBarBuilder Value(float value) { _value = Math.Clamp(value, 0f, 1f); return this; }

    /// <summary>Thin linear track (4px) — prototype <c>.w2prog.thin</c>.</summary>
    public ProgressBarBuilder Thin(bool thin = true) { _thin = thin; return this; }

    /// <summary>Animated diagonal barber-pole stripes over the fill — prototype <c>.w2prog-strip</c>.</summary>
    public ProgressBarBuilder Striped(bool striped = true) { _striped = striped; return this; }

    /// <summary>Circular ring gauge sweeping clockwise from 12 o'clock — prototype <c>W_Progress</c> SVG ring.</summary>
    public ProgressBarBuilder Ring(bool ring = true) { _ring = ring; return this; }

    /// <summary>Alias for <see cref="Ring"/>.</summary>
    public ProgressBarBuilder Circular(bool circular = true) => Ring(circular);

    /// <summary>Fixed track width in pixels (linear mode). Default stretches to fill the row.</summary>
    public ProgressBarBuilder Width(float px) { _width = MathF.Max(8f, px); return this; }

    // ── Variant ──────────────────────────────────────────────────

    public ProgressBarBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public ProgressBarBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ProgressBarBuilder Success() => Variant(OrigamiVariant.Success);
    public ProgressBarBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ProgressBarBuilder Danger() => Variant(OrigamiVariant.Danger);
    public ProgressBarBuilder Info() => Variant(OrigamiVariant.Info);
    public ProgressBarBuilder Subtle() => Variant(OrigamiVariant.Subtle);

    // ── Size ─────────────────────────────────────────────────────

    public ProgressBarBuilder Size(ProgressSize s) { _size = s; return this; }
    public ProgressBarBuilder XS() => Size(ProgressSize.XS);
    public ProgressBarBuilder SM() => Size(ProgressSize.SM);
    public ProgressBarBuilder MD() => Size(ProgressSize.MD);
    public ProgressBarBuilder LG() => Size(ProgressSize.LG);
    public ProgressBarBuilder XL() => Size(ProgressSize.XL);

    /// <summary>Override the track thickness in pixels. Bypasses the size preset.</summary>
    public ProgressBarBuilder Thickness(float px) { _trackThicknessOverride = MathF.Max(2f, px); return this; }

    // ── Decorations ──────────────────────────────────────────────

    public ProgressBarBuilder Label(string text, float? labelWidth = null)
    {
        _label = text;
        _labelWidth = labelWidth;
        return this;
    }

    /// <summary>Show a "42%" readout on the trailing side of the bar.</summary>
    public ProgressBarBuilder ShowPercent(string? format = null)
    {
        _showPercent = true;
        _percentFormatOverride = format;
        return this;
    }

    /// <summary>Show arbitrary text on the trailing side (overrides ShowPercent).</summary>
    public ProgressBarBuilder TrailingText(string text) { _customRightText = text; return this; }

    /// <summary>Soft glow on the fill's leading edge. Disabled by default.</summary>
    public ProgressBarBuilder Glow(bool glow = true) { _glow = glow; return this; }

    /// <summary>Square corners (default is fully rounded pill ends).</summary>
    public ProgressBarBuilder Square(bool square = true) { _square = square; return this; }

    public ProgressBarBuilder TrackColor(Color color) { _trackColorOverride = color; return this; }
    public ProgressBarBuilder FillColor(Color color) { _fillColorOverride = color; return this; }

    // ── Terminator ───────────────────────────────────────────────

    public void Show()
    {
        var font = _theme.Font;
        var ink = _theme.Ink;
        var ramp = _variant == OrigamiVariant.Subtle ? ink : _theme.Get(_variant);

        // Nebula gradient: acc (#A855F7 / C500) -> acc-bright (#BD6BFF / C600).
        Color fillStart = _fillColorOverride ?? ramp.C500;
        Color fillEnd = _fillColorOverride ?? ramp.C600;
        Color accent = fillStart;

        bool hasLabel = !string.IsNullOrEmpty(_label) && font != null;
        bool hasRight = (_customRightText != null || _showPercent) && font != null;

        string rightText = _customRightText
            ?? (_showPercent ? FormatPercent(_value, _percentFormatOverride) : "");

        float labelW = _labelWidth ?? _theme.Metrics.IconWidth * 6f;   // ~96 px default
        float rightW = hasRight ? 44f : 0f;
        float labelFontSize = _theme.Metrics.FontSize;

        if (_ring)
        {
            const float ringRadius = 19f;
            const float ringStroke = 4f;
            const float ringBox = 46f;
            float ringRowH = MathF.Max(ringBox, _theme.Metrics.HeaderHeight);

            var ringSnap = new ProgressSnapshot
            {
                Value = _value,
                Ring = true,
                RingRadius = ringRadius,
                RingStroke = ringStroke,
                // Track ring rgba(255,255,255,0.1).
                TrackColor = _trackColorOverride ?? Color.FromArgb(26, 255, 255, 255),
                Accent = accent,
                FillStart = fillStart,
                FillEnd = fillEnd,
                Time = (float)_paper.Time,
            };

            using (_paper.Row(_id).Height(ringRowH).RowBetween(8).Enter())
            {
                if (hasLabel)
                {
                    _paper.Box($"{_id}_lbl")
                        .Width(labelW).Height(ringRowH).ChildLeft(0)
                        .Alignment(PaperUI.TextAlignment.MiddleLeft).IsNotInteractable()
                        .Text(_label!, font!).TextColor(ink.C500).FontSize(labelFontSize);
                }

                using (_paper.Box($"{_id}_ring").Width(ringBox).Height(ringBox).IsNotInteractable().Enter())
                {
                    _paper.Draw((canvas, rect) => Paint(canvas, rect, in ringSnap));
                }

                if (hasRight)
                {
                    _paper.Box($"{_id}_pct")
                        .Width(rightW).Height(ringRowH)
                        .Alignment(PaperUI.TextAlignment.MiddleRight).IsNotInteractable()
                        .Text(rightText, font!).TextColor(ink.C500).FontSize(labelFontSize);
                }
            }
            return;
        }

        float trackH = ResolveTrackThickness();
        float rowH = MathF.Max(trackH, _theme.Metrics.HeaderHeight);

        // Linear track rgba(255,255,255,0.08).
        Color trackColor = _trackColorOverride ?? Color.FromArgb(20, 255, 255, 255);

        var snap = new ProgressSnapshot
        {
            Value = _value,
            Indeterminate = _indeterminate,
            Striped = _striped,
            TrackH = trackH,
            TrackColor = trackColor,
            FillStart = fillStart,
            FillEnd = fillEnd,
            GlowColor = ramp.C600,
            Glow = _glow,
            Square = _square,
            Time = (float)_paper.Time,
        };

        using (_paper.Row(_id).Height(rowH).RowBetween(8).Enter())
        {
            if (hasLabel)
            {
                _paper.Box($"{_id}_lbl")
                    .Width(labelW).Height(rowH).ChildLeft(0)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft).IsNotInteractable()
                    .Text(_label!, font!).TextColor(ink.C500).FontSize(labelFontSize);
            }

            var trackBox = _paper.Box($"{_id}_track")
                .Width(_width.HasValue ? UnitValue.Pixels(_width.Value) : UnitValue.Stretch())
                .Height(rowH).IsNotInteractable();
            using (trackBox.Enter())
            {
                _paper.Draw((canvas, rect) => Paint(canvas, rect, in snap));
            }

            if (hasRight)
            {
                _paper.Box($"{_id}_pct")
                    .Width(rightW).Height(rowH)
                    .Alignment(PaperUI.TextAlignment.MiddleRight).IsNotInteractable()
                    .Text(rightText, font!).TextColor(ink.C500).FontSize(labelFontSize);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private float ResolveTrackThickness()
    {
        if (_trackThicknessOverride.HasValue) return _trackThicknessOverride.Value;
        if (_thin) return 4f;
        return _size switch
        {
            ProgressSize.XS => 3f,
            ProgressSize.SM => 5f,
            ProgressSize.LG => 10f,
            ProgressSize.XL => 14f,
            _ => 7f,
        };
    }

    private static string FormatPercent(float v, string? format)
    {
        if (format != null) return string.Format(format, v);
        return $"{(int)MathF.Round(v * 100f)}%";
    }

    // ── Paint snapshot ───────────────────────────────────────────

    private struct ProgressSnapshot
    {
        public float Value;
        public bool Indeterminate;
        public bool Striped;
        public float TrackH;
        public Color TrackColor;
        public Color FillStart;
        public Color FillEnd;
        public Color GlowColor;
        public bool Glow;
        public bool Square;
        public float Time;

        public bool Ring;
        public float RingRadius;
        public float RingStroke;
        public Color Accent;
    }

    private static void Paint(Canvas canvas, Rect rect, in ProgressSnapshot s)
    {
        if (s.Ring)
        {
            PaintRing(canvas, rect, in s);
            return;
        }

        float rx = (float)rect.Min.X;
        float ry = (float)rect.Min.Y;
        float rw = (float)rect.Size.X;
        float rh = (float)rect.Size.Y;

        // Center the track vertically inside its row.
        float trackY = ry + (rh - s.TrackH) * 0.5f;
        float trackR = s.Square ? 0f : s.TrackH * 0.5f;

        // ── Track background ────────────────────────────────────
        canvas.RoundedRect(rx, trackY, rw, s.TrackH, trackR, trackR, trackR, trackR);
        canvas.SetFillColor(s.TrackColor);
        canvas.Fill();

        // ── Fill (determinate or indeterminate band) ────────────
        if (s.Indeterminate)
            PaintIndeterminateFill(canvas, rx, trackY, rw, s.TrackH, trackR, in s);
        else if (s.Value > 0f)
            PaintDeterminateFill(canvas, rx, trackY, rw, s.TrackH, trackR, in s);
    }

    private static void PaintDeterminateFill(Canvas canvas, float rx, float trackY, float rw, float trackH, float trackR, in ProgressSnapshot s)
    {
        float fillW = MathF.Max(trackR * 2f, rw * s.Value);
        if (fillW < 1f) return;

        // Fill body: 90deg (left->right) gradient acc -> acc-bright, rounded so it
        // doesn't poke past the rounded track.
        canvas.SaveState();
        canvas.RoundedRect(rx, trackY, fillW, trackH, trackR, trackR, trackR, trackR);
        canvas.SetLinearBrush(rx, trackY, rx + fillW, trackY, ToC32(s.FillStart), ToC32(s.FillEnd));
        canvas.Fill();
        canvas.RestoreState();

        // Animated diagonal barber-pole overlay, drawn procedurally (no textures).
        if (s.Striped)
            PaintStripes(canvas, rx, trackY, fillW, trackH, s.Time);

        // Soft glow on the leading edge (uses Quill's box brush feathering).
        if (s.Glow)
            PaintLeadingGlow(canvas, rx + fillW, trackY, trackH, s.GlowColor);
    }

    /// <summary>
    /// 45deg translucent-white bands scrolling left->right on a 0.7s loop (prototype
    /// <c>.w2prog-strip</c>). Drawn procedurally as slanted parallelograms clipped to the fill rect.
    /// </summary>
    private static void PaintStripes(Canvas canvas, float rx, float trackY, float fillW, float trackH, float time)
    {
        const float period = 14f;   // band size
        const float bandW = 7f;     // white portion of each 14px band
        float phase = ((time / 0.7f) % 1f) * period;    // scrolls 0..14 over 0.7s
        var white = Color32.FromArgb(51, 255, 255, 255); // rgba(255,255,255,0.2)

        canvas.SaveState();
        canvas.IntersectScissor(rx, trackY, fillW, trackH);

        float startX = rx - trackH - period;
        int count = (int)MathF.Ceiling(fillW / period) + 3;
        for (int i = 0; i <= count; i++)
        {
            float bx = startX + i * period + phase;
            // 45deg slant: as y descends by trackH, x shifts left by trackH.
            canvas.BeginPath();
            canvas.MoveTo(bx, trackY);
            canvas.LineTo(bx + bandW, trackY);
            canvas.LineTo(bx + bandW - trackH, trackY + trackH);
            canvas.LineTo(bx - trackH, trackY + trackH);
            canvas.ClosePath();
            canvas.SetFillColor(white);
            canvas.Fill();
        }
        canvas.RestoreState();
    }

    /// <summary>
    /// Circular ring: faint full track ring + accent arc swept clockwise from 12 o'clock
    /// for <c>value</c> fraction (prototype <c>W_Progress</c> SVG ring).
    /// </summary>
    private static void PaintRing(Canvas canvas, Rect rect, in ProgressSnapshot s)
    {
        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);
        float maxR = (float)(Math.Min(rect.Size.X, rect.Size.Y) * 0.5) - s.RingStroke * 0.5f;
        float r = MathF.Min(s.RingRadius, maxR);
        if (r <= 0f) return;

        canvas.SaveState();

        // Track ring — full circle.
        canvas.BeginPath();
        canvas.Arc(cx, cy, r, 0f, Maths.PI * 2f);
        canvas.SetStrokeColor(ToC32(s.TrackColor));
        canvas.SetStrokeWidth(s.RingStroke);
        canvas.Stroke();

        // Accent sweep — clockwise from top (-90deg) for value fraction.
        float v = Math.Clamp(s.Value, 0f, 1f);
        if (v > 0f)
        {
            float start = -Maths.PI * 0.5f;
            float end = start + v * Maths.PI * 2f;
            canvas.BeginPath();
            canvas.Arc(cx, cy, r, start, end);
            canvas.SetStrokeColor(ToC32(s.Accent));
            canvas.SetStrokeWidth(s.RingStroke);
            canvas.SetStrokeCap(EndCapStyle.Round);
            canvas.Stroke();
        }

        canvas.RestoreState();
    }

    private static void PaintIndeterminateFill(Canvas canvas, float rx, float trackY, float rw, float trackH, float trackR, in ProgressSnapshot s)
    {
        // Band that slides from off-left to off-right and back, using an ease curve
        // so the motion has weight at the edges rather than a robotic linear pan.
        float bandW = MathF.Max(rw * 0.35f, trackH * 4f);
        float travel = rw + bandW;
        float t = (s.Time * 0.9f) % 2f;
        float u = t < 1f ? Ease(t) : 1f - Ease(t - 1f);
        float bandCx = (rx - bandW * 0.5f) + travel * u;

        // SetBoxBrush gives a soft-edged horizontal band in a single fill. The
        // rounded-rect path clips it to the track shape.
        var bright = ToC32(WithAlpha(s.FillEnd, 220));
        var fade = ToC32(WithAlpha(s.FillEnd, 0));

        canvas.SaveState();
        canvas.RoundedRect(rx, trackY, rw, trackH, trackR, trackR, trackR, trackR);
        canvas.SetBoxBrush(
            bandCx, trackY + trackH * 0.5f,
            bandW * 0.30f, trackH * 4f,
            0f, bandW * 0.45f,
            bright, fade);
        canvas.Fill();
        canvas.RestoreState();
    }


    private static void PaintLeadingGlow(Canvas canvas, float leadX, float trackY, float trackH, Color color)
    {
        // Box brush with feather creates a soft halo at the leading edge.
        float glowR = trackH * 1.5f;
        float feather = trackH * 1.5f;
        var inner = ToC32(WithAlpha(color, 200));
        var outer = ToC32(WithAlpha(color, 0));

        canvas.SaveState();
        canvas.RoundedRect(leadX - glowR, trackY - trackH * 0.5f, glowR * 2f, trackH * 2f, glowR, glowR, glowR, glowR);
        canvas.SetBoxBrush(leadX, trackY + trackH * 0.5f, trackH * 0.6f, trackH * 0.6f,
            trackH * 0.5f, feather, inner, outer);
        canvas.Fill();
        canvas.RestoreState();
    }

    // ── Tiny colour helpers ──────────────────────────────────────

    private static Color32 ToC32(Color c) => new Color32(c.R, c.G, c.B, c.A);

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static float Ease(float t)
    {
        // Cubic ease in-out, smooth ping-pong feel without snap at edges.
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}
