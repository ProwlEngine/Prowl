// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.Vector;

using Color = Prowl.Vector.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Header display style.
/// </summary>
public enum HeaderStyle
{
    /// <summary>Plain text label with slightly larger/bolder appearance. No background.</summary>
    Text,
    /// <summary>Text with a horizontal line extending to the right.</summary>
    Line,
    /// <summary>Text with horizontal lines on both sides (centered divider).</summary>
    LineCentered,
    /// <summary>Filled background strip.</summary>
    Box,
    /// <summary>Just a horizontal line separator with no text.</summary>
    Separator,
    /// <summary>Subtle underline below the text.</summary>
    Underline,
}

/// <summary>
/// Fluent builder for an Origami header / section divider. Construct via
/// <see cref="Origami.Header"/>; chain modifiers; call <see cref="Show"/> to render.
///
/// Renders everything in a single Paper box with a canvas Draw callback for performance.
/// Supports multiple visual styles, variant coloring, icons, and badges.
/// </summary>
public sealed class HeaderBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private string _label;
    private HeaderStyle _style = HeaderStyle.Text;
    private OrigamiVariant _variant = OrigamiVariant.Default;
    private string? _icon;
    private string? _badge;
    private float _height;
    private float? _fontSizeOverride;
    private float _lineThickness = 1f;
    private float _topMargin = 6f;
    private float _bottomMargin = 2f;

    internal HeaderBuilder(Paper paper, string id, string label, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _label = label;
        _theme = theme;
    }

    // ── Style ────────────────────────────────────────────────────

    /// <summary>Set the header display style.</summary>
    public HeaderBuilder Style(HeaderStyle style) { _style = style; return this; }

    /// <summary>Plain text label (default).</summary>
    public HeaderBuilder Text() => Style(HeaderStyle.Text);

    /// <summary>Text with a horizontal line extending to the right.</summary>
    public HeaderBuilder Line() => Style(HeaderStyle.Line);

    /// <summary>Text with horizontal lines on both sides.</summary>
    public HeaderBuilder LineCentered() => Style(HeaderStyle.LineCentered);

    /// <summary>Filled background strip.</summary>
    public HeaderBuilder Box() => Style(HeaderStyle.Box);

    /// <summary>Just a horizontal line, no text.</summary>
    public HeaderBuilder Separator() => Style(HeaderStyle.Separator);

    /// <summary>Text with an underline accent below it.</summary>
    public HeaderBuilder Underline() => Style(HeaderStyle.Underline);

    // ── Appearance ───────────────────────────────────────────────

    /// <summary>Color variant for the header accent (line, box fill, underline).</summary>
    public HeaderBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public HeaderBuilder Primary() => Variant(OrigamiVariant.Primary);
    public HeaderBuilder Success() => Variant(OrigamiVariant.Success);
    public HeaderBuilder Warning() => Variant(OrigamiVariant.Warning);
    public HeaderBuilder Danger() => Variant(OrigamiVariant.Danger);
    public HeaderBuilder Info() => Variant(OrigamiVariant.Info);

    /// <summary>Leading icon glyph (FontAwesome).</summary>
    public HeaderBuilder Icon(string glyph) { _icon = glyph; return this; }

    /// <summary>Trailing badge text (right-aligned).</summary>
    public HeaderBuilder Badge(string text) { _badge = text; return this; }

    /// <summary>Override the font size. Default scales from the theme.</summary>
    public HeaderBuilder FontSize(float size) { _fontSizeOverride = size; return this; }

    /// <summary>Line thickness for Line/LineCentered/Separator/Underline styles.</summary>
    public HeaderBuilder Thickness(float t) { _lineThickness = Math.Max(0.5f, t); return this; }

    /// <summary>Override the total height of the header row.</summary>
    public HeaderBuilder Height(float h) { _height = h; return this; }

    /// <summary>Vertical margins above and below the header.</summary>
    public HeaderBuilder Margin(float top, float bottom) { _topMargin = top; _bottomMargin = bottom; return this; }

    // ── Terminator ───────────────────────────────────────────────

    public void Show()
    {
        var font = _theme.Font;
        if (font == null) return;

        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        var ramp = _variant == OrigamiVariant.Default ? ink : _theme.Get(_variant);

        float fontSize = _fontSizeOverride ?? metrics.FontSize + 1;
        float rowHeight = _height > 0 ? _height : metrics.HeaderHeight + 4;
        float rounding = metrics.Rounding;

        Color textColor = _variant == OrigamiVariant.Default ? ink.C500 : ramp.C500;
        Color lineColor = _variant == OrigamiVariant.Default ? ink.C200 : ramp.C300;
        Color boxBg = _variant == OrigamiVariant.Default ? ink.C100 : ramp.C200;
        Color badgeColor = ink.C300;

        // Capture everything the paint callback needs into a snapshot struct
        // so the closure doesn't capture `this` (the builder is transient).
        var snap = new HeaderSnapshot
        {
            Label = _label,
            Icon = _icon,
            Badge = _badge,
            Style = _style,
            Font = font,
            FontSize = fontSize,
            TextColor = textColor,
            LineColor = lineColor,
            BoxBg = boxBg,
            BadgeColor = badgeColor,
            LineThickness = _lineThickness,
            Rounding = rounding,
            Pad = 8f,
            IconGap = 4f,
        };

        // Single box - all drawing happens in the canvas callback
        using (_paper.Box(_id)
            .Height(rowHeight)
            .Margin(0, 0, _topMargin, _bottomMargin)
            .IsNotInteractable().Enter())
        {
            _paper.Draw((canvas, rect) => Paint(canvas, rect, in snap));
        }
    }

    // ── Render snapshot (value type, no GC) ──────────────────────

    private struct HeaderSnapshot
    {
        public string Label;
        public string? Icon;
        public string? Badge;
        public HeaderStyle Style;
        public Prowl.Scribe.FontFile Font;
        public float FontSize;
        public Color TextColor;
        public Color LineColor;
        public Color BoxBg;
        public Color BadgeColor;
        public float LineThickness;
        public float Rounding;
        public float Pad;
        public float IconGap;
    }

    // ── Canvas paint ─────────────────────────────────────────────

    private static void Paint(Quill.Canvas canvas, Rect rect, in HeaderSnapshot s)
    {
        float x = (float)rect.Min.X;
        float y = (float)rect.Min.Y;
        float w = (float)rect.Size.X;
        float h = (float)rect.Size.Y;
        float cy = y + h * 0.5f;

        bool hasIcon = !string.IsNullOrEmpty(s.Icon);
        bool hasBadge = !string.IsNullOrEmpty(s.Badge);
        bool hasLabel = !string.IsNullOrEmpty(s.Label);

        // Measure text segments
        Float2 labelSize = hasLabel ? canvas.MeasureText(s.Label, s.FontSize, s.Font) : Float2.Zero;
        Float2 iconSize = hasIcon ? canvas.MeasureText(s.Icon!, s.FontSize - 1, s.Font) : Float2.Zero;
        Float2 badgeSize = hasBadge ? canvas.MeasureText(s.Badge!, s.FontSize - 2, s.Font) : Float2.Zero;

        float iconW = hasIcon ? (float)iconSize.X + s.IconGap : 0;
        float badgeW = hasBadge ? (float)badgeSize.X : 0;

        // Text block width = icon + label
        float textBlockW = iconW + (hasLabel ? (float)labelSize.X : 0);

        // Cursor for drawing left-to-right
        float cx;

        switch (s.Style)
        {
            case HeaderStyle.Separator:
                canvas.RectFilled(x, cy - s.LineThickness * 0.5f, w, s.LineThickness, s.LineColor);
                break;

            case HeaderStyle.Text:
                cx = x + s.Pad;
                if (hasIcon) { DrawIcon(canvas, s, cx, y, h); cx += iconW; }
                if (hasLabel) DrawLabel(canvas, s, cx, y, h);
                if (hasBadge) DrawBadge(canvas, s, x + w - s.Pad - badgeW, y, h);
                break;

            case HeaderStyle.Line:
                cx = x + s.Pad;
                if (hasIcon) { DrawIcon(canvas, s, cx, y, h); cx += iconW; }
                if (hasLabel) { DrawLabel(canvas, s, cx, y, h); cx += (float)labelSize.X; }
                if (hasBadge)
                {
                    // Line between label and badge
                    float badgeX = x + w - s.Pad - badgeW;
                    float lineStart = cx + s.Pad;
                    float lineEnd = badgeX - s.Pad;
                    if (lineEnd > lineStart)
                        canvas.RectFilled(lineStart, cy - s.LineThickness * 0.5f, lineEnd - lineStart, s.LineThickness, s.LineColor);
                    DrawBadge(canvas, s, badgeX, y, h);
                }
                else
                {
                    // Line from after text to the right edge
                    float lineStart = cx + s.Pad;
                    float lineEnd = x + w - s.Pad;
                    if (lineEnd > lineStart)
                        canvas.RectFilled(lineStart, cy - s.LineThickness * 0.5f, lineEnd - lineStart, s.LineThickness, s.LineColor);
                }
                break;

            case HeaderStyle.LineCentered:
                // Center the text block, lines on both sides
                float centerX = x + (w - textBlockW) * 0.5f;
                // Left line
                float llEnd = centerX - s.Pad;
                if (llEnd > x + s.Pad)
                    canvas.RectFilled(x + s.Pad, cy - s.LineThickness * 0.5f, llEnd - x - s.Pad, s.LineThickness, s.LineColor);
                // Icon + label
                cx = centerX;
                if (hasIcon) { DrawIcon(canvas, s, cx, y, h); cx += iconW; }
                if (hasLabel) { DrawLabel(canvas, s, cx, y, h); cx += (float)labelSize.X; }
                // Right line
                float rlStart = cx + s.Pad;
                float rlEnd = x + w - s.Pad;
                if (hasBadge)
                {
                    float badgeX = rlEnd - badgeW;
                    rlEnd = badgeX - s.Pad;
                    DrawBadge(canvas, s, badgeX, y, h);
                }
                if (rlEnd > rlStart)
                    canvas.RectFilled(rlStart, cy - s.LineThickness * 0.5f, rlEnd - rlStart, s.LineThickness, s.LineColor);
                break;

            case HeaderStyle.Box:
                canvas.RoundedRectFilled(x, y, w, h, s.Rounding, s.BoxBg);
                cx = x + s.Pad;
                if (hasIcon) { DrawIcon(canvas, s, cx, y, h); cx += iconW; }
                if (hasLabel) DrawLabel(canvas, s, cx, y, h);
                if (hasBadge) DrawBadge(canvas, s, x + w - s.Pad - badgeW, y, h);
                break;

            case HeaderStyle.Underline:
                cx = x + s.Pad;
                if (hasIcon) { DrawIcon(canvas, s, cx, y, h); cx += iconW; }
                if (hasLabel) DrawLabel(canvas, s, cx, y, h);
                if (hasBadge) DrawBadge(canvas, s, x + w - s.Pad - badgeW, y, h);
                // Underline at the bottom
                canvas.RectFilled(x, y + h - s.LineThickness, w, s.LineThickness, s.LineColor);
                break;
        }
    }

    private static void DrawIcon(Quill.Canvas canvas, in HeaderSnapshot s, float x, float y, float h)
    {
        Float2 size = canvas.MeasureText(s.Icon!, s.FontSize - 1, s.Font);
        float ty = y + (h - (float)size.Y) * 0.5f;
        canvas.DrawText(s.Icon!, x, ty, s.TextColor, s.FontSize - 1, s.Font);
    }

    private static void DrawLabel(Quill.Canvas canvas, in HeaderSnapshot s, float x, float y, float h)
    {
        Float2 size = canvas.MeasureText(s.Label, s.FontSize, s.Font);
        float ty = y + (h - (float)size.Y) * 0.5f;
        canvas.DrawText(s.Label, x, ty, s.TextColor, s.FontSize, s.Font);
    }

    private static void DrawBadge(Quill.Canvas canvas, in HeaderSnapshot s, float x, float y, float h)
    {
        Float2 size = canvas.MeasureText(s.Badge!, s.FontSize - 2, s.Font);
        float ty = y + (h - (float)size.Y) * 0.5f;
        canvas.DrawText(s.Badge!, x, ty, s.BadgeColor, s.FontSize - 2, s.Font);
    }
}
