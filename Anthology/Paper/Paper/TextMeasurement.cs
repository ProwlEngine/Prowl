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
            //if (element.ProcessedText) return Vector2.zero;

            if (string.IsNullOrWhiteSpace(element.Paragraph)) return Float2.Zero;

            // Per-frame memo: this runs several times per frame (stretch resolution) plus once at
            // render. Once we've computed the size this frame, reuse it while the width still applies
            // (width-independent plain text always applies; width-dependent text must match). The
            // cached _textLayout/_quillRichText/_quillMarkdown set on the first compute stay valid on
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

            if (element.IsRichText)
            {
                var settings = new RichTextLayoutSettings {
                    RegularFont = element.Font,
                    BoldFont = element.FontBold,
                    ItalicFont = element.FontItalic,
                    BoldItalicFont = element.FontBoldItalic,
                    MonoFont = element.FontMono,
                    PixelSize = element._elementStyle.GetFontSize(),
                    Quality = element._elementStyle.GetTextQuality(),
                    LineHeight = element._elementStyle.GetLineHeight(),
                    LetterSpacing = element._elementStyle.GetLetterSpacing(),
                    WordSpacing = element._elementStyle.GetWordSpacing(),
                    TabSize = element._elementStyle.GetTabSize(),
                    WrapMode = element.WrapMode,
                    MaxWidth = element.WrapMode == TextWrapMode.Wrap ? availableWidth : 0f,
                    Alignment = ToScribeAlignment(element.TextAlignment),
                };

                // Reuse the cached layout when source + width haven't changed so the layout's
                // animation start time (set on first Draw) survives across frames.
                var cached = gui.GetElementStorageById<Quill.Canvas.QuillRichText?>(element.ID, Paper.RichTextLayoutKey, null);
                var cachedSrc = gui.GetElementStorageById<string>(element.ID, Paper.RichTextSourceKey, null);
                float cachedWidth = gui.GetElementStorageById<float>(element.ID, Paper.RichTextWidthKey, -1f);

                Quill.Canvas.QuillRichText richText;
                if (cached.HasValue && cachedSrc == element.Paragraph && cachedWidth == settings.MaxWidth)
                {
                    richText = cached.Value;
                }
                else
                {
                    richText = canvas.CreateRichText(element.Paragraph, settings);
                    gui.SetElementStorageById<Quill.Canvas.QuillRichText?>(element.ID, Paper.RichTextLayoutKey, richText);
                    gui.SetElementStorageById<string>(element.ID, Paper.RichTextSourceKey, element.Paragraph);
                    gui.SetElementStorageById<float>(element.ID, Paper.RichTextWidthKey, settings.MaxWidth);
                }
                element._quillRichText = richText;

                // QuillRichText.Size is already in logical units. Width matters only when wrapping.
                return Memo(ref element, richText.Size, availableWidth, element.WrapMode != TextWrapMode.Wrap);
            }

            if (element.IsMarkdown == false)
            {
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

                // Per-element cache for width-independent text: left-aligned, no-wrap, non-truncated
                // text lays out identically regardless of the element width (MaxWidth only affects
                // wrapping and center/right alignment). Keying purely on content + font/metrics lets
                // us reuse the same TextLayout across frames and across the measure/draw passes,
                // surviving Scribe's global LRU cap (a long scrolling list of distinct labels would
                // otherwise thrash it). Width-dependent text (wrap / truncate / center / right) keeps
                // going straight through CreateLayout so its width changes are always honoured.
                if (settings.Alignment == Scribe.TextAlignment.Left
                    && element.WrapMode == TextWrapMode.NoWrap && !element.Truncate)
                {
                    var ptKey = new PlainTextKey(element.Paragraph, settings, canvas.FramebufferScale);
                    var cachedLayout = gui.GetElementStorageById<TextLayout>(element.ID, Paper.PlainTextLayoutKey, null);
                    var cachedKey = gui.GetElementStorageById<PlainTextKey>(element.ID, Paper.PlainTextKeyKey, default);

                    if (cachedLayout != null && cachedKey.Equals(ptKey))
                    {
                        element._textLayout = cachedLayout;
                        return Memo(ref element, (Float2)cachedLayout.Size * invScale, availableWidth, true);
                    }

                    element._textLayout = canvas.CreateLayout(element.Paragraph, settings);
                    gui.SetElementStorageById<TextLayout>(element.ID, Paper.PlainTextLayoutKey, element._textLayout);
                    gui.SetElementStorageById<PlainTextKey>(element.ID, Paper.PlainTextKeyKey, ptKey);
                    return Memo(ref element, (Float2)element._textLayout.Size * invScale, availableWidth, true);
                }

                string text = element.Paragraph;

                // Single-line truncation: if the text overflows the element, drop trailing characters
                // and append an ellipsis that is guaranteed to fit. The result depends on the element
                // width, so it uses a width-keyed cache (the width-independent cache above skips
                // truncated text) to avoid re-running the binary search + layout every frame.
                if (element.Truncate && element.WrapMode != TextWrapMode.Wrap && availableWidth > 0f)
                {
                    var trKey = new PlainTextKey(element.Paragraph, settings, canvas.FramebufferScale, availableWidth);
                    var cachedLayout = gui.GetElementStorageById<TextLayout>(element.ID, Paper.TruncTextLayoutKey, null);
                    var cachedKey = gui.GetElementStorageById<PlainTextKey>(element.ID, Paper.TruncTextKeyKey, default);
                    if (cachedLayout != null && cachedKey.Equals(trKey))
                    {
                        element._textLayout = cachedLayout;
                        return Memo(ref element, (Float2)cachedLayout.Size * invScale, availableWidth, false);
                    }

                    var measure = settings;
                    measure.WrapMode = TextWrapMode.NoWrap;
                    measure.MaxWidth = 0f;
                    float LogicalW(string s) => canvas.MeasureText(s, measure).X * invScale;

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

                    element._textLayout = canvas.CreateLayout(text, settings);
                    gui.SetElementStorageById<TextLayout>(element.ID, Paper.TruncTextLayoutKey, element._textLayout);
                    gui.SetElementStorageById<PlainTextKey>(element.ID, Paper.TruncTextKeyKey, trKey);
                    return Memo(ref element, (Float2)element._textLayout.Size * invScale, availableWidth, false);
                }

                element._textLayout = canvas.CreateLayout(text, settings);

                return Memo(ref element, (Float2)element._textLayout.Size * invScale, availableWidth, false);
            }
            else
            {
                var r = element.Font;
                var m = element.FontMono;
                var b = element.FontBold;
                var i = element.FontItalic;
                var bi = element.FontBoldItalic;
                var settings = MarkdownLayoutSettings.Default(r, availableWidth, m, b, i, bi);
                settings.Quality = element._elementStyle.GetTextQuality();

                element._quillMarkdown = canvas.CreateMarkdown(element.Paragraph, settings);

                var markdownResult = element._quillMarkdown;
                return Memo(ref element, (markdownResult?.Size ?? Float2.Zero) * invScale, availableWidth, false);
            }
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
    /// Fingerprint of the inputs that determine a width-independent plain-text layout. Stored
    /// alongside the cached <see cref="TextLayout"/> in element storage; a mismatch rebuilds it.
    /// Width, wrap, truncation and alignment are intentionally absent: this key is only used for
    /// left-aligned, non-wrapping, non-truncating text, whose layout does not depend on them.
    /// </summary>
    internal readonly struct PlainTextKey : IEquatable<PlainTextKey>
    {
        readonly string Text;
        readonly float PixelSize, LetterSpacing, WordSpacing, LineHeight, FbScale, Width;
        readonly int TabSize;
        readonly FontQuality Quality;
        readonly FontFile Font;
        readonly GlyphCustomizer Customizer;

        // Width is only used by the width-dependent (truncation) cache; the width-independent cache
        // leaves it at 0.
        public PlainTextKey(string text, in TextLayoutSettings s, float fbScale, float width = 0f)
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
            FbScale = fbScale;
            Width = width;
        }

        public bool Equals(PlainTextKey o) =>
            ReferenceEquals(Font, o.Font) && ReferenceEquals(Customizer, o.Customizer)
            && TabSize == o.TabSize && Quality == o.Quality
            && PixelSize == o.PixelSize && LetterSpacing == o.LetterSpacing && WordSpacing == o.WordSpacing
            && LineHeight == o.LineHeight && FbScale == o.FbScale && Width == o.Width
            && (ReferenceEquals(Text, o.Text) || string.Equals(Text, o.Text));

        public override bool Equals(object obj) => obj is PlainTextKey o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(Text, PixelSize, TabSize, (int)Quality, Font, FbScale, Width);
    }
}
