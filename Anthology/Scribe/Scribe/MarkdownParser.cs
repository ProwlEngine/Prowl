using System.Text;
using System.Text.RegularExpressions;

namespace Prowl.Scribe
{
    // Minimal markdown → AST
    // Focus: headings, blockquotes, lists (ordered/unordered, simple nesting), code blocks (```),
    // tables (pipe rows with optional header underline), anchors (#[name]), hr (---/===),
    // paragraphs, and a compact inline syntax (strong/em/underline/strike/overline, code spans,
    // links, images, autolinks, hard line breaks).
    //
    // Notes
    // - This is a pragmatic, single-file parser intended for UI text rendering engines.
    // - Inline parsing is non-backtracking and fast; block parsing is line-anchored.
    // - The AST consists of lightweight readonly structs.
    //
    // Limitations (by design)
    // - Nested list handling is supported for standard indent-based nesting, but not all edge
    //   cases of CommonMark.
    // - Tables: alignment is inferred from the header underline row like |:---|:--:|---:| etc.
    // - Link text with deeply nested brackets is parsed in a pragmatic way.
    // - Setext headings and many extended syntaxes are not included.


    #region AST

    public readonly struct Document
    {
        public readonly List<Block> Blocks;
        public Document(List<Block> blocks) => Blocks = blocks;
    }

    public enum BlockKind
    {
        Paragraph,
        Heading,
        BlockQuote,
        List,
        CodeBlock,
        Table,
        HorizontalRule,
        Anchor
    }

    public enum InlineKind
    {
        Text,
        Span,      // styled span (see InlineStyle)
        Code,
        Link,
        Image,
        LineBreak  // hard break
    }

    [Flags]
    public enum InlineStyle
    {
        None = 0,
        Emphasis = 1 << 0, // <em>
        Strong = 1 << 1, // <strong>
        Underline = 1 << 2, // <u>
        Strike = 1 << 3, // <s>
        Overline = 1 << 4  // <del>
    }

    public readonly struct Block
    {
        public readonly BlockKind Kind;
        public readonly Paragraph Paragraph;
        public readonly Heading Heading;
        public readonly BlockQuote BlockQuote;
        public readonly ListBlock List;
        public readonly CodeBlock CodeBlock;
        public readonly Table Table;
        public readonly HorizontalRule HorizontalRule;
        public readonly Anchor Anchor;

        private Block(BlockKind kind,
            Paragraph paragraph = default,
            Heading heading = default,
            BlockQuote blockQuote = default,
            ListBlock list = default,
            CodeBlock codeBlock = default,
            Table table = default,
            HorizontalRule hr = default,
            Anchor anchor = default)
        {
            Kind = kind;
            Paragraph = paragraph;
            Heading = heading;
            BlockQuote = blockQuote;
            List = list;
            CodeBlock = codeBlock;
            Table = table;
            HorizontalRule = hr;
            Anchor = anchor;
        }

        public static Block From(Paragraph p) => new(BlockKind.Paragraph, paragraph: p);
        public static Block From(Heading h) => new(BlockKind.Heading, heading: h);
        public static Block From(BlockQuote q) => new(BlockKind.BlockQuote, blockQuote: q);
        public static Block From(ListBlock l) => new(BlockKind.List, list: l);
        public static Block From(CodeBlock c) => new(BlockKind.CodeBlock, codeBlock: c);
        public static Block From(Table t) => new(BlockKind.Table, table: t);
        public static Block From(HorizontalRule hr) => new(BlockKind.HorizontalRule, hr: hr);
        public static Block From(Anchor a) => new(BlockKind.Anchor, anchor: a);
    }

    public readonly struct Paragraph
    {
        public readonly List<Inline> Inlines;
        public Paragraph(List<Inline> inlines) => Inlines = inlines;
    }

    public readonly struct Heading
    {
        public readonly int Level; // 1..4
        public readonly List<Inline> Inlines;
        public Heading(int level, List<Inline> inlines) { Level = level; Inlines = inlines; }
    }

