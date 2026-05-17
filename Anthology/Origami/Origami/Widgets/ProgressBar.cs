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

        float trackH = ResolveTrackThickness();
        float rowH = MathF.Max(trackH, _theme.Metrics.HeaderHeight);

        Color trackColor = _trackColorOverride ?? ink.C200;
        Color fillStart = _fillColorOverride ?? ramp.C400;
        Color fillEnd = _fillColorOverride ?? ramp.C600;
        Color glowColor = ramp.C600;

        bool hasLabel = !string.IsNullOrEmpty(_label) && font != null;
        bool hasRight = (_customRightText != null || _showPercent) && font != null;

        string rightText = _customRightText
            ?? (_showPercent ? FormatPercent(_value, _percentFormatOverride) : "");

        float labelW = _labelWidth ?? _theme.Metrics.IconWidth * 6f;   // ~96 px default
        float rightW = hasRight ? 44f : 0f;
        float labelFontSize = _theme.Metrics.FontSize;

        var snap = new ProgressSnapshot
        {
            Value = _value,
            Indeterminate = _indeterminate,
            TrackH = trackH,
            TrackColor = trackColor,
            FillStart = fillStart,
            FillEnd = fillEnd,
            GlowColor = glowColor,
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

            using (_paper.Box($"{_id}_track").Width(UnitValue.Stretch()).Height(rowH).IsNotInteractable().Enter())
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
        public float TrackH;
        public Color TrackColor;
        public Color FillStart;
        public Color FillEnd;
        public Color GlowColor;
        public bool Glow;
        public bool Square;
        public float Time;
    }

    private static void Paint(Canvas canvas, Rect rect, in ProgressSnapshot s)
    {
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

        // Fill body with a horizontal gradient so the bar reads with subtle volume.
        canvas.SaveState();
        canvas.RoundedRect(rx, trackY, fillW, trackH, trackR, trackR, trackR, trackR);
        canvas.SetLinearBrush(rx, trackY, rx, trackY + trackH,
            ToC32(Lighten(s.FillStart, 0.06f)),
            ToC32(s.FillEnd));
        canvas.Fill();
        canvas.RestoreState();

        // Soft glow on the leading edge (uses Quill's box brush feathering).
        if (s.Glow)
            PaintLeadingGlow(canvas, rx + fillW, trackY, trackH, s.GlowColor);
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

    private static Color Lighten(Color c, float amount)
    {
        int r = Math.Clamp(c.R + (int)(255 * amount), 0, 255);
        int g = Math.Clamp(c.G + (int)(255 * amount), 0, 255);
        int b = Math.Clamp(c.B + (int)(255 * amount), 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static float Ease(float t)
    {
        // Cubic ease in-out, smooth ping-pong feel without snap at edges.
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}
