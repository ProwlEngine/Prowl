using System;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;
using Prowl.Vector;
namespace Prowl.PaperUI
{
    public static class TextMeasurement
    {
        private static Float2 Memo(ref ElementData element, Float2 size, float availableWidth, bool widthIndependent)
        {
            element._textMemoSize = size;
            element._textMemoWidth = availableWidth;
            element._textMemoWidthIndependent = widthIndependent;
            element._textMemoValid = true;
            return size;
        }

        internal static Float2 ProcessText(this ref ElementData element, Paper gui, float availableWidth)
        {
            if (string.IsNullOrWhiteSpace(element.Paragraph)) return Float2.Zero;

            // Per-frame memo: this runs several times per frame (stretch resolution) plus once at
            // render. Once we've computed the size this frame, reuse it while the width still applies
            // (width-independent plain text always applies; width-dependent text must match). The
            // cached _textLayout/_richText set on the first compute stay valid on
            // the same per-frame ElementData, so the render pass still finds them.
            if (element._textMemoValid
                && (element._textMemoWidthIndependent || element._textMemoWidth == availableWidth))
                return element._textMemoSize;

            element.ProcessedText = true;

            var canvas = gui.Canvas;
            if (canvas == null) throw new InvalidOperationException("Canvas is not set.");

            // TextLayout/markdown sizes returned by the canvas are in pixel space
            // (scaled by FramebufferScale). The layout engine works in logical units.
            float invScale = 1.0f / canvas.FramebufferScale;

            if (element.DrawsRichText)
            {
                // Everything the canvas lays out is in physical pixels, so scale in and back out.
                float scale = canvas.FramebufferScale;
                bool wraps = element.WrapMode == TextWrapMode.Wrap;
                var alignment = ToScribeAlignment(element.TextAlignment);
                // The width only matters to wrapping and to centred or right aligned text. Leaving it
                // out otherwise keeps the block from laying out again every time the element resizes.
                bool dependsOnWidth = wraps || alignment != Scribe.TextAlignment.Left;
                bool usesWidth = dependsOnWidth && availableWidth > 0f && float.IsFinite(availableWidth);
                var rich = new RichText.RichTextSettings {
                    Regular = element.Font,
                    Bold = element.FontBold,
                    Italic = element.FontItalic,
                    BoldItalic = element.FontBoldItalic,
                    Mono = element.FontMono,
                    PixelSize = element._elementStyle.GetFontSize() * scale,
                    Quality = element._elementStyle.GetTextQuality(),
                    LineHeight = element._elementStyle.GetLineHeight(),
                    LetterSpacing = element._elementStyle.GetLetterSpacing() * scale,
                    WordSpacing = element._elementStyle.GetWordSpacing() * scale,
                    TabSize = element._elementStyle.GetTabSize(),
                    Color = element._elementStyle.GetTextColor(),
                    WrapMode = element.WrapMode,
                    MaxWidth = usesWidth ? availableWidth * scale : 0f,
                    Alignment = alignment,
                };

                var block = gui.GetElementStorageById<RichText.RichTextBlock>(element.ID, Paper.RichTextBlockKey, null);
                if (block == null)
                {
                    block = new RichText.RichTextBlock();
                    gui.SetElementStorageById(element.ID, Paper.RichTextBlockKey, block);
                }

                block.Update(element.Paragraph, rich, canvas.Text.FontEngine);
                element._richText = block;

                return Memo(ref element, block.Size * invScale, availableWidth, !dependsOnWidth);
            }

            var settings = TextLayoutSettings.Default;

            settings.WordSpacing = element._elementStyle.GetWordSpacing();
            settings.LetterSpacing = element._elementStyle.GetLetterSpacing();
            settings.LineHeight = element._elementStyle.GetLineHeight();
            settings.TabSize = element._elementStyle.GetTabSize();
            settings.PixelSize = element._elementStyle.GetFontSize();
            settings.Quality = element._elementStyle.GetTextQuality();

            settings.Alignment = ToScribeAlignment(element.TextAlignment);

            settings.Font = element.Font;
            settings.WrapMode = element.WrapMode;
            settings.MaxWidth = availableWidth;
            settings.Customizer = TextMask.For(element.MaskChar);
            gui.ApplyTextDefaults(ref settings);

            // Left aligned text with no wrap or truncation lays out the same at any element width,
            // so its key leaves the width out and a resize never lays it out again.
            bool widthIndependent = settings.Alignment == Scribe.TextAlignment.Left
                && element.WrapMode == TextWrapMode.NoWrap && !element.Truncate;
            bool truncates = element.Truncate && element.WrapMode != TextWrapMode.Wrap && availableWidth > 0f;

            float fbScale = canvas.FramebufferScale;
            var key = new PlainTextKey(element.Paragraph, settings, fbScale,
                widthIndependent ? 0f : availableWidth, truncates);
            var cache = gui.GetTextCache(element.ID);

            var layout = cache.Find(key);
            if (layout == null)
            {
                var fonts = canvas.Text.FontEngine;
                string text = element.Paragraph;

                // Single-line truncation: if the text overflows the element, drop trailing characters
                // and append an ellipsis that is guaranteed to fit.
                if (truncates)
                {
                    var measure = settings;
                    measure.WrapMode = TextWrapMode.NoWrap;
                    measure.MaxWidth = 0f;
                    measure = Scaled(measure, fbScale);
                    var scratch = gui._textScratch;

                    float LogicalW(string s)
                    {
                        fonts.UpdateLayout(scratch, s, measure);
                        return scratch.Size.X / fbScale;
                    }

                    if (LogicalW(text) > availableWidth)
                    {
                        const string ell = "...";
                        float ellW = LogicalW(ell);
                        // Binary-search the longest prefix whose width + ellipsis still fits.
                        int lo = 0, hi = text.Length;
                        while (lo < hi)
                        {
                            int mid = (lo + hi + 1) / 2;
                            if (LogicalW(text[..mid]) + ellW <= availableWidth) lo = mid;
                            else hi = mid - 1;
                        }
                        text = lo > 0 ? text[..lo] + ell : ell;
                    }
                }

                layout = cache.Take(key, !widthIndependent);
                fonts.UpdateLayout(layout, text, Scaled(settings, fbScale));
            }

            element._textLayout = layout;
            return Memo(ref element, layout.Size * invScale, availableWidth, widthIndependent);
        }