    public readonly struct BlockQuote
    {
        public readonly List<Inline> Inlines; // Simple inline quote (matches original lib)
        public BlockQuote(List<Inline> inlines) => Inlines = inlines;
    }

    public readonly struct ListBlock
    {
        public readonly bool Ordered;
        public readonly List<ListItem> Items;
        public ListBlock(bool ordered, List<ListItem> items) { Ordered = ordered; Items = items; }
    }

    public readonly struct ListItem
    {
        public readonly List<Inline> Lead; // Leading inline text on the bullet line
        public readonly List<Block> Children; // Optional nested blocks (paragraphs/lists)
        public ListItem(List<Inline> lead, List<Block> children) { Lead = lead; Children = children; }
    }

    public readonly struct CodeBlock
    {
        public readonly string Language; // may be empty
        public readonly string Code;
        public CodeBlock(string language, string code) { Language = language; Code = code; }
    }

    public enum TableAlign { None, Left, Center, Right }

    public readonly struct Table
    {
        public readonly List<TableRow> Rows;
        public Table(List<TableRow> rows) => Rows = rows;
    }

    public readonly struct TableRow
    {
        public readonly List<TableCell> Cells;
        public TableRow(List<TableCell> cells) => Cells = cells;
    }

    public readonly struct TableCell
    {
        public readonly bool Header;
        public readonly TableAlign Align;
        public readonly List<Inline> Inlines;
        public TableCell(bool header, TableAlign align, List<Inline> inlines) { Header = header; Align = align; Inlines = inlines; }
    }

    public readonly struct HorizontalRule { }

    public readonly struct Anchor
    {
        public readonly string Name;
        public Anchor(string name) => Name = name;
    }

    public readonly struct Inline
    {
        public readonly InlineKind Kind;
        public readonly string Text;            // for Text, Code
        public readonly InlineStyle Style;      // for Span
        public readonly List<Inline> Children;  // for Span, Link text
        public readonly string Href;            // for Link/Image
        public readonly string Title;           // for Link/Image (optional)

        private Inline(InlineKind kind, string text = null, InlineStyle style = InlineStyle.None,
                       List<Inline> children = null, string href = null, string title = null)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Style = style;
            Children = children;
            Href = href ?? string.Empty;
            Title = title ?? string.Empty;
        }

