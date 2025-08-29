// MarkdownLayoutEngine — builds and renders a Markdown AST
// Shapes/lines are emitted as quads sampling the atlas at UV(0,0) (white texel).
//
// Usage:
//   var dl = MarkdownLayoutEngine.Layout(doc, fontSystem, settings, imageProvider);
//   MarkdownLayoutEngine.Render(dl, fontSystem, renderer, new Vector2(x, y)); // draws shapes + text

using Prowl.Scribe.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Prowl.Scribe
{
    #region Support types

    public interface IMarkdownImageProvider
    {
        bool TryGetImage(string url, out object texture, out Vector2 size);
    }

    public enum DecorationKind { Underline, Strike, Overline }

    public struct DecorationSpan
    {
        public int CharStart, CharEnd; // [start,end) in flattened string
        public DecorationKind Kind;
    }

    public interface IDrawOp { }

    public struct RectangleF
    {
        public float X, Y, Width, Height;
        public RectangleF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; }
    }

    public struct DrawText : IDrawOp
    {
        public TextLayout Layout;
        public Vector2 Pos;
        public FontColor Color;
        public List<DecorationSpan> Decorations; // optional
        public List<IntRange> LinkRanges;
    }

    public struct DrawQuad : IDrawOp
    {
        public RectangleF Rect;
        public FontColor Color;
    }

    public struct DrawImage : IDrawOp
    {
        public RectangleF Rect;
        public object Texture;
    }

    public struct IntRange { public int Start, End; public IntRange(int s, int e) { Start = s; End = e; } }

    public struct LinkSpan
    {
        public IntRange Range;
        public string Href;
        public LinkSpan(IntRange r, string href) { Range = r; Href = href; }
    }

    public struct LinkInfo
    {
        public RectangleF Rect;
        public string Href;
        public LinkInfo(RectangleF rect, string href) { Rect = rect; Href = href; }
    }

    public struct StyleSpan
    {
        public int Start, End;
        public bool Bold, Italic;
        public StyleSpan(int s, int e, bool b, bool i) { Start = s; End = e; Bold = b; Italic = i; }
    }

    public struct MarkdownLayoutSettings
    {
        public float Width;                  // content width in pixels
        public float BaseSize;               // default font size
        public float LineHeight;             // default line height multiplier
        public float ParagraphSpacing;       // space after paragraphs/blocks
        public float Heading1Scale, Heading2Scale, Heading3Scale;
        public float BlockQuoteBarWidth;
        public float BlockQuoteIndent;
        public float ListIndent;             // per nesting level
        public float BulletRadius;           // unordered bullet size
        public float HrThickness;
        public float HrSpacing;              // top/bottom margins around HR
        public float CodePadding;            // padding inside code block bg

        public FontFile ParagraphFont;       // main font
        public FontFile MonoFont;            // code font

        public FontColor ColorText;
        public FontColor ColorMutedText;
        public FontColor ColorRule;
        public FontColor ColorQuoteBar;
        public FontColor ColorCodeBg;        // used as solid quad color
        public FontColor ColorLink;

        public static MarkdownLayoutSettings Default(FontFile textFont, FontFile monoFont, float width)
        {
            return new MarkdownLayoutSettings {
                Width = width,
                BaseSize = 18f,
                LineHeight = 1.2f,
                ParagraphSpacing = 8f,
                Heading1Scale = 1.6f,
                Heading2Scale = 1.35f,
                Heading3Scale = 1.2f,
                BlockQuoteBarWidth = 4f,
                BlockQuoteIndent = 10f,
                ListIndent = 28f,
                BulletRadius = 3f,
                HrThickness = 2f,
                HrSpacing = 8f,
                CodePadding = 8f,
                ParagraphFont = textFont,
                MonoFont = monoFont,
                ColorText = new FontColor(255, 255, 255, 255),
                ColorMutedText = new FontColor(210, 210, 210, 255),
                ColorRule = new FontColor(180, 180, 180, 255),
                ColorQuoteBar = new FontColor(160, 160, 160, 255),
                ColorCodeBg = new FontColor(40, 40, 40, 255),
                ColorLink = new FontColor(0, 122, 255, 255)
            };
        }
    }

    public sealed class MarkdownDisplayList
    {
        public readonly List<IDrawOp> Ops = new List<IDrawOp>();
        public readonly List<LinkInfo> Links = new List<LinkInfo>();
        public Vector2 Size; // overall width/height used
    }

    #endregion

    public static class MarkdownLayoutEngine
    {
        #region Public API

        public static MarkdownDisplayList Layout(Document doc, FontSystem fontSystem, MarkdownLayoutSettings settings, IMarkdownImageProvider? imageProvider = null)
        {
            var dl = new MarkdownDisplayList();
            float cursorY = 0;
            float maxRight = 0;

            foreach (var block in doc.Blocks)
            {
                switch (block.Kind)
                {
                    case BlockKind.Heading:
                        cursorY = LayoutHeading(block.Heading, 0, cursorY, dl, fontSystem, settings);
                        break;
                    case BlockKind.Paragraph:
                        cursorY = LayoutParagraph(block.Paragraph, 0, cursorY, dl, fontSystem, settings, imageProvider);
                        break;
                    case BlockKind.BlockQuote:
                        cursorY = LayoutQuote(block.BlockQuote, 0, cursorY, dl, fontSystem, settings, imageProvider);
                        break;
                    case BlockKind.List:
                        cursorY = LayoutList(block.List, 0, cursorY, 0, dl, fontSystem, settings, imageProvider);
                        break;
                    case BlockKind.CodeBlock:
                        cursorY = LayoutCode(block.CodeBlock, 0, cursorY, dl, fontSystem, settings);
                        break;
                    case BlockKind.HorizontalRule:
                        cursorY = LayoutHr(0, cursorY, dl, settings);
                        break;
                    case BlockKind.Table:
                        cursorY = LayoutTable(block.Table, 0, cursorY, dl, fontSystem, settings);
                        break;
                    case BlockKind.Anchor:
                        // no-op for visuals; if you want jump-to, record map here.
                        break;
                }
                maxRight = MathF.Max(maxRight, settings.Width);
            }

            dl.Size = new Vector2(settings.Width, cursorY);
            return dl;
        }

        public static void Render(MarkdownDisplayList dl, FontSystem fontSystem, IFontRenderer renderer, Vector2 position, MarkdownLayoutSettings settings)
        {
            if (dl == null || dl.Ops.Count == 0) return;

            // Batch shape quads into a single DrawQuads call using the font atlas texture.
            var verts = new List<IFontRenderer.Vertex>(128);
            var idx = new List<int>(256);
            int vbase = 0;

            foreach (var op in dl.Ops)
            {
                if (op is DrawQuad q)
                {
                    var offsetRect = new RectangleF(q.Rect.X + position.X, q.Rect.Y + position.Y, q.Rect.Width, q.Rect.Height);
                    AddQuad(ref verts, ref idx, ref vbase, offsetRect, q.Color);
                }
            }

            if (verts.Count > 0)
            {
                renderer.DrawQuads(fontSystem.Texture, verts.ToArray(), idx.ToArray());
            }

            // Draw text and images in submission order
            foreach (var op in dl.Ops)
            {
                if (op is DrawText t)
                {
                    var offsetPos = new Vector2(t.Pos.X + position.X, t.Pos.Y + position.Y);
                    fontSystem.DrawLayout(t.Layout, offsetPos, t.Color);
                    if (t.LinkRanges != null && t.LinkRanges.Count > 0)
                        DrawLinkOverprint(t, position, fontSystem, renderer, settings);
                    if (t.Decorations != null && t.Decorations.Count > 0)
                        DrawDecorations(t, position, fontSystem, renderer, settings);
                }
                else if (op is DrawImage img)
                {
                    var vertsImg = new IFontRenderer.Vertex[4];
                    var idxImg = new int[] { 0, 2, 1, 1, 2, 3 };
                    var r = img.Rect;
                    float offsetX = r.X + position.X;
                    float offsetY = r.Y + position.Y;
                    vertsImg[0] = new IFontRenderer.Vertex(new Vector3(offsetX, offsetY, 0), FontColor.White, new Vector2(0, 0));
                    vertsImg[1] = new IFontRenderer.Vertex(new Vector3(offsetX + r.Width, offsetY, 0), FontColor.White, new Vector2(1, 0));
                    vertsImg[2] = new IFontRenderer.Vertex(new Vector3(offsetX, offsetY + r.Height, 0), FontColor.White, new Vector2(0, 1));
                    vertsImg[3] = new IFontRenderer.Vertex(new Vector3(offsetX + r.Width, offsetY + r.Height, 0), FontColor.White, new Vector2(1, 1));
                    renderer.DrawQuads(img.Texture, vertsImg, idxImg);
                }
            }
        }

        public static bool TryGetLinkAt(MarkdownDisplayList dl, Vector2 point, Vector2 renderOffset, out string href)
        {
            foreach (var link in dl.Links)
            {
                var r = link.Rect;
                float adjustedX = r.X + renderOffset.X;
                float adjustedY = r.Y + renderOffset.Y;
                if (point.X >= adjustedX && point.X <= adjustedX + r.Width &&
                    point.Y >= adjustedY && point.Y <= adjustedY + r.Height)
                {
                    href = link.Href;
                    return true;
                }
            }
            href = string.Empty;
            return false;
        }

        #endregion

        #region Block layout

        private static float LayoutParagraph(Paragraph p, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, IMarkdownImageProvider? imageProvider,
                                      float? sizeOverride = null, float? lineHeightOverride = null, FontFile? fontOverride = null, float? widthOverride = null)
        {
            float wAvail = widthOverride ?? settings.Width;
            var segment = new List<Inline>();
            foreach (var inline in p.Inlines)
            {
                if (inline.Kind == InlineKind.Image)
                {
                    if (segment.Count > 0)
                    {
                        y = LayoutTextSegment(segment, x, y, dl, fontSystem, settings, sizeOverride, lineHeightOverride, fontOverride, wAvail);
                        segment.Clear();
                    }
                    y = LayoutImage(inline, x, y, dl, fontSystem, settings, imageProvider, sizeOverride, lineHeightOverride, fontOverride, wAvail);
                }
                else
                {
                    segment.Add(inline);
                }
            }
            if (segment.Count > 0)
                y = LayoutTextSegment(segment, x, y, dl, fontSystem, settings, sizeOverride, lineHeightOverride, fontOverride, wAvail);

            return y + settings.ParagraphSpacing;
        }

        private static float LayoutTextSegment(List<Inline> inlines, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings,
                                        float? sizeOverride, float? lineHeightOverride, FontFile? fontOverride, float width)
        {
            var (text, decos, linkSpans, styles) = FlattenInlines(inlines);

            var baseFont = fontOverride ?? settings.ParagraphFont;
            var tls = TextLayoutSettings.Default;
            tls.PixelSize = sizeOverride ?? settings.BaseSize;
            tls.LineHeight = lineHeightOverride ?? settings.LineHeight;
            tls.WrapMode = TextWrapMode.Wrap;
            tls.MaxWidth = width;
            tls.Alignment = TextAlignment.Left;
            tls.Font = baseFont;
            tls.FontSelector = (charIndex) => ResolveFontForIndex(charIndex, fontSystem, baseFont, styles, settings);

            var tl = fontSystem.CreateLayout(text, tls);
            var linkRanges = new List<IntRange>();
            foreach (var ls in linkSpans) linkRanges.Add(ls.Range);

            var op = new DrawText { Layout = tl, Pos = new Vector2(x, y), Color = settings.ColorText, Decorations = decos, LinkRanges = linkRanges };
            dl.Ops.Add(op);
            if (linkSpans.Count > 0)
                AddLinkHitBoxes(dl, op, linkSpans);
            return y + tl.Size.Y;
        }

        private static float LayoutImage(Inline img, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, IMarkdownImageProvider? imageProvider,
                                  float? sizeOverride, float? lineHeightOverride, FontFile? fontOverride, float widthAvail)
        {
            if (imageProvider != null && imageProvider.TryGetImage(img.Href, out var tex, out var size))
            {
                float w = size.X;
                float h = size.Y;
                if (w > widthAvail && w > 0)
                {
                    float scale = widthAvail / w;
                    w = widthAvail;
                    h *= scale;
                }
                dl.Ops.Add(new DrawImage { Texture = tex, Rect = new RectangleF(x, y, w, h) });
                return y + h;
            }
            // fallback to alt text
            var alt = new List<Inline> { Inline.TextRun(img.Text) };
            return LayoutTextSegment(alt, x, y, dl, fontSystem, settings, sizeOverride, lineHeightOverride, fontOverride, widthAvail);
        }

        private static float LayoutHeading(Heading h, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, float? widthOverride = null)
        {
            float scale = h.Level switch { 1 => settings.Heading1Scale, 2 => settings.Heading2Scale, 3 => settings.Heading3Scale, _ => 1.1f };
            float size = settings.BaseSize * scale;
            float lh = MathF.Max(settings.LineHeight, 1.0f);
            return LayoutParagraph(new Paragraph(h.Inlines), x, y, dl, fontSystem, settings, null, size, lh, widthOverride: widthOverride);
        }

        private static float LayoutQuote(BlockQuote q, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, IMarkdownImageProvider? imageProvider, float? widthOverride = null)
        {
            float wAvail = widthOverride ?? settings.Width;

            // measure paragraph first to get height
            var beforeOpsCount = dl.Ops.Count; 
            float indent = settings.BlockQuoteBarWidth + settings.BlockQuoteIndent;
            float yAfter = LayoutParagraph(
                new Paragraph(q.Inlines),
                x + indent, y, dl, fontSystem, settings, imageProvider,
                widthOverride: wAvail - indent
            );
            float h = yAfter - y - settings.ParagraphSpacing;

            // prepend left bar quad (ensure it renders under text by ordering)
            dl.Ops.Insert(beforeOpsCount, new DrawQuad {
                Rect = new RectangleF(x, y, settings.BlockQuoteBarWidth, h),
                Color = settings.ColorQuoteBar
            });
            return yAfter;
        }

        private static float LayoutList(ListBlock list, float x, float y, int depth, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, IMarkdownImageProvider? imageProvider)
        {
            float bulletBox = settings.BaseSize * 1.5f;
            float contentX = x + depth * settings.ListIndent + bulletBox;

            int index = 1;

            foreach (var item in list.Items)
            {
                float lineTop = y;
                // bullet/number to be inserted before content
                if (!list.Ordered)
                {
                    //float r = settings.BulletRadius;
                    float r = settings.BaseSize * 0.2f;
                    float bx = x + depth * settings.ListIndent + (bulletBox - 2 * r) * 0.5f;
                    float by = lineTop + settings.BaseSize * 0.35f; // approximate baseline offset
                    dl.Ops.Add(new DrawQuad { Rect = new RectangleF(bx, by, 2 * r, 2 * r), Color = settings.ColorText });
                }
                else
                {
                    // right-aligned number inside bulletBox
                    var tlsNum = TextLayoutSettings.Default;
                    tlsNum.PixelSize = settings.BaseSize;
                    tlsNum.LineHeight = settings.LineHeight;
                    tlsNum.WrapMode = TextWrapMode.NoWrap;
                    tlsNum.MaxWidth = bulletBox;
                    tlsNum.Alignment = TextAlignment.Right;
                    tlsNum.Font = settings.ParagraphFont;
                    var tlNum = fontSystem.CreateLayout($"{index}.", tlsNum);
                    dl.Ops.Add(new DrawText { Layout = tlNum, Pos = new Vector2(x + depth * settings.ListIndent, lineTop), Color = settings.ColorText });
                }

                // lead line
                var para = new Paragraph(item.Lead);
                float widthAvail = settings.Width - (contentX - x);
                // Subtrace Paragraph Spacing since LayoutParagraph adds that by default
                y = LayoutParagraph(para, contentX, y, dl, fontSystem, settings, imageProvider, widthOverride: widthAvail) - settings.ParagraphSpacing;

                // nested children
                foreach (var child in item.Children)
                {
                    switch (child.Kind)
                    {
                        case BlockKind.List: y = LayoutList(child.List, x, y, depth + 1, dl, fontSystem, settings, imageProvider); break;
                        case BlockKind.Paragraph: y = LayoutParagraph(child.Paragraph, contentX, y, dl, fontSystem, settings, imageProvider, widthOverride: widthAvail) - settings.ParagraphSpacing; break;
                        case BlockKind.CodeBlock: y = LayoutCode(child.CodeBlock, contentX, y, dl, fontSystem, settings, widthOverride: widthAvail); break;
                        case BlockKind.BlockQuote: y = LayoutQuote(child.BlockQuote, contentX, y, dl, fontSystem, settings, imageProvider, widthOverride: widthAvail); break;
                        case BlockKind.Table: y = LayoutTable(child.Table, contentX, y, dl, fontSystem, settings, widthOverride: widthAvail); break;
                        case BlockKind.HorizontalRule: y = LayoutHr(contentX, y, dl, settings); break;
                        case BlockKind.Heading: y = LayoutHeading(child.Heading, contentX, y, dl, fontSystem, settings); break;
                    }
                }

                index++;
            }

            return y;
        }

        private static float LayoutHr(float x, float y, MarkdownDisplayList dl, MarkdownLayoutSettings settings)
        {
            y += settings.HrSpacing;
            dl.Ops.Add(new DrawQuad { Rect = new RectangleF(x, y, settings.Width, settings.HrThickness), Color = settings.ColorRule });
            y += settings.HrThickness + settings.HrSpacing;
            return y;
        }

        private static float LayoutCode(CodeBlock cb, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, float? widthOverride = null)
        {
            float pad = settings.CodePadding;
            float wAvail = widthOverride ?? settings.Width;
            float innerX = x + pad;
            float innerW = MathF.Max(0, wAvail - 2 * pad);

            var tls = TextLayoutSettings.Default;
            tls.PixelSize = settings.BaseSize * 0.95f;
            tls.LineHeight = 1.25f;
            tls.WrapMode = TextWrapMode.Wrap;
            tls.MaxWidth = innerW;
            tls.Alignment = TextAlignment.Left;
            tls.Font = settings.MonoFont;

            var tl = fontSystem.CreateLayout(cb.Code.Replace("\r\n", "\n"), tls);
            float h = tl.Size.Y + 2 * pad;
            dl.Ops.Add(new DrawQuad { Rect = new RectangleF(x, y, wAvail, h), Color = settings.ColorCodeBg });
            dl.Ops.Add(new DrawText { Layout = tl, Pos = new Vector2(innerX, y + pad), Color = settings.ColorText });
            return y + h + settings.ParagraphSpacing;
        }

        private static float LayoutTable(Table t, float x, float y, MarkdownDisplayList dl, FontSystem fontSystem, MarkdownLayoutSettings settings, float? widthOverride = null)
        {
            int cols = t.Rows.Max(r => r.Cells.Count);
            float[] minCol = new float[cols];
            float wAvail = widthOverride ?? settings.Width;

            // pass 1: min widths via NoWrap measure
            foreach (var row in t.Rows)
            {
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c]; 
                    var (text, _, _, styles) = FlattenInlines(cell.Inlines);
                    var tls = TextLayoutSettings.Default;
                    tls.PixelSize = settings.BaseSize;
                    tls.LineHeight = settings.LineHeight;
                    tls.WrapMode = TextWrapMode.NoWrap;
                    tls.MaxWidth = float.MaxValue;
                    tls.Alignment = AlignToText(cell.Align);
                    tls.Font = settings.ParagraphFont;
                    tls.FontSelector = (charIndex) => ResolveFontForIndex(charIndex, fontSystem, settings.ParagraphFont, styles, settings);

                    var tl = fontSystem.CreateLayout(text, tls);
                    minCol[c] = MathF.Max(minCol[c], tl.Size.X);
                }
            }

            // distribute to fit content width
            float totalMin = minCol.Sum();
            float[] colW = new float[cols];
            if (totalMin <= wAvail)
            {
                float extra = wAvail - totalMin;
                for (int c = 0; c < cols; c++)
                    colW[c] = minCol[c] + (totalMin > 0 ? extra * (minCol[c] / totalMin) : extra / Math.Max(1, cols));
            }
            else
            {
                for (int c = 0; c < cols; c++)
                    colW[c] = wAvail * (minCol[c] / MathF.Max(totalMin, 1e-3f));
            }

            // Precompute column x positions for grid lines
            float[] colX = new float[cols + 1];
            colX[0] = x;
            for (int c = 0; c < cols; c++) colX[c + 1] = colX[c] + colW[c];

            float tableTop = y;
            float rowY = y;
            var perRowHeights = new float[t.Rows.Count];

            // Pass 2: layout rows (we'll emit text now and draw grid after we know full height)
            for (int r = 0; r < t.Rows.Count; r++)
            {
                var row = t.Rows[r];
                float rowHeight = 0f;

                float cx = x;
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    var (text, decos, linkSpans, styles) = FlattenInlines(cell.Inlines);

                    var tls = TextLayoutSettings.Default;
                    tls.PixelSize = settings.BaseSize;
                    tls.LineHeight = settings.LineHeight;
                    tls.WrapMode = TextWrapMode.Wrap;
                    tls.MaxWidth = colW[c];
                    tls.Alignment = AlignToText(cell.Align);
                    tls.Font = settings.ParagraphFont;
                    tls.FontSelector = (charIndex) => ResolveFontForIndex(charIndex, fontSystem, settings.ParagraphFont, styles, settings);

                    var tl = fontSystem.CreateLayout(text, tls);

                    var linkRanges = new List<IntRange>();
                    foreach (var ls in linkSpans) linkRanges.Add(ls.Range);
                    var op = new DrawText { Layout = tl, Pos = new Vector2(cx, rowY), Color = settings.ColorText, Decorations = decos, LinkRanges = linkRanges };
                    dl.Ops.Add(op);
                    if (linkSpans.Count > 0) AddLinkHitBoxes(dl, op, linkSpans);
                    rowHeight = MathF.Max(rowHeight, tl.Size.Y);

                    cx += colW[c];
                }

                perRowHeights[r] = rowHeight;
                rowY += rowHeight;
            }

            float tableBottom = rowY;

            // Draw grid lines UNDER the text (borders 1px)
            float th = 1f; // thickness
            // Horizontal lines: at each row boundary including top/bottom
            {
                float yCursor = tableTop;
                for (int r = 0; r <= t.Rows.Count; r++)
                {
                    dl.Ops.Insert(0, new DrawQuad {
                        Rect = new RectangleF(x, yCursor - th * 0.5f, wAvail, th),
                        Color = settings.ColorRule
                    });
                    if (r < t.Rows.Count) yCursor += perRowHeights[r];
                }
            }
            // Vertical lines: at each column boundary
            for (int c = 0; c < colX.Length; c++)
            {
                dl.Ops.Insert(0, new DrawQuad {
                    Rect = new RectangleF(colX[c] - th * 0.5f, tableTop, th, tableBottom - tableTop),
                    Color = settings.ColorRule
                });
            }

            return tableBottom + settings.ParagraphSpacing;
        }

        private static TextAlignment AlignToText(TableAlign a) => a switch {
            TableAlign.Center => TextAlignment.Center,
            TableAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        private static FontFile ResolveFontForIndex(int idx, FontSystem fs, FontFile baseFont, List<StyleSpan> spans, MarkdownLayoutSettings settings)
        {
            bool bold = false, italic = false;
            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (idx >= s.Start && idx < s.End) { bold |= s.Bold; italic |= s.Italic; if (bold && italic) break; }
            }

            if (bold && italic) return fs.GetFont(baseFont.FamilyName, FontStyle.BoldItalic) ?? baseFont;
            if (bold) return fs.GetFont(baseFont.FamilyName, FontStyle.Bold) ?? baseFont;
            if (italic) return fs.GetFont(baseFont.FamilyName, FontStyle.Italic) ?? baseFont;
            return baseFont;
        }

        #endregion

        #region Inline flattening & decorations

        private static (string text, List<DecorationSpan> decos, List<LinkSpan> links, List<StyleSpan> styles) FlattenInlines(List<Inline> inlines)
        {
            var sb = new System.Text.StringBuilder();
            var decos = new List<DecorationSpan>();
            var links = new List<LinkSpan>();
            var styles = new List<StyleSpan>();

            void EmitText(string s, bool bold, bool italic)
            {
                if (string.IsNullOrEmpty(s)) return;
                int s0 = sb.Length;
                sb.Append(s);
                int s1 = sb.Length;
                if (bold || italic) styles.Add(new StyleSpan(s0, s1, bold, italic));
            }

            void Walk(List<Inline> xs, bool bold, bool italic)
            {
                foreach (var x in xs)
                {
                    switch (x.Kind)
                    {
                        case InlineKind.Text:
                            EmitText(x.Text, bold, italic);
                            break;

                        case InlineKind.Code:
                            // render code as plain mono: no bold/italic
                            EmitText(x.Text, false, false);
                            break;

                        case InlineKind.Span:
                        {
                            bool b2 = bold || x.Style.HasFlag(InlineStyle.Strong);
                            bool i2 = italic || x.Style.HasFlag(InlineStyle.Emphasis);

                            int s0 = sb.Length;
                            Walk(x.Children, b2, i2);
                            int s1 = sb.Length;

                            if (s1 > s0)
                            {
                                if (x.Style.HasFlag(InlineStyle.Underline))
                                    decos.Add(new DecorationSpan { CharStart = s0, CharEnd = s1, Kind = DecorationKind.Underline });
                                if (x.Style.HasFlag(InlineStyle.Strike))
                                    decos.Add(new DecorationSpan { CharStart = s0, CharEnd = s1, Kind = DecorationKind.Strike });
                                if (x.Style.HasFlag(InlineStyle.Overline))
                                    decos.Add(new DecorationSpan { CharStart = s0, CharEnd = s1, Kind = DecorationKind.Overline });
                            }
                            break;
                        }

                        case InlineKind.Link:
                        {
                            int s0 = sb.Length;
                            Walk(x.Children, bold, italic);
                            int s1 = sb.Length;
                            if (s1 > s0)
                            {
                                // underline + remember for blue overprint and hit testing
                                decos.Add(new DecorationSpan { CharStart = s0, CharEnd = s1, Kind = DecorationKind.Underline });
                                links.Add(new LinkSpan(new IntRange(s0, s1), x.Href));
                            }
                            break;
                        }

                        // images are handled separately by LayoutParagraph
                        case InlineKind.Image:
                            // ignore
                            break;

                        case InlineKind.LineBreak:
                            sb.Append('\n'); // no style span
                            break;
                    }
                }
            }

            Walk(inlines, false, false);
            return (sb.ToString(), decos, links, styles);
        }

        private static void DrawLinkOverprint(DrawText t, Vector2 position, FontSystem fontSystem, IFontRenderer renderer, MarkdownLayoutSettings settings)
        {
            var layout = t.Layout;
            if (layout.Lines == null || layout.Lines.Count == 0) return;

            var verts = new List<IFontRenderer.Vertex>(256);
            var idx = new List<int>(512);
            int vbase = 0;

            string text = layout.Text ?? string.Empty;
            int ti = 0; // absolute text index cursor

            foreach (var line in layout.Lines)
            {
                var glyphs = line.Glyphs;
                int gCount = glyphs.Count;
                if (gCount == 0) continue;

                // map glyph index -> absolute text index (skip whitespace in text)
                var g2t = new int[gCount];
                for (int gi = 0; gi < gCount; gi++)
                {
                    char gc = glyphs[gi].Character;
                    while (ti < text.Length)
                    {
                        char tc = text[ti];
                        if (tc == ' ' || tc == '\t' || tc == '\n' || tc == '\r') { ti++; continue; }
                        if (tc == gc) { g2t[gi] = ti++; break; }
                        ti++;
                    }
                }

                foreach (var rng in t.LinkRanges)
                {
                    int i0 = -1, i1 = -1;
                    for (int gi = 0; gi < gCount; gi++) { if (g2t[gi] >= rng.Start) { i0 = gi; break; } }
                    if (i0 == -1) continue;
                    for (int gi = gCount - 1; gi >= i0; gi--) { if (g2t[gi] < rng.End) { i1 = gi; break; } }
                    if (i1 == -1 || i1 < i0) continue;

                    for (int gi = i0; gi <= i1; gi++)
                    {
                        var ginst = glyphs[gi];
                        var g = ginst.Glyph;              // AtlasGlyph
                        if (!g.IsInAtlas || g.AtlasWidth <= 0 || g.AtlasHeight <= 0) continue;

                        float x = t.Pos.X + position.X + line.Position.X + ginst.Position.X;
                        float y = t.Pos.Y + position.Y + line.Position.Y + ginst.Position.Y;

                        var c = settings.ColorLink;
                        // 4 vertices (pos, uv, color)
                        verts.Add(new IFontRenderer.Vertex(new Vector3(x, y, 0), c, new Vector2(g.U0, g.V0)));
                        verts.Add(new IFontRenderer.Vertex(new Vector3(x + g.AtlasWidth, y, 0), c, new Vector2(g.U1, g.V0)));
                        verts.Add(new IFontRenderer.Vertex(new Vector3(x, y + g.AtlasHeight, 0), c, new Vector2(g.U0, g.V1)));
                        verts.Add(new IFontRenderer.Vertex(new Vector3(x + g.AtlasWidth, y + g.AtlasHeight, 0), c, new Vector2(g.U1, g.V1)));
                        idx.Add(vbase + 0); idx.Add(vbase + 2); idx.Add(vbase + 1);
                        idx.Add(vbase + 1); idx.Add(vbase + 2); idx.Add(vbase + 3);
                        vbase += 4;
                    }
                }
            }

            if (verts.Count > 0)
                renderer.DrawQuads(fontSystem.Texture, verts.ToArray(), idx.ToArray());
        }

        private static void DrawDecorations(DrawText t, Vector2 position, FontSystem fontSystem, IFontRenderer renderer, MarkdownLayoutSettings settings)
        {
            var layout = t.Layout;
            if (layout.Lines == null || layout.Lines.Count == 0) return;

            var verts = new List<IFontRenderer.Vertex>(128);
            var idx = new List<int>(256);
            int vbase = 0;

            // We will map each line's glyphs to absolute character indices in layout.Text.
            // This copes with spaces/tabs/newlines that don't produce glyphs.
            string text = layout.Text ?? string.Empty;
            int ti = 0; // absolute text index cursor we advance as we match glyph chars

            foreach (var line in layout.Lines)
            {
                var glyphs = line.Glyphs;
                int gCount = glyphs.Count;
                if (gCount == 0) continue;

                // Build mapping: glyphIndex -> absolute text index
                var g2t = new int[gCount];
                for (int gi = 0; gi < gCount; gi++)
                {
                    char gc = glyphs[gi].Character; // the char this glyph renders
                                                    // Advance text index until we find the next matching character, skipping whitespace
                                                    // and control chars that don't produce glyphs.
                    while (ti < text.Length)
                    {
                        char tc = text[ti];
                        // Skip chars that produce no glyphs in your engine (space, tab, newline, CR)
                        if (tc == ' ' || tc == '\t' || tc == '\n' || tc == '\r')
                        {
                            ti++;
                            continue;
                        }
                        // Found a printable; try to match it to the glyph char
                        if (tc == gc)
                        {
                            g2t[gi] = ti;
                            ti++; // move past it
                            break;
                        }
                        // If it doesn't match (punctuation or multi-script mixing),
                        // advance the text cursor until it lines up. This is greedy but
                        // stable because we only move forward.
                        ti++;
                    }
                }

                // For each decoration, intersect with this line's glyphs via text-index mapping
                foreach (var deco in t.Decorations)
                {
                    // Find first glyph whose text index >= deco.CharStart
                    int i0 = -1, i1 = -1;
                    for (int gi = 0; gi < gCount; gi++)
                    {
                        if (g2t[gi] >= deco.CharStart) { i0 = gi; break; }
                    }
                    if (i0 == -1) continue;

                    // Find last glyph whose text index < deco.CharEnd
                    for (int gi = gCount - 1; gi >= i0; gi--)
                    {
                        if (g2t[gi] < deco.CharEnd) { i1 = gi; break; }
                    }
                    if (i1 == -1 || i1 < i0) continue;

                    // Convert glyph span -> x range
                    float x0 = t.Pos.X + position.X + line.Position.X + glyphs[i0].Position.X;
                    float x1 = t.Pos.X + position.X + line.Position.X + glyphs[i1].Position.X + glyphs[i1].AdvanceWidth;

                    float yBase = t.Pos.Y + position.Y + line.Position.Y;
                    float thickness = MathF.Max(1f, line.Height * 0.06f);

                    float y = deco.Kind switch {
                        DecorationKind.Underline => yBase + line.Height * 0.7f,  // near baseline bottom
                        DecorationKind.Strike => yBase + line.Height * 0.50f,  // midline
                        _ => yBase + line.Height * 0.18f   // Overline
                    };


                    // Decide color: links use ColorLink, others use text color
                    FontColor color = t.Color;
                    if (deco.Kind == DecorationKind.Underline && t.LinkRanges != null)
                    {
                        foreach (var lr in t.LinkRanges)
                        {
                            if (lr.End > deco.CharStart && lr.Start < deco.CharEnd)
                            {
                                color = settings.ColorLink;
                                break;
                            }
                        }
                    }

                    // Make sure we draw at least a 1px wide segment
                    AddQuad(ref verts, ref idx, ref vbase, new RectangleF(x0, y, MathF.Max(1, x1 - x0), thickness), color);
                }
            }

            if (verts.Count > 0)
                renderer.DrawQuads(fontSystem.Texture, verts.ToArray(), idx.ToArray());
        }

        private static void AddLinkHitBoxes(MarkdownDisplayList dl, DrawText t, List<LinkSpan> links)
        {
            var layout = t.Layout;
            if (layout.Lines == null || layout.Lines.Count == 0) return;

            string text = layout.Text ?? string.Empty;
            int ti = 0;

            foreach (var line in layout.Lines)
            {
                var glyphs = line.Glyphs;
                int gCount = glyphs.Count;
                if (gCount == 0) continue;

                var g2t = new int[gCount];
                for (int gi = 0; gi < gCount; gi++)
                {
                    char gc = glyphs[gi].Character;
                    while (ti < text.Length)
                    {
                        char tc = text[ti];
                        if (tc == ' ' || tc == '\t' || tc == '\n' || tc == '\r') { ti++; continue; }
                        if (tc == gc) { g2t[gi] = ti++; break; }
                        ti++;
                    }
                }

                foreach (var l in links)
                {
                    var rng = l.Range;
                    int i0 = -1, i1 = -1;
                    for (int gi = 0; gi < gCount; gi++) { if (g2t[gi] >= rng.Start) { i0 = gi; break; } }
                    if (i0 == -1) continue;
                    for (int gi = gCount - 1; gi >= i0; gi--) { if (g2t[gi] < rng.End) { i1 = gi; break; } }
                    if (i1 == -1 || i1 < i0) continue;

                    float x0 = t.Pos.X + line.Position.X + glyphs[i0].Position.X;
                    float x1 = t.Pos.X + line.Position.X + glyphs[i1].Position.X + glyphs[i1].AdvanceWidth;
                    float y0 = t.Pos.Y + line.Position.Y;
                    float h = line.Height;
                    dl.Links.Add(new LinkInfo(new RectangleF(x0, y0, x1 - x0, h), l.Href));
                }
            }
        }

        #endregion

        #region Quad helpers

        private static void AddQuad(ref List<IFontRenderer.Vertex> verts, ref List<int> idx, ref int vbase, RectangleF r, FontColor color)
        {
            // UV(0,0) white texel as requested
            var uv = new Vector2(0, 0);
            verts.Add(new IFontRenderer.Vertex(new Vector3(r.X, r.Y, 0), color, uv));
            verts.Add(new IFontRenderer.Vertex(new Vector3(r.X + r.Width, r.Y, 0), color, uv));
            verts.Add(new IFontRenderer.Vertex(new Vector3(r.X, r.Y + r.Height, 0), color, uv));
            verts.Add(new IFontRenderer.Vertex(new Vector3(r.X + r.Width, r.Y + r.Height, 0), color, uv));
            idx.Add(vbase + 0); idx.Add(vbase + 1); idx.Add(vbase + 2);
            idx.Add(vbase + 1); idx.Add(vbase + 3); idx.Add(vbase + 2);
            vbase += 4;
        }

        #endregion
    }
}
