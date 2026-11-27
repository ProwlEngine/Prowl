using System;

using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.RichText;

/// <summary>Fonts and metrics a rich text block is shaped with.</summary>
public struct RichTextSettings
{
    public FontFile? Regular;
    public FontFile? Bold;
    public FontFile? Italic;
    public FontFile? BoldItalic;
    public FontFile? Mono;
    public float PixelSize;
    public float LineHeight;
    public float LetterSpacing;
    public float WordSpacing;
    public int TabSize;
    public FontQuality Quality;
    public Color Color;
    public float MaxWidth;
    public TextWrapMode WrapMode;
    public Prowl.Scribe.TextAlignment Alignment;

    public static RichTextSettings Default(FontFile? font, float pixelSize, Color color) => new()
    {
        Regular = font,
        PixelSize = pixelSize,
        LineHeight = 1f,
        TabSize = 4,
        Quality = FontQuality.Normal,
        Color = color,
        WrapMode = TextWrapMode.NoWrap,
        Alignment = Prowl.Scribe.TextAlignment.Left
    };
}

/// <summary>
/// A block of rich text.
///
/// Parsing and effect evaluation live here, and everything about *text* stays in Scribe. The block
/// tells Scribe each character's font, size and decoration through a <see cref="GlyphCustomizer"/>,
/// lets Scribe lay the text out, and then adjusts each glyph on the way to the renderer through a
/// <see cref="GlyphModifier"/>. That keeps kerning, ligatures, line breaking, alignment and
/// underline/strike decorations in the one place that already knows how to do them.
/// </summary>
public sealed class RichTextBlock
{
    private readonly RichTextParser _parser = new();
    private readonly TextLayout _layout = new();
    private readonly GlyphCustomizer _customizer;
    private readonly GlyphModifier _modifier;

    private string _source = string.Empty;
    private RichTextSettings _settings;
    private bool _built;
    private float _time;

    public RichTextBlock()
    {
        // Cached delegates: these are handed to Scribe every frame and must not allocate.
        _customizer = Customize;
        _modifier = Modify;
    }

    public string VisibleText => _parser.VisibleText;
    public TextLayout Layout => _layout;
    public Float2 Size => _layout.Size;

    /// <summary>False when the markup is styled but never moves, so a frame can skip effect work.</summary>
    public bool IsAnimated => _parser.HasAnimatedEffects;

    /// <summary>Parses and lays out again only when something that moves a glyph actually changed.</summary>
    public void Update(string source, in RichTextSettings settings, FontSystem fonts)
    {
        bool sourceChanged = !string.Equals(_source, source, StringComparison.Ordinal);
        bool settingsChanged = !SameGeometry(_settings, settings);

        if (!sourceChanged && !settingsChanged && _built)
        {
            // Colour is not geometry, so it changes without a new layout but still has to land.
            _settings.Color = settings.Color;
            // Scribe still lays out again on its own if the atlas was rebuilt under us.
            _layout.EnsureUpToDate(fonts);
            return;
        }

        if (sourceChanged || !_built)
        {
            _parser.Parse(source ?? string.Empty);
            _source = source ?? string.Empty;
        }

        _settings = settings;

        var text = new TextLayoutSettings
        {
            Font = settings.Regular,
            PixelSize = settings.PixelSize,
            LetterSpacing = settings.LetterSpacing,
            WordSpacing = settings.WordSpacing,
            LineHeight = settings.LineHeight,
            TabSize = settings.TabSize,
            WrapMode = settings.WrapMode,
            Alignment = settings.Alignment,
            MaxWidth = settings.MaxWidth,
            Quality = settings.Quality,
            // Font, size and decoration for each character, so a bold or resized run shapes, kerns and
            // lines up as itself rather than being stretched afterwards.
            Customizer = _parser.Styles.Count > 0 || _parser.Sizes.Count > 0 ? _customizer : null
        };

        fonts.UpdateLayout(_layout, _parser.VisibleText, text);
        _built = true;
    }

    /// <summary>Everything that changes where a glyph lands. Colour is absent on purpose: it is
    /// applied at draw time, so recolouring text never lays it out again.</summary>
    private static bool SameGeometry(in RichTextSettings a, in RichTextSettings b) =>
        ReferenceEquals(a.Regular, b.Regular)
        && ReferenceEquals(a.Bold, b.Bold)
        && ReferenceEquals(a.Italic, b.Italic)
        && ReferenceEquals(a.BoldItalic, b.BoldItalic)
        && ReferenceEquals(a.Mono, b.Mono)
        && a.PixelSize == b.PixelSize
        && a.LineHeight == b.LineHeight
        && a.LetterSpacing == b.LetterSpacing
        && a.WordSpacing == b.WordSpacing
        && a.TabSize == b.TabSize
        && a.Quality == b.Quality
        && a.MaxWidth == b.MaxWidth
        && a.WrapMode == b.WrapMode
        && a.Alignment == b.Alignment;