        public static Inline TextRun(string text) => new(InlineKind.Text, text: text);
        public static Inline Code(string code) => new(InlineKind.Code, text: code);
        public static Inline Span(InlineStyle style, List<Inline> children) => new(InlineKind.Span, style: style, children: children);
        public static Inline Link(List<Inline> children, string href, string title = null) => new(InlineKind.Link, children: children, href: href, title: title);
        public static Inline Image(string alt, string src, string title = null) => new(InlineKind.Image, text: alt, href: src, title: title);
        public static Inline LineBreak() => new(InlineKind.LineBreak);
    }

    #endregion

    #region Parser

    public static class Markdown
    {
        // Entry point
        public static Document Parse(string input)
        {
            var text = Normalize(input);
            var pos = 0;
            var blocks = new List<Block>();
            while (pos < text.Length)
            {
                // Skip leading blank lines
                if (IsBlankLine(text, pos)) { pos = NextLineStart(text, pos); continue; }

                if (TryParseCodeBlock(text, ref pos, out var code)) { blocks.Add(Block.From(code)); continue; }
                if (TryParseHorizontalRule(text, ref pos, out var hr)) { blocks.Add(Block.From(hr)); continue; }
                if (TryParseAnchor(text, ref pos, out var anchor)) { blocks.Add(Block.From(anchor)); continue; }
                if (TryParseHeading(text, ref pos, out var heading)) { blocks.Add(Block.From(heading)); continue; }
                if (TryParseBlockQuote(text, ref pos, out var quote)) { blocks.Add(Block.From(quote)); continue; }
                if (TryParseTable(text, ref pos, out var table)) { blocks.Add(Block.From(table)); continue; }
                if (TryParseList(text, ref pos, out var list)) { blocks.Add(Block.From(list)); continue; }

                // Paragraph (until blank line or next block)
                blocks.Add(Block.From(ParseParagraph(text, ref pos)));
            }
            return new Document(blocks);
        }

        #region Block helpers

        private static bool TryParseHeading(string text, ref int pos, out Heading heading)
        {
            heading = default;
            int lineStart = pos;
            if (!AtLineStart(text, pos)) return false;
            int i = pos;
            int hashes = 0;
            while (i < text.Length && text[i] == '#') { hashes++; i++; }
            if (hashes == 0 || hashes > 6) return false;
            if (i < text.Length && text[i] == ' ') i++; else return false;

            int lineEnd = LineEnd(text, i);
            var line = text.Substring(i, lineEnd - i).TrimEnd();
            var inlines = ParseInlines(line);
            heading = new Heading(hashes, inlines);
            pos = NextLineStart(text, lineEnd);
            return true;
        }

        private static bool TryParseHorizontalRule(string text, ref int pos, out HorizontalRule hr)
        {
            hr = default;
            if (!AtLineStart(text, pos)) return false;
            int lineEnd = LineEnd(text, pos);
            var line = text.Substring(pos, lineEnd - pos).Trim();
            if (Regex.IsMatch(line, "^(?:===+|---+)$"))
            {
                pos = NextLineStart(text, lineEnd);
                return true;
            }
            return false;
        }

        private static bool TryParseAnchor(string text, ref int pos, out Anchor anchor)
        {
            anchor = default;
            if (!AtLineStart(text, pos)) return false;
            int lineEnd = LineEnd(text, pos);
            var line = text.Substring(pos, lineEnd - pos).Trim();
            var m = Regex.Match(line, "^#\\[([^\\]]+?)\\]$");
            if (m.Success)
            {
                anchor = new Anchor(m.Groups[1].Value);
                pos = NextLineStart(text, lineEnd);
                return true;
            }
            return false;
        }

        private static bool TryParseBlockQuote(string text, ref int pos, out BlockQuote quote)
        {
            quote = default;
            if (!AtLineStart(text, pos)) return false;
            int i = pos;
            var sb = new StringBuilder();
            bool any = false;
            while (i < text.Length)
            {
                int le = LineEnd(text, i);
                if (text[i] == '>')
                {
                    int j = i + 1;
                    if (j < text.Length && text[j] == ' ') j++;
                    sb.Append(text, j, le - j);
                    sb.Append('\n');
                    i = NextLineStart(text, le);
                    any = true;
                }
                else break;
            }
            if (!any) return false;
            // Original applies inline() to the content
            var inlines = ParseInlines(sb.ToString().TrimEnd());
            quote = new BlockQuote(inlines);
            pos = i;
            return true;
        }

        private static bool TryParseCodeBlock(string text, ref int pos, out CodeBlock code)
        {
            code = default;
            if (!AtLineStart(text, pos)) return false;
            int i = pos;
            if (!(i + 3 <= text.Length && text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`'))
                return false;
            i += 3;
            // language
            int le = LineEnd(text, i);
            string lang = text.Substring(i, le - i).Trim();
            i = NextLineStart(text, le);
            int start = i;
            while (i < text.Length)
            {
                int e = LineEnd(text, i);
                if (e - i >= 3 && text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`')
                {
                    string body = text.Substring(start, i - start);
                    code = new CodeBlock(lang, body);
                    pos = NextLineStart(text, e);
                    return true;
                }
                i = NextLineStart(text, e);
            }
            // No closing fence; treat fence as paragraph
            return false;
        }

        private static bool TryParseList(string text, ref int pos, out ListBlock list)
        {
            list = default;
            if (!AtLineStart(text, pos)) return false;

            var items = new List<ListItem>();
            bool? ordered = null;
            int i = pos;
            while (i < text.Length)
            {
                int le = LineEnd(text, i);
                var line = text.Substring(i, le - i);
                var m = Regex.Match(line, "^(?:([+*-])|(\\d+)\\.)\\s+(.*)$");
                if (!m.Success) break;

                bool thisOrdered = m.Groups[2].Success;
                ordered ??= thisOrdered;
                if (ordered.Value != thisOrdered) break; // mixed type -> stop

                string lead = m.Groups[3].Value;

                // Gather any following indented lines as the item's continuation
                int j = NextLineStart(text, le);
                var cont = new StringBuilder();
                while (j < text.Length)
                {
                    int le2 = LineEnd(text, j);
                    string l2 = text.Substring(j, le2 - j);
                    if (string.IsNullOrWhiteSpace(l2)) break;
                    // stop if the line begins a new list item
                    if (Regex.IsMatch(l2, "^(?:[+*-]|\\d+\\.)\\s+")) break;
                    cont.AppendLine(l2);
                    j = NextLineStart(text, le2);
                }

                var leadInlines = ParseInlineBlock(lead.TrimEnd());
                var children = new List<Block>();
                if (cont.Length > 0)
                {
                    var nestedText = Outdent(cont.ToString());
                    children = ParseBlocks(nestedText);
                }
                items.Add(new ListItem(leadInlines, children));
                i = j;
            }

            if (items.Count == 0) return false;
            list = new ListBlock(ordered ?? false, items);
            pos = i;
            return true;
        }

        private static bool TryParseTable(string text, ref int pos, out Table table)
        {
            table = default;
            if (!AtLineStart(text, pos)) return false;
            int i = pos;
            int le = LineEnd(text, i);
            string first = text.Substring(i, le - i);
            if (!first.TrimStart().StartsWith("|")) return false;

            var lines = new List<string> { first };
            int j = NextLineStart(text, le);
            while (j < text.Length)
            {
                int le2 = LineEnd(text, j);
                string l2 = text.Substring(j, le2 - j);
                if (l2.TrimStart().StartsWith("|")) { lines.Add(l2); j = NextLineStart(text, le2); }
                else break;
            }

            if (lines.Count == 0) return false;

            // Optional header underline row is the second line if it consists of pipes/dashes/colons
            bool hasHeaderUnderline = false;
            if (lines.Count >= 2)
            {
                var ul = lines[1].Trim();
                // allow alignment row cells with a single or more dashes (e.g. |:--:|)
                hasHeaderUnderline = Regex.IsMatch(ul, "^\\| *:?-+:? *(\\| *:?-+:? *)*\\|?$");
            }

            // parse column alignments before consuming rows so header cells can use them
            var rows = new List<TableRow>();
            TableAlign[] aligns = Array.Empty<TableAlign>();
            if (hasHeaderUnderline)
            {
                aligns = ParseAligns(lines[1].Trim());
            }

            for (int idx = 0; idx < lines.Count; idx++)
            {
                if (idx == 1 && hasHeaderUnderline) continue; // underline line is not a content row

                var trimmed = lines[idx].Trim();
                var parts = SplitPipes(trimmed);
                var cells = new List<TableCell>(parts.Length);
                for (int c = 0; c < parts.Length; c++)
                {
                    bool header = hasHeaderUnderline && idx == 0; // header row
                    var align = (c < aligns.Length) ? aligns[c] : TableAlign.None;
                    var inlines = ParseInlineBlock(parts[c].Trim());
                    cells.Add(new TableCell(header, align, inlines));
                }
                rows.Add(new TableRow(cells));
            }

            table = new Table(rows);
            pos = j;
            return true;
        }

        private static Paragraph ParseParagraph(string text, ref int pos)
        {
            int start = pos;
            int i = pos;
            while (i < text.Length)
            {
                int le = LineEnd(text, i);
                string line = text.Substring(i, le - i);
                if (string.IsNullOrWhiteSpace(line)) { i = le; break; }
                // stop if next line begins a block structure
                int next = NextLineStart(text, le);
                if (next < text.Length)
                {
                    if (StartsBlock(text, next)) { i = le; break; }
                }
                i = next;
            }
            int end = i;
            var paraText = text.Substring(start, end - start).TrimEnd();
            pos = end < text.Length ? NextLineStart(text, end) : end;
            return new Paragraph(ParseInlineBlock(paraText));
        }

        private static List<Block> ParseBlocks(string text)
        {
            int pos = 0;
            var blocks = new List<Block>();
            while (pos < text.Length)
            {
                if (IsBlankLine(text, pos)) { pos = NextLineStart(text, pos); continue; }
                if (TryParseCodeBlock(text, ref pos, out var code)) { blocks.Add(Block.From(code)); continue; }
                if (TryParseHorizontalRule(text, ref pos, out var hr)) { blocks.Add(Block.From(hr)); continue; }
                if (TryParseAnchor(text, ref pos, out var anchor)) { blocks.Add(Block.From(anchor)); continue; }
                if (TryParseHeading(text, ref pos, out var heading)) { blocks.Add(Block.From(heading)); continue; }
                if (TryParseBlockQuote(text, ref pos, out var quote)) { blocks.Add(Block.From(quote)); continue; }
                if (TryParseTable(text, ref pos, out var table)) { blocks.Add(Block.From(table)); continue; }
                if (TryParseList(text, ref pos, out var list)) { blocks.Add(Block.From(list)); continue; }
                blocks.Add(Block.From(ParseParagraph(text, ref pos)));
            }
            return blocks;
        }

        private static bool StartsBlock(string text, int pos)
        {
            if (!AtLineStart(text, pos)) return false;
            int le = LineEnd(text, pos);
            var line = text.Substring(pos, le - pos);
            if (line.StartsWith("```")) return true;
            if (Regex.IsMatch(line, "^(?:#{1,6} )")) return true;
            if (Regex.IsMatch(line.Trim(), "^(?:===+|---+)$")) return true;
            if (line.StartsWith(">")) return true;
            if (line.TrimStart().StartsWith("|")) return true;
            if (Regex.IsMatch(line, "^(?:[+-]|\\d+\\.)\\s+")) return true;
            if (Regex.IsMatch(line.Trim(), "^#\\[[^\\]]+\\]$")) return true;
            return false;
        }

        #endregion

        #region Inline parser

        private static List<Inline> ParseInlineBlock(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return new List<Inline>();
            // Apply inline code/media first, then styling
            var tokens = TokenizeInline(text);
            return ApplyStyles(tokens);
        }

        private static List<Inline> ParseInlines(string text)
        {
            var tokens = TokenizeInline(text);
            return ApplyStyles(tokens);
        }

        private static List<Inline> TokenizeInline(string text)
        {
            var list = new List<Inline>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // hard break: two spaces before/after newline
                if (c == '\n')
                {
                    // handle "\n  " or "  \n" equivalence pragmatically
                    list.Add(Inline.LineBreak());
                    i++;
                    continue;
                }

                if (c == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end >= 0)
                    {
                        string code = text.Substring(i + 1, end - i - 1);
                        list.Add(Inline.Code(code));
                        i = end + 1;
                        continue;
                    }
                }

                if (c == '!' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    if (TryParseBracket(text, i + 1, out var label, out var next))
                    {
                        if (next < text.Length && text[next] == '(' && TryParseParen(text, next + 1, out var href, out var title, out var endp))
                        {
                            list.Add(Inline.Image(label, href, title));
                            i = endp;
                            continue;
                        }
                    }
                }

                if (c == '[')
                {
                    if (TryParseBracket(text, i, out var label, out var next))
                    {
                        if (next < text.Length && text[next] == '(' && TryParseParen(text, next + 1, out var href, out var title, out var endp))
                        {
                            var child = ApplyStyles(TokenizeInline(label));
                            list.Add(Inline.Link(child, href, title));
                            i = endp;
                            continue;
                        }
                    }
                }

                // autolink (scheme://...)
                if (IsUrlStart(text, i))
                {
                    int j = i;
                    while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != ')') j++;
                    int end = j;
                    while (end > i && ".,:;!?".IndexOf(text[end - 1]) >= 0) end--;
                    string url = text.Substring(i, end - i);
                    list.Add(Inline.Link(new List<Inline> { Inline.TextRun(url) }, url));
                    i = end;
                    continue;
                }

                // default: gather plain text until special char
                int k = i;
                while (k < text.Length && !IsSpecial(text, k)) k++;
                if (k > i)
                {
                    // Keep escapes for now; they'll be processed during style application
                    var run = text.Substring(i, k - i);
                    list.Add(Inline.TextRun(run));
                    i = k;
                }
                else
                {
                    // Don’t drop characters we didn’t handle; preserve as text.
                    list.Add(Inline.TextRun(text[i].ToString()));
                    i++;
                }
            }
            return list;
        }

        private static List<Inline> ApplyStyles(List<Inline> tokens)
        {
            // Join Text runs first
            tokens = CoalesceText(tokens);
            var output = new List<Inline>();
            foreach (var t in tokens)
            {
                if (t.Kind != InlineKind.Text) { output.Add(t); continue; }
                string s = t.Text;
                var sb = new StringBuilder();
                int i = 0;
                while (i < s.Length)
                {
                    // handle escapes for style markers and other characters
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        sb.Append(s[i + 1]);
                        i += 2;
                        continue;
                    }

                    // ~, *, _ groups of 1..3
                    char ch = s[i];
                    if (ch == '~' || ch == '*' || ch == '_')
                    {
                        int count = 1; int j = i + 1;
                        while (j < s.Length && s[j] == ch && count < 3) { count++; j++; }
                        // find matching closing markers
                        int close = IndexOfClosing(s, ch, count, j);
                        if (close > j)
                        {
                            // flush any pending text before applying style
                            if (sb.Length > 0) { output.Add(Inline.TextRun(sb.ToString())); sb.Clear(); }
                            var inner = ApplyStyles(TokenizeInline(s.Substring(j, close - j)));
                            InlineStyle style = InlineStyle.None;
                            if (ch == '~') style = count switch { 1 => InlineStyle.Underline, 2 => InlineStyle.Strike, _ => InlineStyle.Overline };
                            else if (ch == '*' || ch == '_')
                            {
                                if (count >= 2) style |= InlineStyle.Strong;
                                if ((count % 2) == 1) style |= InlineStyle.Emphasis;
                            }
                            output.Add(Inline.Span(style, inner));
                            i = close + count; continue;
                        }
                    }

                    // normal character
                    sb.Append(ch);
                    i++;
                }
                if (sb.Length > 0) { output.Add(Inline.TextRun(sb.ToString())); }
            }
            return CoalesceText(output);
        }

        private static int IndexOfClosing(string s, char ch, int count, int from)
        {
            for (int i = from; i + count <= s.Length; i++)
            {
                bool all = true;
                for (int k = 0; k < count; k++) if (i + k >= s.Length || s[i + k] != ch) { all = false; break; }
                if (all)
                {
                    // ignore escaped markers
                    if (i > 0 && s[i - 1] == '\\') continue;
                    return i;
                }
            }
            return -1;
        }

        private static List<Inline> CoalesceText(List<Inline> list)
        {
            if (list.Count == 0) return list;
            var res = new List<Inline>(list.Count);
            var sb = (StringBuilder)null;
            void Flush()
            {
                if (sb != null && sb.Length > 0) { res.Add(Inline.TextRun(sb.ToString())); sb.Clear(); }
            }
            foreach (var it in list)
            {
                if (it.Kind == InlineKind.Text)
                {
                    sb ??= new StringBuilder(); sb.Append(it.Text);
                }
                else { Flush(); res.Add(it); }
            }
            Flush();
            return res;
        }

        #endregion

        #region Utilities

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = s.Replace("\v", string.Empty).Replace("\b", string.Empty).Replace("\f", string.Empty);
            return s;
        }

        private static string Outdent(string block)
        {
            if (string.IsNullOrEmpty(block)) return string.Empty;
            var lines = block.Split('\n');
            int min = int.MaxValue;
            foreach (var ln in lines)
            {
                if (ln.Length == 0) continue;
                int i = 0; while (i < ln.Length && (ln[i] == ' ' || ln[i] == '\t')) i++;
                if (i < ln.Length) min = Math.Min(min, i);
            }
            if (min == int.MaxValue) return block; // all blank
            for (int k = 0; k < lines.Length; k++)
            {
                var ln = lines[k];
                int take = Math.Min(min, ln.Length);
                int i = 0; while (i < take && (ln[i] == ' ' || ln[i] == '\t')) i++;
                lines[k] = ln.Substring(i);
            }
            return string.Join("\n", lines);
        }

        private static bool AtLineStart(string text, int pos) => pos == 0 || text[pos - 1] == '\n';
        private static int LineEnd(string text, int pos) { int i = pos; while (i < text.Length && text[i] != '\n') i++; return i; }
        private static int NextLineStart(string text, int pos) { int i = pos; if (i < text.Length && text[i] != '\n') i = LineEnd(text, i); if (i < text.Length && text[i] == '\n') i++; return i; }
        private static bool IsBlankLine(string text, int pos) { int le = LineEnd(text, pos); for (int i = pos; i < le; i++) if (!char.IsWhiteSpace(text[i])) return false; return true; }

        private static bool IsSpecial(string s, int i)
        {
            char c = s[i];
            // Keep style markers (* _ ~) inside text so ApplyStyles() can parse them.
            // Only split on structural delimiters and autolinks.
            if (c == '`' || c == '[' || c == '!' || c == '\n') return true;
            if (IsUrlStart(s, i)) return true;
            return false;
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1]); i++; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static bool TryParseBracket(string s, int startBracket, out string label, out int nextIndex)
        {
            // startBracket points to '[' (or after '!' for image)
            int i = startBracket + 1; int depth = 1;
            while (i < s.Length)
            {
                if (s[i] == '[') depth++;
                else if (s[i] == ']') { depth--; if (depth == 0) break; }
                i++;
            }
            if (i >= s.Length || s[i] != ']') { label = null; nextIndex = startBracket; return false; }
            label = s.Substring(startBracket + 1, i - (startBracket + 1));
            nextIndex = i + 1;
            return true;
        }

        private static bool TryParseParen(string s, int startParen, out string href, out string title, out int endIndex)
        {
            // parse until matching ')', allowing spaces in title
            int i = startParen; int depth = 0;
            href = null; title = null; endIndex = startParen;
            // href
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int hrefStart = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ')') i++;
            href = s.Substring(hrefStart, i - hrefStart);
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && s[i] != ')')
            {
                int titleStart = i;
                while (i < s.Length && s[i] != ')') i++;
                title = s.Substring(titleStart, i - titleStart).Trim();
                if ((title.StartsWith("\"") && title.EndsWith("\"")) || (title.StartsWith("'") && title.EndsWith("'")))
                    title = title.Length >= 2 ? title.Substring(1, title.Length - 2) : title;
            }
            if (i < s.Length && s[i] == ')') { endIndex = i + 1; return true; }
            return false;
        }

        private static bool IsUrlStart(string s, int i)
        {
            // scheme://
            int j = i;
            while (j < s.Length && char.IsLetter(s[j])) j++;
            if (j + 2 < s.Length && s[j] == ':' && s[j + 1] == '/' && s[j + 2] == '/') return j > i;
            return false;
        }

        private static string[] SplitPipes(string line)
        {
            // Remove leading/trailing pipes then split; no escape handling for simplicity
            var t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            var parts = t.Split('|');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        private static TableAlign[] ParseAligns(string underline)
        {
            var parts = SplitPipes(underline);
            var arr = new TableAlign[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                bool left = p.StartsWith(":");
                bool right = p.EndsWith(":");
                if (left && right) arr[i] = TableAlign.Center;
                else if (left) arr[i] = TableAlign.Left;
                else if (right) arr[i] = TableAlign.Right;
                else arr[i] = TableAlign.None;
            }
            return arr;
        }

        #endregion
    }

    #endregion
}