        // Logical units to the physical pixels the font engine works in, as Canvas.CreateLayout does.
        internal static TextLayoutSettings Scaled(TextLayoutSettings settings, float scale)
        {
            settings.PixelSize *= scale;
            settings.LetterSpacing *= scale;
            settings.WordSpacing *= scale;
            if (settings.MaxWidth > 0)
                settings.MaxWidth *= scale;
            return settings;
        }

        private static Scribe.TextAlignment ToScribeAlignment(TextAlignment a)
        {
            if (a == TextAlignment.Left || a == TextAlignment.MiddleLeft || a == TextAlignment.BottomLeft)
                return Scribe.TextAlignment.Left;
            if (a == TextAlignment.Center || a == TextAlignment.MiddleCenter || a == TextAlignment.BottomCenter)
                return Scribe.TextAlignment.Center;
            if (a == TextAlignment.Right || a == TextAlignment.MiddleRight || a == TextAlignment.BottomRight)
                return Scribe.TextAlignment.Right;
            return Scribe.TextAlignment.Left;
        }
    }

    /// <summary>
    /// The plain text layouts an element owns. Text that depends on the element width keeps two,
    /// since layout can measure it at one width and then place it at another every frame.
    /// </summary>
    internal sealed class TextLayoutCache
    {
        private TextLayout? _layout, _other;
        private PlainTextKey _key, _otherKey;

        public TextLayout? Find(in PlainTextKey key)
        {
            if (_layout != null && _key.Equals(key)) return _layout;
            if (_other == null || !_otherKey.Equals(key)) return null;

            Swap();
            return _layout;
        }

        /// <summary>The layout to rebuild for <paramref name="key"/>. With <paramref name="keepCurrent"/> the oldest one is given up.</summary>
        public TextLayout Take(in PlainTextKey key, bool keepCurrent)
        {
            if (keepCurrent) Swap();
            _key = key;
            return _layout ??= new TextLayout();
        }

        private void Swap()
        {
            (_layout, _other) = (_other, _layout);
            (_key, _otherKey) = (_otherKey, _key);
        }
    }

    /// <summary>Fingerprint of everything that decides a plain text layout. A mismatch rebuilds it.</summary>
    internal readonly struct PlainTextKey : IEquatable<PlainTextKey>
    {
        readonly string Text;
        readonly float PixelSize, LetterSpacing, WordSpacing, LineHeight, FbScale, Width;
        readonly int TabSize;
        readonly FontQuality Quality;
        readonly FontFile? Font;
        readonly GlyphCustomizer? Customizer;
        readonly bool NoLigatures, Truncated;
        readonly TextWrapMode Wrap;
        readonly Scribe.TextAlignment Alignment;

        // Width is 0 for text whose layout does not depend on the element width.
        public PlainTextKey(string text, in TextLayoutSettings s, float fbScale, float width, bool truncated)
        {
            Text = text;
            PixelSize = s.PixelSize;
            LetterSpacing = s.LetterSpacing;
            WordSpacing = s.WordSpacing;
            LineHeight = s.LineHeight;
            TabSize = s.TabSize;
            Quality = s.Quality;
            Font = s.Font;
            Customizer = s.Customizer;
            NoLigatures = s.NoLigatures;
            Wrap = s.WrapMode;
            Alignment = s.Alignment;
            FbScale = fbScale;
            Width = width;
            Truncated = truncated;
        }

        public bool Equals(PlainTextKey o) =>
            ReferenceEquals(Font, o.Font) && ReferenceEquals(Customizer, o.Customizer)
            && TabSize == o.TabSize && Quality == o.Quality && NoLigatures == o.NoLigatures
            && Wrap == o.Wrap && Alignment == o.Alignment && Truncated == o.Truncated
            && PixelSize == o.PixelSize && LetterSpacing == o.LetterSpacing && WordSpacing == o.WordSpacing
            && LineHeight == o.LineHeight && FbScale == o.FbScale && Width.Equals(o.Width)
            && (ReferenceEquals(Text, o.Text) || string.Equals(Text, o.Text));

        public override bool Equals(object? obj) => obj is PlainTextKey o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(Text, PixelSize, TabSize, (int)Quality, Font, FbScale, Width);
    }
}
