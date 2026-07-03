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
    /// <summary>Foldout / component header row: vector chevron, leading icon, title, checkbox and "more" dots.</summary>
    Component,
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

    // Component-row extras (all vector-drawn; the UI font ships no glyphs).
    private bool _chevron;
    private bool _chevronExpanded = true;
    private bool _checkbox;
    private bool _checkboxOn;
    private bool _more;
    private IOrigamiIcon? _iconObj;
    private Action? _onClick;

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

    /// <summary>Foldout / component header row (vector chevron + icon + title + checkbox + more).</summary>
    public HeaderBuilder Component() => Style(HeaderStyle.Component);

    // ── Component row parts ──────────────────────────────────────

    /// <summary>Show a leading disclosure chevron (down when <paramref name="expanded"/>, else right).</summary>
    public HeaderBuilder Chevron(bool expanded = true) { _chevron = true; _chevronExpanded = expanded; return this; }

    /// <summary>Show a trailing checkbox with a vector check when <paramref name="on"/>.</summary>
    public HeaderBuilder Checkbox(bool on) { _checkbox = true; _checkboxOn = on; return this; }

    /// <summary>Show a trailing vertical "more options" ellipsis.</summary>
    public HeaderBuilder More(bool show = true) { _more = show; return this; }

    /// <summary>Leading icon (acc-300) drawn into a reserved, row-tall cell.</summary>
    public HeaderBuilder Icon(IOrigamiIcon? icon) { _iconObj = icon; return this; }

    /// <summary>Make the header row clickable (foldout toggle, section collapse, ...).</summary>
    public HeaderBuilder OnClick(Action onClick) { _onClick = onClick; return this; }

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
        bool isComponent = _style == HeaderStyle.Component;

        // Component title is 600 (semi-bold); the uppercase section label is 700 (bold).
        var labelFont = (isComponent ? _theme.SemiBold : _theme.Bold) ?? font;

        // Section label ~= 11px (metrics-2); component title ~= 12.5px (metrics).
        float fontSize = _fontSizeOverride ?? (isComponent ? metrics.FontSize : metrics.FontSize - 2f);
        float rowHeight = _height > 0 ? _height : metrics.HeaderHeight + 4;
        float rounding = metrics.Rounding;

        // Section label: UPPERCASE, wide tracking, acc-300. Component title: t-hi, normal case.
        bool uppercase = !isComponent;
        float letterSpacing = uppercase ? fontSize * 0.06f : 0f;
        string label = uppercase ? (_label ?? string.Empty).ToUpperInvariant() : (_label ?? string.Empty);

        Color textColor = isComponent
            ? ink.C500                                                  // t-hi title
            : (_variant == OrigamiVariant.Default ? _theme.Primary.C700 // acc-300 section label
                                                  : ramp.C500);
        Color lineColor = _variant == OrigamiVariant.Default
            ? _theme.BorderSoft          // bd-soft
            : ramp.C300;
        Color boxBg = _variant == OrigamiVariant.Default ? _theme.Neutral.C300 : ramp.C200; // glass-head
        Color badgeColor = ink.C200;                                    // t-lo

        // Capture everything the paint callback needs into a snapshot struct
        // so the closure doesn't capture `this` (the builder is transient).
        var snap = new HeaderSnapshot
        {
            Label = label,
            Icon = _icon,
            Badge = _badge,
            Style = _style,
            Font = labelFont,
            FontSize = fontSize,
            LetterSpacing = letterSpacing,
            TextColor = textColor,
            LineColor = lineColor,
            BoxBg = boxBg,
            BadgeColor = badgeColor,
            LineThickness = _lineThickness,
            Rounding = rounding,
            Pad = 8f,
            IconGap = 4f,
            Chevron = _chevron,
            ChevronExpanded = _chevronExpanded,
            Checkbox = _checkbox,
            CheckboxOn = _checkboxOn,
            More = _more,
            IconObj = _iconObj,
            ChevronColor = ink.C200,        // t-lo
            AccentColor = _theme.Primary.C500,   // acc
            CheckColor = ink.C600,          // white
            MoreColor = ink.C200,           // t-lo
        };

        // Single box - all drawing happens in the canvas callback.
        var box = _paper.Box(_id)
            .Height(rowHeight)
            .Margin(0, 0, _topMargin, _bottomMargin);

        // The component/foldout row carries the .w2hdrrow chrome: glass-in fill, bd-soft border, radius 8.
        if (isComponent)
            box.BackgroundColor(_theme.Glass)
               .BorderColor(_theme.BorderSoft).BorderWidth(1)
               .Rounded(8f);

        if (_onClick != null)
        {
            var click = _onClick;
            if (!isComponent) box.Rounded(rounding);
            box.OnClick(_ => click());
            box.Hovered.BackgroundColor(_theme.Hover).End(); // hover
        }
        else
        {
            box.IsNotInteractable();
        }

        using (box.Enter())
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
        public float LetterSpacing;
        public Color TextColor;
        public Color LineColor;
        public Color BoxBg;
        public Color BadgeColor;
        public float LineThickness;
        public float Rounding;
        public float Pad;
        public float IconGap;

        // Component-row parts
        public bool Chevron;
        public bool ChevronExpanded;
        public bool Checkbox;
        public bool CheckboxOn;
        public bool More;
        public IOrigamiIcon? IconObj;
        public Color ChevronColor;
        public Color AccentColor;
        public Color CheckColor;
        public Color MoreColor;
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

        // Component row is laid out separately (vector parts + spacer + right cluster).
        if (s.Style == HeaderStyle.Component)
        {
            DrawComponentRow(canvas, s, x, y, w, h, cy);
            return;
        }

        // Measure text segments
        Float2 labelSize = hasLabel ? canvas.MeasureText(s.Label, s.FontSize, s.Font, s.LetterSpacing) : Float2.Zero;
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
        Float2 size = canvas.MeasureText(s.Label, s.FontSize, s.Font, s.LetterSpacing);
        float ty = y + (h - (float)size.Y) * 0.5f;
        canvas.DrawText(s.Label, x, ty, s.TextColor, s.FontSize, s.Font, s.LetterSpacing);
    }

    private static void DrawBadge(Quill.Canvas canvas, in HeaderSnapshot s, float x, float y, float h)
    {
        Float2 size = canvas.MeasureText(s.Badge!, s.FontSize - 2, s.Font);
        float ty = y + (h - (float)size.Y) * 0.5f;
        canvas.DrawText(s.Badge!, x, ty, s.BadgeColor, s.FontSize - 2, s.Font);
    }

    // ── Component header row ─────────────────────────────────────

    private static void DrawComponentRow(Quill.Canvas canvas, in HeaderSnapshot s,
        float x, float y, float w, float h, float cy)
    {
        float pad = s.Pad;
        float gap = s.IconGap + 2f;
        float cx = x + pad;

        // Leading disclosure chevron (~12px, t-lo).
        if (s.Chevron)
        {
            float sz = s.FontSize * 0.78f;
            DrawChevron(canvas, cx + sz * 0.5f, cy, sz, s.ChevronExpanded, s.ChevronColor);
            cx += sz + gap;
        }

        // Leading icon (~14px, acc-300).
        if (s.IconObj != null)
        {
            float sz = s.FontSize * 0.9f;
            s.IconObj.Draw(canvas, new Rect(cx, cy - sz * 0.5f, cx + sz, cy + sz * 0.5f), s.AccentColor);
            cx += sz + gap;
        }

        // Title.
        if (!string.IsNullOrEmpty(s.Label))
        {
            Float2 ts = canvas.MeasureText(s.Label, s.FontSize, s.Font, s.LetterSpacing);
            canvas.DrawText(s.Label, cx, cy - (float)ts.Y * 0.5f, s.TextColor, s.FontSize, s.Font, s.LetterSpacing);
        }

        // Right cluster (laid out right-to-left): more dots, then checkbox.
        float rx = x + w - pad;
        if (s.More)
        {
            rx -= MathF.Max(2f, s.FontSize * 0.16f);
            DrawMoreDots(canvas, rx, cy, s.FontSize, s.MoreColor);
            rx -= gap + MathF.Max(2f, s.FontSize * 0.16f);
        }
        if (s.Checkbox)
        {
            float box = s.FontSize * 0.95f;
            rx -= box;
            DrawCheckbox(canvas, rx, cy - box * 0.5f, box, s.CheckboxOn, s.AccentColor, s.CheckColor);
        }
    }

    private static void DrawChevron(Quill.Canvas canvas, float cx, float cy, float size, bool down, Color color)
    {
        float half = size * 0.5f;
        float q = size * 0.28f;
        canvas.SaveState();
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(MathF.Max(1.4f, size * 0.14f));
        canvas.SetStrokeCap(Quill.EndCapStyle.Round);
        canvas.SetStrokeJoint(Quill.JointStyle.Round);
        canvas.BeginPath();
        if (down)
        {
            canvas.MoveTo(cx - half + q, cy - q * 0.5f);
            canvas.LineTo(cx, cy + q * 0.7f);
            canvas.LineTo(cx + half - q, cy - q * 0.5f);
        }
        else
        {
            canvas.MoveTo(cx - q * 0.5f, cy - half + q);
            canvas.LineTo(cx + q * 0.7f, cy);
            canvas.LineTo(cx - q * 0.5f, cy + half - q);
        }
        canvas.Stroke();
        canvas.RestoreState();
    }

    private static void DrawCheckbox(Quill.Canvas canvas, float x, float y, float size, bool on, Color accent, Color check)
    {
        float r = size * 0.28f;
        Color border = on ? accent : Origami.Current.BorderStrong; // bd-strong
        Color fill = on ? accent : Origami.Current.Glass;          // acc / glass-in
        canvas.RoundedRectFilled(x, y, size, size, r, border);
        canvas.RoundedRectFilled(x + 1, y + 1, size - 2, size - 2, MathF.Max(0, r - 1), fill);

        if (on)
        {
            canvas.SaveState();
            canvas.SetStrokeColor(check);
            canvas.SetStrokeWidth(MathF.Max(1.4f, size * 0.13f));
            canvas.SetStrokeCap(Quill.EndCapStyle.Round);
            canvas.SetStrokeJoint(Quill.JointStyle.Round);
            canvas.BeginPath();
            canvas.MoveTo(x + size * 0.26f, y + size * 0.52f);
            canvas.LineTo(x + size * 0.43f, y + size * 0.68f);
            canvas.LineTo(x + size * 0.74f, y + size * 0.34f);
            canvas.Stroke();
            canvas.RestoreState();
        }
    }

    private static void DrawMoreDots(Quill.Canvas canvas, float cx, float cy, float fontSize, Color color)
    {
        float r = MathF.Max(1f, fontSize * 0.08f);
        float gap = fontSize * 0.26f;
        canvas.CircleFilled(cx, cy - gap, r, color);
        canvas.CircleFilled(cx, cy, r, color);
        canvas.CircleFilled(cx, cy + gap, r, color);
    }
}
