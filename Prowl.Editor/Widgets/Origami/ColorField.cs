// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using SysColor = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Describes a color palette that the ColorField popover can display.
/// The caller owns the list and persistence - Origami just renders it.
/// </summary>
public sealed class ColorPalette
{
    /// <summary>List of colors in the palette. The caller owns and persists this list.</summary>
    public List<Color> Colors { get; }

    /// <summary>Called when the user clicks the + button. Return the color to add, or null to cancel.</summary>
    public Func<Color?>? OnAdd { get; set; }

    /// <summary>Called after a color is removed (right-click). Index is the removed position.</summary>
    public Action<int>? OnRemoved { get; set; }

    /// <summary>Maximum visible rows in the palette grid (0 = unlimited).</summary>
    public int MaxRows { get; set; } = 3;

    /// <summary>Size of each swatch in pixels.</summary>
    public float SwatchSize { get; set; } = 16f;

    public ColorPalette(List<Color> colors) => Colors = colors ?? throw new ArgumentNullException(nameof(colors));
}

/// <summary>
/// Fluent builder for an Origami color field. Renders a clickable color swatch that
/// opens a full-featured color picker popover with SV square, hue/alpha bars,
/// HSV/RGB/Hex input modes, preview, and optional palette.
/// No dependencies on Prowl.Editor or Prowl.Runtime - fully self-contained.
/// </summary>
public sealed class ColorFieldBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly Color _value;
    private readonly Action<Color> _setter;
    private readonly OrigamiTheme _theme;

    private bool _showAlpha = true;
    private bool _hdr;
    private bool _readOnly;
    private UnitValue _width = UnitValue.Stretch();
    private ColorPalette? _palette;

    internal ColorFieldBuilder(Paper paper, string id, Color value, Action<Color> setter, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _value = value;
        _setter = setter;
        _theme = theme;
    }

    /// <summary>Show/hide the alpha channel bar and slider. Default true.</summary>
    public ColorFieldBuilder Alpha(bool show) { _showAlpha = show; return this; }

    /// <summary>Allow HDR values (channels > 1.0). Default false.</summary>
    public ColorFieldBuilder HDR(bool enable) { _hdr = enable; return this; }

    /// <summary>Display-only, no interaction.</summary>
    public ColorFieldBuilder ReadOnly() { _readOnly = true; return this; }

    /// <summary>Override the swatch width.</summary>
    public ColorFieldBuilder Width(UnitValue w) { _width = w; return this; }

    /// <summary>Override the swatch width.</summary>
    public ColorFieldBuilder Width(float w) { _width = w; return this; }

    /// <summary>
    /// Attach a color palette to the picker popover. The caller owns the list and
    /// persistence. Left-click a swatch to select, right-click to remove, + to add.
    /// Pass null to hide the palette section.
    /// </summary>
    public ColorFieldBuilder Palette(ColorPalette? palette) { _palette = palette; return this; }

    // ── Terminator ───────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        var font = _theme.Font;
        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        float rounding = metrics.Rounding;

        int ri = Clamp255(_value.R), gi = Clamp255(_value.G), bi = Clamp255(_value.B), ai = Clamp255(_value.A);
        string hexText = _showAlpha ? $"#{ri:X2}{gi:X2}{bi:X2}{ai:X2}" : $"#{ri:X2}{gi:X2}{bi:X2}";

        // Swatch trigger - single box, all visuals drawn via canvas
        float fontSize = metrics.FontSize - 1;
        var capturedFont = font;
        var capturedInk = ink;
        var capturedPrimary = _theme.Primary;

        var swatch = _paper.Box(_id)
            .Width(_width).Height(metrics.HeaderHeight);

        if (!_readOnly)
        {
            var value = _value;
            var setter = _setter;
            var id = _id;
            var showAlpha = _showAlpha;
            var hdr = _hdr;
            var palette = _palette;
            swatch.OnClick(e =>
            {
                float anchorX = (float)e.ElementRect.Min.X;
                float anchorY = (float)e.ElementRect.Max.Y + 2;
                var modal = new ColorPickerModal(id, value, setter, showAlpha, hdr, palette, anchorX, anchorY);
                Modal.Push(modal);
            });
        }

        using (swatch.Enter())
        {
            bool isHovered = _paper.IsParentHovered;

            // Draw swatch background, border, and hex text in one canvas pass
            _paper.Draw((canvas, rect) =>
            {
                float x = (float)rect.Min.X, y = (float)rect.Min.Y;
                float w = (float)rect.Size.X, h = (float)rect.Size.Y;

                // Fill with the color
                canvas.RoundedRectFilled(x, y, w, h, rounding, rounding, rounding, rounding,
                    Color32.FromArgb(ai, ri, gi, bi));

                // Border
                var borderCol = isHovered
                    ? Color32.FromArgb(255, capturedPrimary.C400.R, capturedPrimary.C400.G, capturedPrimary.C400.B)
                    : Color32.FromArgb(255, capturedInk.C200.R, capturedInk.C200.G, capturedInk.C200.B);
                canvas.SetStrokeColor(borderCol);
                canvas.SetStrokeWidth(1);
                canvas.BeginPath();
                canvas.RoundedRect(x, y, w, h, rounding, rounding, rounding, rounding);
                canvas.Stroke();

                // Hex text with outline for readability on any background
                if (capturedFont != null)
                {
                    var textSize = canvas.MeasureText(hexText, fontSize, capturedFont);
                    float tx = x + metrics.Padding;
                    float ty = y + (h - (float)textSize.Y) * 0.5f;

                    // Dark outline behind text so it pops on white/bright backgrounds
                    var shadow = Color32.FromArgb(180, 0, 0, 0);
                    for (int ox = 0; ox <= 1; ox++)
                        for (int oy = 0; oy <= 1; oy++)
                            if (ox != 0 || oy != 0)
                                canvas.DrawText(hexText, tx + ox, ty + oy, shadow, fontSize, capturedFont);

                    // White text on top
                    canvas.DrawText(hexText, tx, ty, Color32.FromArgb(255, 255, 255, 255), fontSize, capturedFont);
                }
            });
        }
    }

    // ── Popover ──────────────────────────────────────────────────

    private const float BarWidth = 20f;
    private const float PopWidth = 300f;

    internal void RenderPopover()
    {
        var font = _theme.Font;
        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        float pad = metrics.PaddingLarge;
        float gap = metrics.SpacingMedium;

        using (_paper.Column($"{_id}_cf_pop")
            .Width(PopWidth)
            .Height(UnitValue.Auto)
            .Padding(pad, pad, pad, pad)
            .ColBetween(gap)
            .Enter())
        {
            var popEl = _paper.CurrentParent;

            // Init HSV from current value on first open
            float h = _paper.GetElementStorage(popEl, "h", -1f);
            if (h < 0)
            {
                ColorToHSV(_value, out h, out float si, out float vi);
                _paper.SetElementStorage(popEl, "h", h);
                _paper.SetElementStorage(popEl, "s", si);
                _paper.SetElementStorage(popEl, "v", vi);
                _paper.SetElementStorage(popEl, "a", _value.A);
            }
            float s = _paper.GetElementStorage(popEl, "s", 1f);
            float v = _paper.GetElementStorage(popEl, "v", 1f);
            float a = _paper.GetElementStorage(popEl, "a", 1f);

            // Mode: 0=HSV, 1=RGB, 2=Hex
            int mode = _paper.GetElementStorage(popEl, "mode", 0);

            // Mode tabs
            Origami.ButtonGroup(_paper, $"{_id}_cf_tabs", mode, m => _paper.SetElementStorage(popEl, "mode", m))
                .Item("HSV").Item("RGB").Item("Hex")
                .FullWidth().Show();

            // SV Square + Hue Bar + Alpha Bar
            float barCount = _showAlpha ? 2 : 1;
            float svW = PopWidth - pad * 2 - (BarWidth + gap) * barCount;

            using (_paper.Row($"{_id}_cf_top").Height(svW).RowBetween(gap).Enter())
            {
                DrawSVSquare(popEl, svW, h, s, v, a);
                DrawHueBar(popEl, svW, h, s, v, a);
                if (_showAlpha) DrawAlphaBar(popEl, svW, h, s, v, a);
            }

            // Channel inputs
            var currentColor = HSVToColor(h, s, v, a);

            int ni = Clamp255(currentColor.R), ng = Clamp255(currentColor.G), nb = Clamp255(currentColor.B), na = Clamp255(currentColor.A);
            _paper.Box($"{_id}_cf_new").Height(metrics.RowHeight).Rounded(metrics.SmallRounding)
                .BorderColor(ink.C200).BorderWidth(1)
                .BackgroundColor(SysColor.FromArgb(na, ni, ng, nb));

            if (mode == 0)
                DrawHSVInputs(popEl, h, s, v, a);
            else if (mode == 1)
                DrawRGBInputs(popEl, currentColor);
            else
                DrawHexInput(popEl, currentColor);

            // Palette
            if (_palette != null && _palette.Colors.Count > 0 || _palette?.OnAdd != null)
            {
                _paper.Box($"{_id}_cf_psep").Height(1).BackgroundColor(ink.C200);
                DrawPalette(popEl);
            }

            // Escape is handled by the modal stack
        }
    }

    // ── SV Square ────────────────────────────────────────────────

    private void DrawSVSquare(ElementHandle popEl, float size, float h, float s, float v, float a)
    {
        var svRound = _theme.Metrics.SmallRounding;
        _paper.Box($"{_id}_cf_sv").Size(size, size)
            .OnClick(e => SetSV(popEl, e, a))
            .OnDragging(e => SetSV(popEl, e, a))
            .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
            {
                float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, ht = (float)r.Size.Y;
                var hc = HSVToColor32(h, 1, 1);

                canvas.RoundedRectFilled(x, y, w, ht, svRound, svRound, svRound, svRound, hc);
                canvas.SetLinearBrush(x, y + ht / 2, x + w, y + ht / 2,
                    Color32.FromArgb(255, 255, 255, 255), Color32.FromArgb(0, 255, 255, 255));
                canvas.RoundedRectFilled(x, y, w, ht, svRound, svRound, svRound, svRound, Color32.FromArgb(255, 255, 255, 255));
                canvas.ClearBrush();
                canvas.SetLinearBrush(x + w / 2, y, x + w / 2, y + ht,
                    Color32.FromArgb(0, 0, 0, 0), Color32.FromArgb(255, 0, 0, 0));
                canvas.RoundedRectFilled(x, y, w, ht, svRound, svRound, svRound, svRound, Color32.FromArgb(255, 255, 255, 255));
                canvas.ClearBrush();

                canvas.SetStrokeColor(_theme.Primary.C400);
                canvas.SetStrokeWidth(1);
                canvas.BeginPath(); canvas.RoundedRect(x, y, w, ht, svRound, svRound, svRound, svRound); canvas.Stroke();

                float cx = x + s * w, cy = y + (1f - v) * ht;
                canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                canvas.SetStrokeWidth(2);
                canvas.BeginPath(); canvas.Circle(cx, cy, 5, 16); canvas.Stroke();
                canvas.SetStrokeColor(Color32.FromArgb(255, 0, 0, 0));
                canvas.SetStrokeWidth(1);
                canvas.BeginPath(); canvas.Circle(cx, cy, 6, 16); canvas.Stroke();
            }));
    }

    private void SetSV(ElementHandle popEl, PaperUI.Events.ElementEvent e, float a)
    {
        float ns = Math.Clamp((float)e.NormalizedPosition.X, 0, 1);
        float nv = 1f - Math.Clamp((float)e.NormalizedPosition.Y, 0, 1);
        _paper.SetElementStorage(popEl, "s", ns);
        _paper.SetElementStorage(popEl, "v", nv);
        float h = _paper.GetElementStorage(popEl, "h", 0f);
        _setter(HSVToColor(h, ns, nv, _paper.GetElementStorage(popEl, "a", a)));
    }

    // ── Hue Bar ──────────────────────────────────────────────────

    private void DrawHueBar(ElementHandle popEl, float height, float h, float s, float v, float a)
    {
        _paper.Box($"{_id}_cf_hue").Size(BarWidth, height)
            .OnClick(e => SetHue(popEl, e))
            .OnDragging(e => SetHue(popEl, e))
            .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
            {
                float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, ht = (float)r.Size.Y;
                int segs = 12;
                float segH = ht / segs;
                for (int i = 0; i < segs; i++)
                {
                    var c1 = HSVToColor32((float)i / segs * 360, 1, 1);
                    var c2 = HSVToColor32((float)(i + 1) / segs * 360, 1, 1);
                    canvas.SetLinearBrush(x + w / 2, y + i * segH, x + w / 2, y + (i + 1) * segH, c1, c2);
                    canvas.RectFilled(x, y + i * segH, w, segH + 1, Color32.FromArgb(255, 255, 255, 255));
                    canvas.ClearBrush();
                }
                float cy = y + (h / 360f) * ht;
                canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                canvas.SetStrokeWidth(2);
                canvas.BeginPath(); canvas.Rect(x - 1, cy - 1, w, 4); canvas.Stroke();
            }));
    }

    private void SetHue(ElementHandle popEl, PaperUI.Events.ElementEvent e)
    {
        float nh = Math.Clamp((float)e.NormalizedPosition.Y, 0, 1) * 360f;
        _paper.SetElementStorage(popEl, "h", nh);
        float s = _paper.GetElementStorage(popEl, "s", 1f);
        float v = _paper.GetElementStorage(popEl, "v", 1f);
        float a = _paper.GetElementStorage(popEl, "a", 1f);
        _setter(HSVToColor(nh, s, v, a));
    }

    // ── Alpha Bar ────────────────────────────────────────────────

    private void DrawAlphaBar(ElementHandle popEl, float height, float h, float s, float v, float a)
    {
        var alphaRound = _theme.Metrics.SmallRounding;
        _paper.Box($"{_id}_cf_alpha").Size(BarWidth, height).Rounded(alphaRound)
            .OnClick(e => SetAlpha(popEl, e))
            .OnDragging(e => SetAlpha(popEl, e))
            .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
            {
                float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, ht = (float)r.Size.Y;
                DrawCheckerboard(canvas, x, y, w, ht, 4f);
                var col = HSVToColor32(h, s, v);
                var colT = Color32.FromArgb(0, col.R, col.G, col.B);
                canvas.SetLinearBrush(x + w / 2, y, x + w / 2, y + ht, col, colT);
                canvas.RoundedRectFilled(x, y, w, ht, alphaRound, alphaRound, alphaRound, alphaRound, Color32.FromArgb(255, 255, 255, 255));
                canvas.ClearBrush();
                float cy = y + (1f - a) * ht;
                canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                canvas.SetStrokeWidth(2);
                canvas.BeginPath(); canvas.Rect(x - 1, cy - 1, w, 4); canvas.Stroke();
            }));
    }

    private void SetAlpha(ElementHandle popEl, PaperUI.Events.ElementEvent e)
    {
        float na = 1f - Math.Clamp((float)e.NormalizedPosition.Y, 0, 1);
        _paper.SetElementStorage(popEl, "a", na);
        float h = _paper.GetElementStorage(popEl, "h", 0f);
        float s = _paper.GetElementStorage(popEl, "s", 1f);
        float v = _paper.GetElementStorage(popEl, "v", 1f);
        _setter(HSVToColor(h, s, v, na));
    }

    // ── HSV Inputs ───────────────────────────────────────────────

    private static readonly SysColor HueCol = SysColor.FromArgb(255, 200, 80, 80);
    private static readonly SysColor SatCol = SysColor.FromArgb(255, 80, 200, 80);
    private static readonly SysColor ValCol = SysColor.FromArgb(255, 80, 80, 200);
    private static readonly SysColor AlphaCol = SysColor.FromArgb(255, 180, 180, 180);
    private static readonly SysColor RedCol = SysColor.FromArgb(255, 200, 80, 80);
    private static readonly SysColor GreenCol = SysColor.FromArgb(255, 80, 200, 80);
    private static readonly SysColor BlueCol = SysColor.FromArgb(255, 80, 80, 200);

    private void DrawHSVInputs(ElementHandle popEl, float h, float s, float v, float a)
    {
        using (_paper.Row($"{_id}_cf_hsv_row").Height(UnitValue.Auto).RowBetween(_theme.Metrics.Spacing).Enter())
        {
            ChannelNumeric("H", $"{_id}_cf_h", (int)h, 0, 360, nh =>
            { _paper.SetElementStorage(popEl, "h", (float)nh); EmitFromHSV(popEl); }, HueCol);
            ChannelNumeric("S", $"{_id}_cf_s", (int)(s * 100), 0, 100, ns =>
            { _paper.SetElementStorage(popEl, "s", ns / 100f); EmitFromHSV(popEl); }, SatCol);
            ChannelNumeric("V", $"{_id}_cf_v", (int)(v * 100), 0, 100, nv =>
            { _paper.SetElementStorage(popEl, "v", nv / 100f); EmitFromHSV(popEl); }, ValCol);
            if (_showAlpha)
                ChannelNumeric("A", $"{_id}_cf_a", (int)(a * 255), 0, 255, na =>
                { _paper.SetElementStorage(popEl, "a", na / 255f); EmitFromHSV(popEl); }, AlphaCol);
        }
    }

    // ── RGB Inputs ───────────────────────────────────────────────

    private void DrawRGBInputs(ElementHandle popEl, Color c)
    {
        using (_paper.Row($"{_id}_cf_rgb_row").Height(UnitValue.Auto).RowBetween(_theme.Metrics.Spacing).Enter())
        {
            ChannelNumeric("R", $"{_id}_cf_r", Clamp255(c.R), 0, 255, nr =>
            { var nc = new Color(nr / 255f, c.G, c.B, c.A); SyncHSV(popEl, nc); _setter(nc); }, RedCol);
            ChannelNumeric("G", $"{_id}_cf_g", Clamp255(c.G), 0, 255, ng =>
            { var nc = new Color(c.R, ng / 255f, c.B, c.A); SyncHSV(popEl, nc); _setter(nc); }, GreenCol);
            ChannelNumeric("B", $"{_id}_cf_b", Clamp255(c.B), 0, 255, nb =>
            { var nc = new Color(c.R, c.G, nb / 255f, c.A); SyncHSV(popEl, nc); _setter(nc); }, BlueCol);
            if (_showAlpha)
            {
                float a = _paper.GetElementStorage(popEl, "a", 1f);
                ChannelNumeric("A", $"{_id}_cf_a2", Clamp255(a), 0, 255, na =>
                { _paper.SetElementStorage(popEl, "a", na / 255f); _setter(new Color(c.R, c.G, c.B, na / 255f)); }, AlphaCol);
            }
        }
    }

    // ── Hex Input ────────────────────────────────────────────────

    private void DrawHexInput(ElementHandle popEl, Color c)
    {
        int ri = Clamp255(c.R), gi = Clamp255(c.G), bi = Clamp255(c.B), ai = Clamp255(c.A);
        string hex = _showAlpha ? $"{ri:X2}{gi:X2}{bi:X2}{ai:X2}" : $"{ri:X2}{gi:X2}{bi:X2}";

        Origami.TextField(_paper, $"{_id}_cf_hex", hex, raw =>
        {
            raw = raw.TrimStart('#').Trim();
            if (TryParseHex(raw, out Color parsed))
            {
                SyncHSV(popEl, parsed);
                _setter(parsed);
            }
        }).Placeholder("#RRGGBB").Show();
    }

    // ── Channel Numeric Helper ───────────────────────────────────

    private void ChannelNumeric(string label, string id, int value, int min, int max, Action<int> onChange,
        SysColor? color = null)
    {
        Origami.NumericField<int>(_paper, id, value, v => onChange(Math.Clamp(v, min, max)))
            .Min(min).Max(max)
            .Prefix(label, color ?? SysColor.FromArgb(255, 180, 180, 180))
            .Width(UnitValue.Stretch())
            .Show();
    }

    // ── Palette ──────────────────────────────────────────────────

    private void DrawPalette(ElementHandle popEl)
    {
        if (_palette == null) return;

        var colors = _palette.Colors;
        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        float swatchSize = _palette.SwatchSize;
        float gap = metrics.SpacingSmall;
        float availWidth = PopWidth - metrics.PaddingLarge * 2;
        int cols = Math.Max(1, (int)((availWidth + gap) / (swatchSize + gap)));
        bool hasAdd = _palette.OnAdd != null;
        int totalItems = colors.Count + (hasAdd ? 1 : 0);
        int rowCount = (totalItems + cols - 1) / cols;
        if (_palette.MaxRows > 0) rowCount = Math.Min(rowCount, _palette.MaxRows);

        for (int row = 0; row < rowCount; row++)
        {
            using (_paper.Row($"{_id}_cf_pr{row}").Height(swatchSize).RowBetween(gap).Enter())
            {
                for (int col = 0; col < cols; col++)
                {
                    int itemIdx = row * cols + col;
                    if (itemIdx >= totalItems) break;

                    if (itemIdx < colors.Count)
                    {
                        int idx = itemIdx;
                        var pc = colors[idx];
                        int pr = Clamp255(pc.R), pg = Clamp255(pc.G), pb = Clamp255(pc.B), pa = Clamp255(pc.A);

                        _paper.Box($"{_id}_cf_ps{idx}")
                            .Size(swatchSize, swatchSize)
                            .BackgroundColor(SysColor.FromArgb(pa, pr, pg, pb))
                            .Rounded(metrics.SmallRounding)
                            .Hovered.BorderColor(_theme.Primary.C400).BorderWidth(1).End()
                            .OnClick(idx, (ci, _) =>
                            {
                                SyncHSV(popEl, colors[ci]);
                                _setter(colors[ci]);
                            })
                            .OnRightClick(idx, (ci, _) =>
                            {
                                colors.RemoveAt(ci);
                                _palette.OnRemoved?.Invoke(ci);
                            });
                    }
                    else
                    {
                        // Add button
                        _paper.Box($"{_id}_cf_padd")
                            .Size(swatchSize, swatchSize)
                            .BackgroundColor(ink.C100)
                            .Rounded(metrics.SmallRounding)
                            .BorderColor(ink.C200).BorderWidth(1)
                            .Hovered.BackgroundColor(ink.C200).End()
                            .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                            {
                                float cx = (float)r.Min.X + (float)r.Size.X / 2f;
                                float cy = (float)r.Min.Y + (float)r.Size.Y / 2f;
                                canvas.SetStrokeColor(Color32.FromArgb(180, 200, 200, 200));
                                canvas.SetStrokeWidth(1.5f);
                                canvas.BeginPath(); canvas.MoveTo(cx - 4, cy); canvas.LineTo(cx + 4, cy); canvas.Stroke();
                                canvas.BeginPath(); canvas.MoveTo(cx, cy - 4); canvas.LineTo(cx, cy + 4); canvas.Stroke();
                            }))
                            .OnClick(_ =>
                            {
                                var addColor = _palette.OnAdd?.Invoke();
                                if (addColor.HasValue)
                                {
                                    colors.Add(addColor.Value);
                                }
                            });
                    }
                }
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private void EmitFromHSV(ElementHandle popEl)
    {
        float h = _paper.GetElementStorage(popEl, "h", 0f);
        float s = _paper.GetElementStorage(popEl, "s", 1f);
        float v = _paper.GetElementStorage(popEl, "v", 1f);
        float a = _paper.GetElementStorage(popEl, "a", 1f);
        _setter(HSVToColor(h, s, v, a));
    }

    private void SyncHSV(ElementHandle popEl, Color c)
    {
        ColorToHSV(c, out float h, out float s, out float v);
        _paper.SetElementStorage(popEl, "h", h);
        _paper.SetElementStorage(popEl, "s", s);
        _paper.SetElementStorage(popEl, "v", v);
        _paper.SetElementStorage(popEl, "a", c.A);
    }

    private static int Clamp255(float v) => Math.Clamp((int)(v * 255), 0, 255);

    private static bool TryParseHex(string hex, out Color result)
    {
        result = default;
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            result = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
            return true;
        }
        if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgba))
        {
            result = new Color(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
            return true;
        }
        return false;
    }

    private static void DrawCheckerboard(Canvas canvas, float x, float y, float w, float h, float cellSize)
    {
        var light = Color32.FromArgb(255, 180, 180, 180);
        var dark = Color32.FromArgb(255, 120, 120, 120);
        int cols = (int)MathF.Ceiling(w / cellSize);
        int rows = (int)MathF.Ceiling(h / cellSize);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                canvas.RectFilled(x + c * cellSize, y + r * cellSize,
                    MathF.Min(cellSize, x + w - (x + c * cellSize)),
                    MathF.Min(cellSize, y + h - (y + r * cellSize)),
                    (r + c) % 2 == 0 ? light : dark);
    }

    // ── Color Math ───────────────────────────────────────────────

    private static void ColorToHSV(Color c, out float h, out float s, out float v)
    {
        float r = c.R, g = c.G, b = c.B;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b)), delta = max - min;
        v = max; s = max > 0 ? delta / max : 0;
        if (delta == 0) h = 0;
        else if (max == r) h = 60f * (((g - b) / delta) % 6);
        else if (max == g) h = 60f * (((b - r) / delta) + 2);
        else h = 60f * (((r - g) / delta) + 4);
        if (h < 0) h += 360;
    }

    private static Color HSVToColor(float h, float s, float v, float a = 1f)
    {
        float c = v * s, x = c * (1f - MathF.Abs((h / 60f) % 2 - 1)), m = v - c;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; } else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
        return new Color(r + m, g + m, b + m, a);
    }

    private static Color32 HSVToColor32(float h, float s, float v)
    {
        var c = HSVToColor(h, s, v);
        return Color32.FromArgb(255, Clamp255(c.R), Clamp255(c.G), Clamp255(c.B));
    }
}

