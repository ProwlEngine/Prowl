// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Preset font sizes for an Origami label, relative to the theme base.</summary>
public enum LabelSize
{
    XS,
    SM,
    MD,
    LG,
    XL,
}

/// <summary>Horizontal alignment of the label content within its box.</summary>
public enum LabelHAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Vertical alignment of the label content within its box.</summary>
public enum LabelVAlign
{
    Top,
    Middle,
    Bottom,
}

/// <summary>How a label handles content wider than its box.</summary>
public enum LabelTruncation
{
    /// <summary>No truncation; content draws past the right edge.</summary>
    None,

    /// <summary>Append "..." to the longest prefix that fits.</summary>
    Ellipsis,

    /// <summary>Hard-clip to the box (no ellipsis glyph).</summary>
    Clip,
}

/// <summary>
/// Fluent builder for an Origami label. Construct via <see cref="Origami.Label"/>;
/// chain modifiers; call <see cref="Show"/> to render.
///
/// <para>Renders everything in a single Paper box with a canvas Draw callback. Supports
/// variant coloring, leading/trailing icons, tooltip, click handler, and a small set of
/// decorations (background, border, underline, strikethrough, drop shadow, inset).</para>
/// </summary>
public sealed class LabelBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly string _text;

    // Core
    private OrigamiVariant _variant = OrigamiVariant.Default;
    private LabelSize _size = LabelSize.MD;
    private LabelHAlign _hAlign = LabelHAlign.Left;
    private LabelVAlign _vAlign = LabelVAlign.Middle;
    private LabelTruncation _truncation = LabelTruncation.None;
    private string? _leadingIcon;
    private string? _trailingIcon;
    private string? _tooltip;
    private bool _disabled;
    private Action? _onClick;

    private float? _fontSizeOverride;
    private Prowl.Scribe.FontFile? _fontOverride;
    private Color? _colorOverride;
    private float? _widthOverride;
    private float? _heightOverride;
    private float _padX = 4f;
    private float _padY = 0f;
    private float _iconGap = 4f;
    private float _letterSpacingEm;
    private bool _uppercase;

    // Leading vector-draw hook (used for semantic dots and any glyph-less icon,
    // since OrigamiIcons ships empty strings by default).
    private Action<Canvas, Rect>? _leadingDraw;
    private IOrigamiIcon? _leadingIconObj;
    private float _leadingDrawWidth;
    private bool _dot;
    private Color? _dotColor;

    // Decorations
    private bool _bg;
    private Color? _bgColor;
    private float? _bgRounding;
    private bool _bgPill;

    private bool _border;
    private Color? _borderColor;
    private float _borderThickness = 1f;

    private bool _underline;
    private bool _doubleUnderline;
    private bool _strikethrough;
    private Color? _decorColor;
    private float _decorThickness = 1f;

    private bool _shadow;
    private Color _shadowColor = Color.FromArgb(160, 0, 0, 0);
    private float _shadowDx = 1f;
    private float _shadowDy = 1f;

    private bool _inset;
    private Color? _insetHighlight;

    internal LabelBuilder(Paper paper, string id, string text, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _text = text ?? string.Empty;
        _theme = theme;
    }

    // ── Variant ──────────────────────────────────────────────────

    public LabelBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public LabelBuilder Primary() => Variant(OrigamiVariant.Primary);
    public LabelBuilder Success() => Variant(OrigamiVariant.Success);
    public LabelBuilder Warning() => Variant(OrigamiVariant.Warning);
    public LabelBuilder Danger() => Variant(OrigamiVariant.Danger);
    public LabelBuilder Info() => Variant(OrigamiVariant.Info);
    public LabelBuilder Subtle() => Variant(OrigamiVariant.Subtle);

    // ── Size / font ──────────────────────────────────────────────

    public LabelBuilder Size(LabelSize s) { _size = s; return this; }
    public LabelBuilder XS() => Size(LabelSize.XS);
    public LabelBuilder SM() => Size(LabelSize.SM);
    public LabelBuilder MD() => Size(LabelSize.MD);
    public LabelBuilder LG() => Size(LabelSize.LG);
    public LabelBuilder XL() => Size(LabelSize.XL);

    /// <summary>Override the resolved font size. Bypasses the size preset.</summary>
    public LabelBuilder FontSize(float px) { _fontSizeOverride = px; return this; }

    /// <summary>Set the text colour explicitly. Overrides variant and disabled treatment.</summary>
    public LabelBuilder TextColor(Color color) { _colorOverride = color; return this; }

    // ── Alignment / sizing ───────────────────────────────────────

    public LabelBuilder Align(LabelHAlign h) { _hAlign = h; return this; }
    public LabelBuilder AlignLeft() => Align(LabelHAlign.Left);
    public LabelBuilder AlignCenter() => Align(LabelHAlign.Center);
    public LabelBuilder AlignRight() => Align(LabelHAlign.Right);

    public LabelBuilder VAlign(LabelVAlign v) { _vAlign = v; return this; }

    public LabelBuilder Truncate(LabelTruncation mode) { _truncation = mode; return this; }
    public LabelBuilder Ellipsis() => Truncate(LabelTruncation.Ellipsis);
    public LabelBuilder Clip() => Truncate(LabelTruncation.Clip);

    /// <summary>Fix the box width. Required for truncation/alignment to be meaningful.</summary>
    public LabelBuilder Width(float w) { _widthOverride = w; return this; }
    public LabelBuilder Height(float h) { _heightOverride = h; return this; }
    public LabelBuilder Padding(float x, float y) { _padX = x; _padY = y; return this; }
    public LabelBuilder IconGap(float g) { _iconGap = g; return this; }

    // ── Icons / tooltip ──────────────────────────────────────────

    public LabelBuilder LeadingIcon(string glyph) { _leadingIcon = glyph; return this; }
    public LabelBuilder TrailingIcon(string glyph) { _trailingIcon = glyph; return this; }

    public LabelBuilder Tooltip(string text) { _tooltip = text; return this; }

    // ── State / interaction ──────────────────────────────────────

    public LabelBuilder Disabled(bool disabled = true) { _disabled = disabled; return this; }
    public LabelBuilder OnClick(Action onClick) { _onClick = onClick; return this; }

    // ── Decorations ──────────────────────────────────────────────

    /// <summary>Fill behind the label. With no args, picks a colour from the active variant.</summary>
    public LabelBuilder Background(Color? color = null, float? rounding = null)
    {
        _bg = true;
        if (color.HasValue) _bgColor = color;
        if (rounding.HasValue) _bgRounding = rounding;
        return this;
    }

    /// <summary>Background with corner radius = height/2 — chip/pill look.</summary>
    public LabelBuilder Pill(Color? color = null)
    {
        _bg = true;
        _bgPill = true;
        if (color.HasValue) _bgColor = color;
        return this;
    }

    /// <summary>Outline ring around the label box.</summary>
    public LabelBuilder Border(Color? color = null, float thickness = 1f)
    {
        _border = true;
        if (color.HasValue) _borderColor = color;
        _borderThickness = MathF.Max(0.5f, thickness);
        return this;
    }

    public LabelBuilder Underline(Color? color = null, float thickness = 1f)
    {
        _underline = true;
        if (color.HasValue) _decorColor = color;
        _decorThickness = MathF.Max(0.5f, thickness);
        return this;
    }

    public LabelBuilder DoubleUnderline(Color? color = null, float thickness = 1f)
    {
        _doubleUnderline = true;
        if (color.HasValue) _decorColor = color;
        _decorThickness = MathF.Max(0.5f, thickness);
        return this;
    }

    public LabelBuilder Strikethrough(Color? color = null, float thickness = 1f)
    {
        _strikethrough = true;
        if (color.HasValue) _decorColor = color;
        _decorThickness = MathF.Max(0.5f, thickness);
        return this;
    }

    /// <summary>Offset shadow behind the text. Drawn as a re-rendered copy in shadow colour.</summary>
    public LabelBuilder Shadow(Color? color = null, float dx = 1f, float dy = 1f)
    {
        _shadow = true;
        if (color.HasValue) _shadowColor = color.Value;
        _shadowDx = dx;
        _shadowDy = dy;
        return this;
    }

    /// <summary>
    /// Engraved/inset look: a light highlight one pixel below-right of the text, drawn
    /// before the main text. Highlight colour defaults to ink.C600 (extra-bright stop).
    /// </summary>
    public LabelBuilder Inset(Color? highlight = null)
    {
        _inset = true;
        if (highlight.HasValue) _insetHighlight = highlight;
        return this;
    }

    // ── Nebula presets ───────────────────────────────────────────
    // Thin wrappers over the primitives above that reproduce the prototype's named
    // text roles. Weight (700/600) has no analogue in the single-weight UI font, so
    // emphasis is carried by size + brighter ink stops.

    /// <summary>Bright section heading (t-hi, ~17px, bold) with tight tracking.</summary>
    public LabelBuilder Heading() { _size = LabelSize.LG; _colorOverride = _theme.Ink.C500; _fontOverride = _theme.Bold; _letterSpacingEm = -0.01f; return this; }

    /// <summary>Bright sub-heading (t-hi, ~13.5px, semi-bold).</summary>
    public LabelBuilder Subheading() { _size = LabelSize.SM; _colorOverride = _theme.Ink.C500; _fontOverride = _theme.SemiBold; return this; }

    /// <summary>Default body copy (t, ~12.5px).</summary>
    public LabelBuilder Body() { _size = LabelSize.MD; return this; }

    /// <summary>Small muted caption (t-lo, ~11.5px).</summary>
    public LabelBuilder Caption() { _size = LabelSize.XS; _colorOverride = _theme.Ink.C200; return this; }

    /// <summary>De-emphasised secondary text (t-lo).</summary>
    public LabelBuilder Muted() => Variant(OrigamiVariant.Subtle);

    /// <summary>UPPERCASE form field label — t-lo, ~11px, wide tracking.</summary>
    public LabelBuilder FieldLabel()
    {
        _size = LabelSize.XS;
        _uppercase = true;
        _colorOverride = _theme.Ink.C200;   // t-lo
        _fontOverride = _theme.SemiBold;     // 600
        _letterSpacingEm = 0.05f;
        return this;
    }

    /// <summary>Accent hyperlink — acc-300, underlined.</summary>
    public LabelBuilder Link()
    {
        _colorOverride = _theme.Primary.C700; // acc-300
        _underline = true;
        _decorColor = _theme.Primary.C700;
        return this;
    }

    /// <summary>Inline code / mono badge — glass-in fill, bd-soft border, bright text.
    /// A true monospace face is unavailable (single UI font); glyphs use the UI font.</summary>
    public LabelBuilder Code()
    {
        _size = LabelSize.XS;
        _colorOverride = _theme.Ink.C500;                   // t-hi
        _heightOverride = ResolveFontSize() + 6f;           // hug like a badge
        _bg = true;
        _bgColor = _theme.Glass;           // glass-in
        _bgRounding = _theme.Metrics.SmallRounding;         // 5
        _border = true;
        _borderColor = _theme.BorderSoft;   // bd-soft
        _borderThickness = 1f;
        _padX = 6f;
        _padY = 1f;
        return this;
    }

    /// <summary>Leading 7px semantic dot in the current (variant) colour.</summary>
    public LabelBuilder Dot(Color? color = null) { _dot = true; _dotColor = color; return this; }

    /// <summary>Per-glyph tracking in em (relative to the resolved font size).</summary>
    public LabelBuilder LetterSpacing(float em) { _letterSpacingEm = em; return this; }

    /// <summary>Leading icon drawn into a reserved <paramref name="width"/>-wide, row-tall cell.</summary>
    public LabelBuilder LeadingIcon(IOrigamiIcon? icon, float width)
    {
        _leadingIconObj = icon;
        _leadingDrawWidth = MathF.Max(1f, width);
        return this;
    }

    // ── Terminator ───────────────────────────────────────────────

    public void Show()
    {
        var font = _fontOverride ?? _theme.Font;
        if (font == null) return;

        float fontSize = ResolveFontSize();
        float letterSpacing = _letterSpacingEm * fontSize;
        Color textColor = ResolveTextColor();
        Color decorColor = _decorColor ?? textColor;
        Color bgColor = _bgColor ?? ResolveBgColor();
        Color borderColor = _borderColor ?? ResolveBorderColor();
        Color insetHi = _insetHighlight ?? _theme.Ink.C600;

        string text = _uppercase ? _text.ToUpperInvariant() : _text;

        // A semantic dot is just a synthesised leading vector-draw (variant-coloured circle).
        Action<Canvas, Rect>? leadingDraw = _leadingDraw;
        float leadingDrawWidth = _leadingDrawWidth;
        if (_leadingIconObj != null && leadingDraw == null)
        {
            var icon = _leadingIconObj;
            leadingDraw = (cv, r) => icon.Draw(cv, r, textColor);
        }
        if (_dot && leadingDraw == null)
        {
            Color dotColor = _dotColor ?? textColor;
            leadingDraw = (cv, r) =>
            {
                float cx = (float)(r.Min.X + r.Size.X * 0.5f);
                float cy = (float)(r.Min.Y + r.Size.Y * 0.5f);
                cv.CircleFilled(cx, cy, MathF.Min((float)r.Size.X, (float)r.Size.Y) * 0.5f, dotColor);
            };
            leadingDrawWidth = 7f;
        }

        // Pre-measure for content-width sizing if no explicit width is given.
        float measuredW = MeasureContentWidth(font, fontSize, text, leadingDrawWidth, letterSpacing);
        float boxWidth = _widthOverride ?? measuredW;
        // Default box height to the theme's row-height metric so a bare
        // Origami.Label(...).Show() matches the editor's row geometry.
        float boxHeight = _heightOverride ?? _theme.Metrics.HeaderHeight;

        var snap = new LabelSnapshot
        {
            Text = text,
            LeadingIcon = _leadingIcon,
            TrailingIcon = _trailingIcon,
            LeadingDraw = leadingDraw,
            LeadingDrawWidth = leadingDrawWidth,
            LetterSpacing = letterSpacing,
            Font = font,
            FontSize = fontSize,
            TextColor = textColor,
            DecorColor = decorColor,
            BgColor = bgColor,
            BorderColor = borderColor,
            ShadowColor = _shadowColor,
            InsetHighlight = insetHi,
            HAlign = _hAlign,
            VAlign = _vAlign,
            Truncation = _truncation,
            PadX = _padX,
            PadY = _padY,
            IconGap = _iconGap,
            HasBg = _bg,
            BgPill = _bgPill,
            BgRounding = _bgRounding ?? _theme.Metrics.Rounding,
            HasBorder = _border,
            BorderThickness = _borderThickness,
            HasUnderline = _underline,
            HasDoubleUnderline = _doubleUnderline,
            HasStrikethrough = _strikethrough,
            DecorThickness = _decorThickness,
            HasShadow = _shadow,
            ShadowDx = _shadowDx,
            ShadowDy = _shadowDy,
            HasInset = _inset,
        };

        var box = _paper.Box(_id)
            .Width(boxWidth)
            .Height(boxHeight);

        bool needsHover = !string.IsNullOrEmpty(_tooltip);
        bool clickable = _onClick != null && !_disabled;

        if (clickable)
        {
            var click = _onClick!;
            box.OnClick(_ => click());
        }
        else if (!needsHover)
        {
            box.IsNotInteractable();
        }

        if (_truncation == LabelTruncation.Clip)
            box.Clip();

        using (box.Enter())
        {
            _paper.Draw((canvas, rect) => Paint(canvas, rect, in snap));
        }

        if (!string.IsNullOrEmpty(_tooltip))
            DrawTooltipOverlay();
    }

    // ── Internal helpers ─────────────────────────────────────────

    private float ResolveFontSize()
    {
        if (_fontSizeOverride.HasValue) return _fontSizeOverride.Value;
        float baseSz = _theme.Metrics.FontSize;
        return _size switch
        {
            LabelSize.XS => baseSz - 2f,   // ~11px caption / field label
            LabelSize.SM => baseSz + 2f,   // ~13.5px sub-heading
            LabelSize.LG => baseSz + 6f,   // ~17px heading
            LabelSize.XL => baseSz + 12f,  // display
            _ => baseSz,                    // ~12.5px body
        };
    }

    private Color ResolveTextColor()
    {
        if (_colorOverride.HasValue) return _colorOverride.Value;
        if (_disabled) return _theme.Ink.C100;                          // t-dim
        if (_variant == OrigamiVariant.Default) return _theme.Ink.C400; // body "t"
        if (_variant == OrigamiVariant.Subtle) return _theme.Ink.C200;  // t-lo
        return _theme.Get(_variant).C500;
    }

    private Color ResolveBgColor()
    {
        if (_variant == OrigamiVariant.Default || _variant == OrigamiVariant.Subtle)
            return _theme.Glass; // glass-in
        return _theme.Get(_variant).C200;
    }

    private Color ResolveBorderColor()
    {
        if (_variant == OrigamiVariant.Default || _variant == OrigamiVariant.Subtle)
            return _theme.BorderSoft; // bd-soft
        return _theme.Get(_variant).C300;
    }

    private float MeasureContentWidth(FontFile font, float fontSize, string text, float leadingDrawWidth, float letterSpacing)
    {
        bool hasLead = leadingDrawWidth > 0f || !string.IsNullOrEmpty(_leadingIcon);
        bool hasTrail = !string.IsNullOrEmpty(_trailingIcon);
        bool hasText = !string.IsNullOrEmpty(text);

        float content = 0f;
        if (leadingDrawWidth > 0f) content += leadingDrawWidth;
        else if (!string.IsNullOrEmpty(_leadingIcon)) content += (float)_paper.MeasureText(_leadingIcon!, fontSize, font).X;
        if (hasText)
        {
            if (hasLead) content += _iconGap;
            content += (float)_paper.MeasureText(text, fontSize, font).X + letterSpacing * MathF.Max(0, text.Length - 1);
        }
        if (hasTrail)
        {
            if (hasLead || hasText) content += _iconGap;
            content += (float)_paper.MeasureText(_trailingIcon!, fontSize, font).X;
        }
        return content + _padX * 2f;
    }

    // ── Paint snapshot (value type, no closure capture of `this`) ──

    private struct LabelSnapshot
    {
        public string Text;
        public string? LeadingIcon;
        public string? TrailingIcon;
        public Action<Canvas, Rect>? LeadingDraw;
        public float LeadingDrawWidth;
        public float LetterSpacing;
        public FontFile Font;
        public float FontSize;

        public Color TextColor;
        public Color DecorColor;
        public Color BgColor;
        public Color BorderColor;
        public Color ShadowColor;
        public Color InsetHighlight;

        public LabelHAlign HAlign;
        public LabelVAlign VAlign;
        public LabelTruncation Truncation;

        public float PadX;
        public float PadY;
        public float IconGap;

        public bool HasBg;
        public bool BgPill;
        public float BgRounding;

        public bool HasBorder;
        public float BorderThickness;

        public bool HasUnderline;
        public bool HasDoubleUnderline;
        public bool HasStrikethrough;
        public float DecorThickness;

        public bool HasShadow;
        public float ShadowDx;
        public float ShadowDy;

        public bool HasInset;
    }

    // ── Canvas paint ─────────────────────────────────────────────

    private static void Paint(Canvas canvas, Rect rect, in LabelSnapshot s)
    {
        float x = (float)rect.Min.X;
        float y = (float)rect.Min.Y;
        float w = (float)rect.Size.X;
        float h = (float)rect.Size.Y;

        // Background fill
        if (s.HasBg)
        {
            float radius = s.BgPill ? h * 0.5f : s.BgRounding;
            canvas.RoundedRectFilled(x, y, w, h, radius, s.BgColor);
        }

        // Border ring (drawn after fill so it sits on top)
        if (s.HasBorder)
        {
            float radius = s.BgPill ? h * 0.5f : s.BgRounding;
            DrawRoundedBorder(canvas, x, y, w, h, radius, s.BorderThickness, s.BorderColor);
        }

        // Measure pieces
        bool hasLeadDraw = s.LeadingDraw != null;
        bool hasLead = hasLeadDraw || !string.IsNullOrEmpty(s.LeadingIcon);
        bool hasTrail = !string.IsNullOrEmpty(s.TrailingIcon);
        bool hasText = !string.IsNullOrEmpty(s.Text);

        Float2 leadSize = hasLeadDraw
            ? new Float2(s.LeadingDrawWidth, s.LeadingDrawWidth)
            : (!string.IsNullOrEmpty(s.LeadingIcon) ? canvas.MeasureText(s.LeadingIcon!, s.FontSize, s.Font) : Float2.Zero);
        Float2 trailSize = hasTrail ? canvas.MeasureText(s.TrailingIcon!, s.FontSize, s.Font) : Float2.Zero;

        float innerLeft = x + s.PadX;
        float innerRight = x + w - s.PadX;
        float innerWidth = MathF.Max(0f, innerRight - innerLeft);

        // Reserve space for icons; remaining width is for the text.
        float reservedForIcons =
            (hasLead ? (float)leadSize.X + (hasText || hasTrail ? s.IconGap : 0f) : 0f)
          + (hasTrail ? (float)trailSize.X + (hasText ? s.IconGap : 0f) : 0f);
        float textBudget = MathF.Max(0f, innerWidth - reservedForIcons);

        // Truncate text if requested.
        string drawnText = s.Text;
        Float2 textSize = Float2.Zero;
        if (hasText)
        {
            textSize = canvas.MeasureText(drawnText, s.FontSize, s.Font, s.LetterSpacing);
            if (s.Truncation == LabelTruncation.Ellipsis && (float)textSize.X > textBudget)
            {
                drawnText = Ellipsize(canvas, drawnText, textBudget, s.FontSize, s.Font, s.LetterSpacing);
                textSize = canvas.MeasureText(drawnText, s.FontSize, s.Font, s.LetterSpacing);
            }
        }

        float contentW =
            (hasLead ? (float)leadSize.X : 0f)
          + (hasText ? (hasLead ? s.IconGap : 0f) + (float)textSize.X : 0f)
          + (hasTrail ? ((hasText || hasLead) ? s.IconGap : 0f) + (float)trailSize.X : 0f);

        // Horizontal start
        float cursor = s.HAlign switch
        {
            LabelHAlign.Center => innerLeft + (innerWidth - contentW) * 0.5f,
            LabelHAlign.Right  => innerRight - contentW,
            _                  => innerLeft,
        };

        // Vertical baseline for text and icons. MeasureText returns the glyph box
        // height; centering on that gives a result that matches PaperUI's own text layout.
        float textTop = ResolveVAlign(s.VAlign, y, h, (float)textSize.Y, s.PadY);
        float leadTop = ResolveVAlign(s.VAlign, y, h, (float)leadSize.Y, s.PadY);
        float trailTop = ResolveVAlign(s.VAlign, y, h, (float)trailSize.Y, s.PadY);

        // Draw leading icon (vector hook takes precedence over a glyph)
        if (hasLead)
        {
            if (hasLeadDraw)
            {
                var cell = new Rect(cursor, leadTop, cursor + (float)leadSize.X, leadTop + (float)leadSize.Y);
                s.LeadingDraw!(canvas, cell);
            }
            else
            {
                DrawTextWithEffects(canvas, s.LeadingIcon!, cursor, leadTop, s.FontSize, s.Font,
                    s.TextColor, 0f, in s);
            }
            cursor += (float)leadSize.X;
            if (hasText || hasTrail) cursor += s.IconGap;
        }

        // Draw text
        if (hasText)
        {
            DrawTextWithEffects(canvas, drawnText, cursor, textTop, s.FontSize, s.Font,
                s.TextColor, s.LetterSpacing, in s);

            // Underline / double underline / strikethrough are positioned relative to the text box.
            float textRight = cursor + (float)textSize.X;
            float textBottom = textTop + (float)textSize.Y;
            float textCenter = textTop + (float)textSize.Y * 0.5f;

            if (s.HasUnderline)
                canvas.RectFilled(cursor, textBottom - s.DecorThickness, (float)textSize.X, s.DecorThickness, s.DecorColor);

            if (s.HasDoubleUnderline)
            {
                canvas.RectFilled(cursor, textBottom - s.DecorThickness, (float)textSize.X, s.DecorThickness, s.DecorColor);
                canvas.RectFilled(cursor, textBottom - s.DecorThickness * 3f, (float)textSize.X, s.DecorThickness, s.DecorColor);
            }

            if (s.HasStrikethrough)
                canvas.RectFilled(cursor, textCenter - s.DecorThickness * 0.5f, (float)textSize.X, s.DecorThickness, s.DecorColor);

            cursor = textRight;
            if (hasTrail) cursor += s.IconGap;
        }

        // Draw trailing icon
        if (hasTrail)
        {
            DrawTextWithEffects(canvas, s.TrailingIcon!, cursor, trailTop, s.FontSize, s.Font,
                s.TextColor, 0f, in s);
        }
    }

    /// <summary>
    /// Draws text with optional shadow and inset passes. The order is: shadow first
    /// (behind), then inset highlight, then the main text on top.
    /// </summary>
    private static void DrawTextWithEffects(Canvas canvas, string text, float x, float y,
        float fontSize, FontFile font, Color color, float letterSpacing, in LabelSnapshot s)
    {
        if (s.HasShadow)
            canvas.DrawText(text, x + s.ShadowDx, y + s.ShadowDy, s.ShadowColor, fontSize, font, letterSpacing);

        if (s.HasInset)
            canvas.DrawText(text, x + 1f, y + 1f, s.InsetHighlight, fontSize, font, letterSpacing);

        canvas.DrawText(text, x, y, color, fontSize, font, letterSpacing);
    }

    private static float ResolveVAlign(LabelVAlign align, float y, float h, float contentH, float padY)
    {
        return align switch
        {
            LabelVAlign.Top    => y + padY,
            LabelVAlign.Bottom => y + h - padY - contentH,
            _                  => y + (h - contentH) * 0.5f,
        };
    }

    private static void DrawRoundedBorder(Canvas canvas, float x, float y, float w, float h,
        float radius, float thickness, Color color)
    {
        canvas.BeginPath();
        canvas.RoundedRect(x, y, w, h, radius);
        canvas.SetStrokeColor(new Color32(color.R, color.G, color.B, color.A));
        canvas.SetStrokeWidth(thickness);
        canvas.Stroke();
    }

    private static string Ellipsize(Canvas canvas, string text, float maxWidth, float fontSize, FontFile font, float letterSpacing)
    {
        if (maxWidth <= 0f || string.IsNullOrEmpty(text)) return string.Empty;
        const string ell = "...";
        float ellW = (float)canvas.MeasureText(ell, fontSize, font, letterSpacing).X;
        if (ellW > maxWidth) return string.Empty;

        // Binary search the longest prefix that fits with the ellipsis appended.
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            float w = (float)canvas.MeasureText(text.Substring(0, mid) + ell, fontSize, font, letterSpacing).X;
            if (w <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? ell : text.Substring(0, lo) + ell;
    }

    // ── Tooltip ──────────────────────────────────────────────────

    private void DrawTooltipOverlay()
    {
        var thandle = _paper.CurrentParent;
        string tt = _tooltip!;
        var font = _theme.Font!;
        float fontSize = _theme.Metrics.FontSize - 1f;
        Color ttBg = _theme.Neutral.C500;
        Color ttFg = _theme.Ink.C500;
        string ttId = _id;

        using (_paper.Box($"{_id}_tt")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Width(1).Height(1)
            .Layer(Layer.Topmost)
            .HookToParent()
            .IsNotInteractable()
            .Enter())
        {
            bool wantTooltip = _paper.IsElementHovered(thandle.Data.ID);
            float ttAnim = _paper.AnimateBool(wantTooltip, 0.16f, id: $"{ttId}_ttf");
            if (ttAnim < 0.01f) return;

            _paper.Draw((canvas, _) =>
            {
                var tr = thandle.Data.LayoutRect;
                float trX = (float)tr.Min.X;
                float trY = (float)tr.Min.Y;
                float trW = (float)tr.Size.X;

                var ts = canvas.MeasureText(tt, fontSize, font);
                float padX = 6f, padY = 2f;
                float bw = (float)ts.X + padX * 2f;
                float bh = (float)ts.Y + padY * 2f;
                float slide = (1f - ttAnim) * 4f;
                float bx = trX + trW * 0.5f - bw * 0.5f;
                float by = trY - bh - 6f + slide;

                byte aShadow = (byte)Math.Clamp((int)(80 * ttAnim), 0, 255);
                byte aBody = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);
                byte aText = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);

                canvas.RoundedRectFilled(bx + 1f, by + 2f, bw, bh, 3f,
                    Color.FromArgb(aShadow, 0, 0, 0));
                canvas.RoundedRectFilled(bx, by, bw, bh, 3f,
                    Color.FromArgb(aBody, ttBg.R, ttBg.G, ttBg.B));
                canvas.DrawText(tt, bx + padX, by + padY,
                    Color.FromArgb(aText, ttFg.R, ttFg.G, ttFg.B), fontSize, font);
            });
        }
    }
}
