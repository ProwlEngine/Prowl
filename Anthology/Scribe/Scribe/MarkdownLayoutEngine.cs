// MarkdownLayoutEngine — builds and renders a Markdown AST using your FontSystem
// Images and links are ignored (renders link text only), as requested.
// Shapes/lines are emitted as quads sampling the atlas at UV(0,0) (white texel).
//
// Usage:
//   var engine = new Prowl.Scribe.MarkdownLayoutEngine(fontSystem, renderer, settings);
//   var ops = engine.Layout(doc, new Vector2(x, y));
//   engine.Render(ops); // draws shapes + text
//
// Dependencies:
//   - FontSystem (provided in your project)
//   - Prowl.Markdown AST (from the parser we wrote)
//   - IFontRenderer and FontColor types from your renderer layer

using StbTrueTypeSharp;
using System.Numerics;

namespace Prowl.Scribe
{
    #region Support types

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

    public struct IntRange { public int Start, End; public IntRange(int s, int e) { Start = s; End = e; } }

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

        public FontInfo ParagraphFont;       // main font
        public FontInfo MonoFont;            // code font
        public FontInfo BoldFont;        // optional
        public FontInfo ItalicFont;      // optional
        public FontInfo BoldItalicFont;  // optional

        public FontColor ColorText;
        public FontColor ColorMutedText;
        public FontColor ColorRule;
        public FontColor ColorQuoteBar;
        public FontColor ColorCodeBg;        // used as solid quad color
        public FontColor ColorLink;