/// <summary>
/// Modal wrapper for the color picker popover. Pushed onto the modal stack
/// when the color swatch is clicked.
/// </summary>
internal sealed class ColorPickerModal : IModal
{
    private readonly string _id;
    private Color _value;
    private readonly Action<Color> _setter;
    private readonly bool _showAlpha;
    private readonly bool _hdr;
    private readonly ColorPalette? _palette;
    private readonly float _anchorX;
    private readonly float _anchorY;

    public bool CloseOnBackdrop => true;
    public bool CloseOnEscape => true;

    public ColorPickerModal(string id, Color value, Action<Color> setter, bool showAlpha, bool hdr, ColorPalette? palette, float anchorX, float anchorY)
    {
        _id = id;
        _value = value;
        _setter = setter;
        _showAlpha = showAlpha;
        _hdr = hdr;
        _palette = palette;
        _anchorX = anchorX;
        _anchorY = anchorY;
    }

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;

        using (paper.Column($"{_id}_cpmod")
            .PositionType(PositionType.SelfDirected)
            .Position(_anchorX, _anchorY)
            .Width(300f).Height(UnitValue.Auto)
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(theme.Ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .BoxShadow(0, 4, 24, 0, SysColor.FromArgb(100, 0, 0, 0))
            .Layer(layer)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            var builder = new ColorFieldBuilder(paper, _id, _value, v =>
            {
                _value = v;
                _setter(v);
            }, theme);
            if (!_showAlpha) builder.Alpha(false);
            if (_hdr) builder.HDR(true);
            if (_palette != null) builder.Palette(_palette);
            builder.RenderPopover();
        }
    }
}