    private void Customize(ref GlyphStyle glyph)
    {
        var style = StyleForIndex(glyph.CharIndex);
        glyph.Font = FontFor(style);
        glyph.PixelSize = SizeForIndex(glyph.CharIndex);
        glyph.Underline = (style & RichStyle.Underline) != 0;
        glyph.Strikethrough = (style & RichStyle.Strike) != 0;
    }

    private RichStyle StyleForIndex(int index)
    {
        var style = RichStyle.None;
        var spans = _parser.Styles;
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Covers(index))
            {
                style |= spans[i].Style;
            }
        }

        return style;
    }

    private FontFile FontFor(RichStyle style)
    {
        if ((style & RichStyle.Mono) != 0 && _settings.Mono != null)
        {
            return _settings.Mono;
        }

        bool bold = (style & RichStyle.Bold) != 0;
        bool italic = (style & RichStyle.Italic) != 0;
        if (bold && italic)
        {
            return _settings.BoldItalic ?? _settings.Bold ?? _settings.Italic ?? _settings.Regular!;
        }

        if (bold)
        {
            return _settings.Bold ?? _settings.Regular!;
        }

        if (italic)
        {
            return _settings.Italic ?? _settings.Regular!;
        }

        return _settings.Regular!;
    }

    private float SizeForIndex(int index)
    {
        float scale = 1f;
        var spans = _parser.Sizes;
        // Nested sizes multiply, so <size 2> around <size 0.5> is back to the base.
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Covers(index))
            {
                scale *= spans[i].Scale;
            }
        }

        return _settings.PixelSize * scale;
    }

    private Color ColorForIndex(int index)
    {
        var color = _settings.Color;
        var spans = _parser.Colors;
        // The innermost tag wins. It starts last, and when two start together it closes first,
        // which puts it earlier in the list.
        int innermost = int.MinValue;
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Covers(index) && spans[i].Start > innermost)
            {
                innermost = spans[i].Start;
                color = spans[i].Color;
            }
        }

        return color;
    }

    /// <summary>The link at a character index, or null.</summary>
    public string? LinkAtIndex(int index)
    {
        var spans = _parser.Links;
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Covers(index))
            {
                return spans[i].Href;
            }
        }

        return null;
    }

    /// <summary>The link under a point relative to the block, using Scribe's own hit testing.</summary>
    public string? LinkAt(Float2 point)
    {
        if (_parser.Links.Count == 0)
        {
            return null;
        }

        // A caret sits between two characters, so the one under the point is whichever neighbour
        // actually contains it. Empty space past the end of a line contains neither.
        int caret = _layout.GetCursorIndex(point);
        int length = _parser.VisibleText.Length;
        for (int i = caret - 1; i <= caret; i++)
        {
            if (i < 0 || i >= length) continue;

            var rect = _layout.GetCharacterRect(i);
            if (rect.Width > 0f
                && point.X >= rect.X && point.X < rect.X + rect.Width
                && point.Y >= rect.Y && point.Y < rect.Y + rect.Height)
            {
                return LinkAtIndex(i);
            }
        }

        return null;
    }

    /// <summary>Draw the block, evaluating effects at <paramref name="time"/> seconds.</summary>
    public void Draw(FontSystem fonts, Float2 position, float time)
    {
        _time = time;
        var color = ToFontColor(_settings.Color);
        bool needsModifier = _parser.HasAnimatedEffects || _parser.Colors.Count > 0;
        fonts.DrawLayout(_layout, position, color, needsModifier ? _modifier : null);
    }

    private void Modify(ref GlyphDraw glyph)
    {
        var color = ColorForIndex(glyph.CharIndex);

        // A bar spans many glyphs, so it takes its run's colour but none of the motion each glyph has.
        if (glyph.IsDecoration)
        {
            glyph.Color = ToFontColor(color);
            return;
        }

        if (!_parser.HasAnimatedEffects)
        {
            glyph.Color = ToFontColor(color);
            return;
        }

        var fx = RichTextEffects.Evaluate(glyph.CharIndex, _parser.Effects, _time, glyph.PixelSize, color);
        glyph.Color = ToFontColor(fx.Color);

        if (fx.IsIdentity)
        {
            return;
        }

        var centre = glyph.Centre;
        float width = glyph.Width;
        float height = glyph.Height;

        fx.Apply(0f, 0f, width, height, out float ax, out float ay);
        fx.Apply(1f, 0f, width, height, out float bx, out float by);
        fx.Apply(0f, 1f, width, height, out float cx, out float cy);
        fx.Apply(1f, 1f, width, height, out float dx, out float dy);

        glyph.SetCorners(
            new Float2(centre.X + ax, centre.Y + ay),
            new Float2(centre.X + bx, centre.Y + by),
            new Float2(centre.X + cx, centre.Y + cy),
            new Float2(centre.X + dx, centre.Y + dy));
    }

    private static FontColor ToFontColor(Color c) =>
        new((byte)Math.Clamp(c.R * 255f, 0f, 255f),
            (byte)Math.Clamp(c.G * 255f, 0f, 255f),
            (byte)Math.Clamp(c.B * 255f, 0f, 255f),
            (byte)Math.Clamp(c.A * 255f, 0f, 255f));
}