        public static MarkdownLayoutSettings Default(FontInfo textFont, FontInfo monoFont, float width)
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
                MonoFont = monoFont ?? textFont,
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
        public readonly List<IDrawOp> Ops = new();
        public Vector2 Size; // overall width/height used
    }

    #endregion

    public sealed class MarkdownLayoutEngine
    {
        private readonly FontSystem _fs;
        private readonly IFontRenderer _renderer; // to draw shape quads
        private readonly MarkdownLayoutSettings _s;

        public MarkdownLayoutEngine(FontSystem fs, IFontRenderer renderer, in MarkdownLayoutSettings settings)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _s = settings;
        }

        #region Public API

        public MarkdownDisplayList Layout(Document doc, Vector2 origin)
        {
            var dl = new MarkdownDisplayList();
            float cursorY = origin.Y;
            float maxRight = origin.X;

            foreach (var block in doc.Blocks)
            {
                switch (block.Kind)
                {
                    case BlockKind.Heading:
                        cursorY = LayoutHeading(block.Heading, origin.X, cursorY, dl);
                        break;
                    case BlockKind.Paragraph:
                        cursorY = LayoutParagraph(block.Paragraph, origin.X, cursorY, dl);
                        break;
                    case BlockKind.BlockQuote:
                        cursorY = LayoutQuote(block.BlockQuote, origin.X, cursorY, dl);
                        break;
                    case BlockKind.List:
                        cursorY = LayoutList(block.List, origin.X, cursorY, 0, dl);
                        break;
                    case BlockKind.CodeBlock:
                        cursorY = LayoutCode(block.CodeBlock, origin.X, cursorY, dl);
                        break;
                    case BlockKind.HorizontalRule:
                        cursorY = LayoutHr(origin.X, cursorY, dl);
                        break;
                    case BlockKind.Table:
                        cursorY = LayoutTable(block.Table, origin.X, cursorY, dl);
                        break;
                    case BlockKind.Anchor:
                        // no-op for visuals; if you want jump-to, record map here.
                        break;
                }
                maxRight = MathF.Max(maxRight, origin.X + _s.Width);
            }

            dl.Size = new Vector2(_s.Width, cursorY - origin.Y);
            return dl;
        }

        public void Render(MarkdownDisplayList dl)
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
                    AddQuad(ref verts, ref idx, ref vbase, q.Rect, q.Color);
                }
            }

            if (verts.Count > 0)
            {
                _renderer.DrawQuads(_fs.Texture, verts.ToArray(), idx.ToArray());
            }

            // Now draw text layouts (they create their own batches inside FontSystem)
            foreach (var op in dl.Ops)
            {
                if (op is DrawText t)
                {
                    _fs.DrawLayout(t.Layout, t.Pos, t.Color);
                    if (t.LinkRanges != null && t.LinkRanges.Count > 0)
                        DrawLinkOverprint(t);
                    if (t.Decorations != null && t.Decorations.Count > 0)
                        DrawDecorations(t);
                }
            }
        }

        #endregion

        #region Block layout

        private float LayoutParagraph(Paragraph p, float x, float y, MarkdownDisplayList dl,
                                      float? sizeOverride = null, float? lineHeightOverride = null, FontInfo fontOverride = null, float? widthOverride = null)
        {
            var (text, decos, links, styles) = FlattenInlines(p.Inlines);

            var tls = TextLayoutSettings.Default;
            tls.PixelSize = sizeOverride ?? _s.BaseSize;
            tls.LineHeight = lineHeightOverride ?? _s.LineHeight;
            tls.WrapMode = TextWrapMode.Wrap;
            tls.MaxWidth = widthOverride ?? _s.Width;
            tls.Alignment = TextAlignmentMD.Left;
            tls.PreferredFont = fontOverride ?? _s.ParagraphFont; 
            tls.FontSelector = (charIndex) => ResolveFontForIndex(charIndex, tls.PreferredFont, styles);


            var tl = _fs.CreateLayout(text, tls);

            dl.Ops.Add(new DrawText { Layout = tl, Pos = new Vector2(x, y), Color = _s.ColorText, Decorations = decos, LinkRanges = links });
            return y + tl.Size.Y + _s.ParagraphSpacing;
        }

        private float LayoutHeading(Heading h, float x, float y, MarkdownDisplayList dl, float? widthOverride = null)
        {
            float scale = h.Level switch { 1 => _s.Heading1Scale, 2 => _s.Heading2Scale, 3 => _s.Heading3Scale, _ => 1.1f };
            float size = _s.BaseSize * scale;
            float lh = MathF.Max(_s.LineHeight, 1.0f);
            return LayoutParagraph(new Paragraph(h.Inlines), x, y, dl, size, lh, widthOverride: widthOverride);
        }

        private float LayoutQuote(BlockQuote q, float x, float y, MarkdownDisplayList dl, float? widthOverride = null)
        {
            float wAvail = widthOverride ?? _s.Width;

            // measure paragraph first to get height
            var beforeOpsCount = dl.Ops.Count; 
            float indent = _s.BlockQuoteBarWidth + _s.BlockQuoteIndent;
            float yAfter = LayoutParagraph(
                new Paragraph(q.Inlines),
                x + indent, y, dl,
                widthOverride: wAvail - indent
            );
            float h = yAfter - y - _s.ParagraphSpacing;

            // prepend left bar quad (ensure it renders under text by ordering)
            dl.Ops.Insert(beforeOpsCount, new DrawQuad {
                Rect = new RectangleF(x, y, _s.BlockQuoteBarWidth, h),
                Color = _s.ColorQuoteBar
            });
            return yAfter;
        }

        private float LayoutList(ListBlock list, float x, float y, int depth, MarkdownDisplayList dl)
        {
            float bulletBox = _s.BaseSize * 1.5f;
            float contentX = x + depth * _s.ListIndent + bulletBox;

            int index = 1;

            foreach (var item in list.Items)
            {
                float lineTop = y;
                // bullet/number to be inserted before content
                if (!list.Ordered)
                {
                    float r = _s.BulletRadius;
                    float bx = x + depth * _s.ListIndent + (bulletBox - 2 * r) * 0.5f;
                    float by = lineTop + _s.BaseSize * 0.35f; // approximate baseline offset
                    dl.Ops.Add(new DrawQuad { Rect = new RectangleF(bx, by, 2 * r, 2 * r), Color = _s.ColorText });
                }
                else
                {
                    // right-aligned number inside bulletBox
                    var tlsNum = TextLayoutSettings.Default;
                    tlsNum.PixelSize = _s.BaseSize;
                    tlsNum.LineHeight = _s.LineHeight;
                    tlsNum.WrapMode = TextWrapMode.NoWrap;
                    tlsNum.MaxWidth = bulletBox;
                    tlsNum.Alignment = TextAlignmentMD.Right;
                    tlsNum.PreferredFont = _s.ParagraphFont;
                    var tlNum = _fs.CreateLayout($"{index}.", tlsNum);
                    dl.Ops.Add(new DrawText { Layout = tlNum, Pos = new Vector2(x + depth * _s.ListIndent, lineTop), Color = _s.ColorText });
                }

                // lead line
                var para = new Paragraph(item.Lead);
                float widthAvail = _s.Width - (contentX - x);
                y = LayoutParagraph(para, contentX, y, dl, widthOverride: widthAvail);

                // nested children
                foreach (var child in item.Children)
                {
                    switch (child.Kind)
                    {
                        case BlockKind.List: y = LayoutList(child.List, x, y, depth + 1, dl); break;
                        case BlockKind.Paragraph: y = LayoutParagraph(child.Paragraph, contentX, y, dl, widthOverride: widthAvail); break;
                        case BlockKind.CodeBlock: y = LayoutCode(child.CodeBlock, contentX, y, dl, widthOverride: widthAvail); break;
                        case BlockKind.BlockQuote: y = LayoutQuote(child.BlockQuote, contentX, y, dl, widthOverride: widthAvail); break;
                        case BlockKind.Table: y = LayoutTable(child.Table, contentX, y, dl, widthOverride: widthAvail); break;
                        case BlockKind.HorizontalRule: y = LayoutHr(contentX, y, dl); break;
                        case BlockKind.Heading: y = LayoutHeading(child.Heading, contentX, y, dl); break;
                    }
                }

                index++;
            }

            return y;
        }

        private float LayoutHr(float x, float y, MarkdownDisplayList dl)
        {
            y += _s.HrSpacing;
            dl.Ops.Add(new DrawQuad { Rect = new RectangleF(x, y, _s.Width, _s.HrThickness), Color = _s.ColorRule });
            y += _s.HrThickness + _s.HrSpacing;
            return y;
        }

        private float LayoutCode(CodeBlock cb, float x, float y, MarkdownDisplayList dl, float? widthOverride = null)
        {
            float pad = _s.CodePadding;
            float wAvail = widthOverride ?? _s.Width;
            float innerX = x + pad;
            float innerW = MathF.Max(0, wAvail - 2 * pad);

            var tls = TextLayoutSettings.Default;
            tls.PixelSize = _s.BaseSize * 0.95f;
            tls.LineHeight = 1.25f;
            tls.WrapMode = TextWrapMode.Wrap;
            tls.MaxWidth = innerW;
            tls.Alignment = TextAlignmentMD.Left;
            tls.PreferredFont = _s.MonoFont;

            var tl = _fs.CreateLayout(cb.Code.Replace("\r\n", "\n"), tls);
            float h = tl.Size.Y + 2 * pad;
            dl.Ops.Add(new DrawQuad { Rect = new RectangleF(x, y, wAvail, h), Color = _s.ColorCodeBg });
            dl.Ops.Add(new DrawText { Layout = tl, Pos = new Vector2(innerX, y + pad), Color = _s.ColorText });
            return y + h + _s.ParagraphSpacing;
        }

        private float LayoutTable(Table t, float x, float y, MarkdownDisplayList dl, float? widthOverride = null)
        {
            int cols = t.Rows.Max(r => r.Cells.Count);
            float[] minCol = new float[cols];
            float wAvail = widthOverride ?? _s.Width;

            // pass 1: min widths via NoWrap measure
            foreach (var row in t.Rows)
            {
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c]; 
                    var (text, _, _, styles) = FlattenInlines(cell.Inlines);
                    var tls = TextLayoutSettings.Default;
                    tls.PixelSize = _s.BaseSize;
                    tls.LineHeight = _s.LineHeight;
                    tls.WrapMode = TextWrapMode.NoWrap;
                    tls.MaxWidth = float.MaxValue;
                    tls.Alignment = AlignToText(cell.Align);
                    tls.PreferredFont = _s.ParagraphFont;
                    tls.FontSelector = (charIndex) => ResolveFontForIndex(charIndex, tls.PreferredFont, styles);

                    var tl = _fs.CreateLayout(text, tls);
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

            // Pass 2: layout rows (we’ll emit text now and draw grid after we know full height)
            for (int r = 0; r < t.Rows.Count; r++)
            {
                var row = t.Rows[r];
                float rowHeight = 0f;

                float cx = x;
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    var (text, _, _, styles) = FlattenInlines(cell.Inlines);

                    var tls = TextLayoutSettings.Default;
                    tls.PixelSize = _s.BaseSize;
                    tls.LineHeight = _s.LineHeight;
                    tls.WrapMode = TextWrapMode.Wrap;
                    tls.MaxWidth = colW[c];
                    tls.Alignment = AlignToText(cell.Align);
                    tls.PreferredFont = _s.ParagraphFont;
                    tls.FontSelector = (charIndex) => ResolveFontForIndex(charIndex, tls.PreferredFont, styles);

                    var tl = _fs.CreateLayout(text, tls);

                    // top-align cell content; change to (rowHeight - tl.Size.Y)*0.5f for vertical centering
                    dl.Ops.Add(new DrawText { Layout = tl, Pos = new Vector2(cx, rowY), Color = _s.ColorText });
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
                        Color = _s.ColorRule
                    });
                    if (r < t.Rows.Count) yCursor += perRowHeights[r];
                }
            }
            // Vertical lines: at each column boundary
            for (int c = 0; c < colX.Length; c++)
            {
                dl.Ops.Insert(0, new DrawQuad {
                    Rect = new RectangleF(colX[c] - th * 0.5f, tableTop, th, tableBottom - tableTop),
                    Color = _s.ColorRule
                });
            }

            return tableBottom + _s.ParagraphSpacing;
        }


        private TextAlignmentMD AlignToText(TableAlignMD a) => a switch {
            TableAlignMD.Center => TextAlignmentMD.Center,
            TableAlignMD.Right => TextAlignmentMD.Right,
            _ => TextAlignmentMD.Left
        };

        private FontInfo ResolveFontForIndex(int idx, FontInfo baseFont, List<StyleSpan> spans)
        {
            bool bold = false, italic = false;
            // combine overlapping spans (small inputs; O(n) scan is fine)
            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (idx >= s.Start && idx < s.End) { bold |= s.Bold; italic |= s.Italic; if (bold && italic) break; }
            }

            if (bold && italic && _s.BoldItalicFont != null) return _s.BoldItalicFont;
            if (bold && _s.BoldFont != null) return _s.BoldFont;
            if (italic && _s.ItalicFont != null) return _s.ItalicFont;
            return baseFont;
        }

        #endregion

        #region Inline flattening & decorations

        private (string text, List<DecorationSpan> decos, List<IntRange> links, List<StyleSpan> styles) FlattenInlines(List<Inline> inlines)
        {
            var sb = new System.Text.StringBuilder();
            var decos = new List<DecorationSpan>();
            var links = new List<IntRange>();
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
                                // underline + remember for blue overprint
                                decos.Add(new DecorationSpan { CharStart = s0, CharEnd = s1, Kind = DecorationKind.Underline });
                                links.Add(new IntRange(s0, s1));
                            }
                            break;
                        }

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


        private void DrawLinkOverprint(DrawText t)
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

                        float x = t.Pos.X + line.Position.X + ginst.Position.X;
                        float y = t.Pos.Y + line.Position.Y + ginst.Position.Y;

                        var c = _s.ColorLink;
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
                _renderer.DrawQuads(_fs.Texture, verts.ToArray(), idx.ToArray());
        }


        private void DrawDecorations(DrawText t)
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
                    float x0 = t.Pos.X + line.Position.X + glyphs[i0].Position.X;
                    float x1 = t.Pos.X + line.Position.X + glyphs[i1].Position.X + glyphs[i1].AdvanceWidth;

                    float yBase = t.Pos.Y + line.Position.Y;
                    float thickness = 1f;// MathF.Max(1f, line.Height * 0.06f);

                    float y = deco.Kind switch {
                        DecorationKind.Underline => yBase + line.Height * 0.66f,  // near baseline bottom
                        DecorationKind.Strike => yBase + line.Height * 0.50f,  // midline
                        _ => yBase + line.Height * 0.18f   // Overline
                    };

                    // Make sure we draw at least a 1px wide segment
                    AddQuad(ref verts, ref idx, ref vbase, new RectangleF(x0, y, MathF.Max(1, x1 - x0), thickness), t.Color);
                }
            }

            if (verts.Count > 0)
                _renderer.DrawQuads(_fs.Texture, verts.ToArray(), idx.ToArray());
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
