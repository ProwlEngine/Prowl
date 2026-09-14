using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.RichText;
using Prowl.Quire;
using FontFile = Prowl.Scribe.FontFile;
using TextWrapMode = Prowl.Scribe.TextWrapMode;
using Prowl.Vector;

namespace Prowl.PaperUI.Markdown;

/// <summary>
/// Turns Markdown into ordinary Paper elements.
/// </summary>
public sealed class MarkdownBuilder
{
    private readonly MarkdownDocument _document = new();
    // Per block, the string it is drawn from: rich text for prose or the body of a code block.
    private readonly List<string> _blockText = new();
    // Per block, the source of a paragraph that is only an image. Kept apart from the text so a
    // paragraph whose image does not resolve still falls back to its alt text.
    private readonly List<string> _imageSource = new();
    private readonly StringBuilder _scratch = new();
    private readonly Action<ClickEvent> _linkClick;
    private readonly Action<ElementEvent> _linkHover;

    private string _source = string.Empty;
    private bool _parsed;
    private Paper _paper;

    private FontFile _regular;
    private FontFile _bold;
    private FontFile _italic;
    private FontFile _boldItalic;
    private FontFile _mono;
    private float _fontSize = 16f;
    private float _spacing = 8f;
    private Color _text = new(1f, 1f, 1f, 1f);
    private Color _muted = new(0.82f, 0.82f, 0.82f, 1f);
    private Color _link = new(0f, 0.48f, 1f, 1f);
    private Color _codeBackground = new(0.16f, 0.16f, 0.16f, 1f);
    private Color _rule = new(0.63f, 0.63f, 0.63f, 1f);
    private Action<string> _onLink;
    private Func<string, object> _images;

    public MarkdownBuilder(FontFile font)
    {
        _regular = font ?? throw new ArgumentNullException(nameof(font));
        _linkClick = OnParagraphClicked;
        _linkHover = OnParagraphHovered;
    }

    /// <summary>The Markdown to build. Only parsed again when it actually changes.</summary>
    public MarkdownBuilder Source(string markdown)
    {
        markdown ??= string.Empty;
        if (!_parsed || !string.Equals(markdown, _source, StringComparison.Ordinal))
        {
            _source = markdown;
            _parsed = false;
        }

        return this;
    }

    /// <summary>Faces for styled text. A missing face falls back to the regular font.</summary>
    public MarkdownBuilder Fonts(FontFile bold = null, FontFile italic = null, FontFile boldItalic = null, FontFile mono = null)
    {
        _bold = bold;
        _italic = italic;
        _boldItalic = boldItalic;
        _mono = mono;
        return this;
    }

    /// <summary>Body text size. Headings scale up from it.</summary>
    public MarkdownBuilder FontSize(float size)
    {
        _fontSize = size;
        return this;
    }

    /// <summary>Space between blocks.</summary>
    public MarkdownBuilder Spacing(float spacing)
    {
        _spacing = spacing;
        return this;
    }

    public MarkdownBuilder Colors(Color text, Color muted, Color link, Color codeBackground, Color rule)
    {
        // Link and alt text colours are baked into the rich text, so only they force a rebuild.
        if (!link.Equals(_link) || !muted.Equals(_muted)) _parsed = false;

        _text = text;
        _muted = muted;
        _link = link;
        _codeBackground = codeBackground;
        _rule = rule;
        return this;
    }

    /// <summary>Called with the href when a link is clicked.</summary>
    public MarkdownBuilder OnLink(Action<string> handler)
    {
        _onLink = handler;
        return this;
    }

    /// <summary>
    /// Resolves an image's source to a texture. An image on a line of its own is drawn at its
    /// natural size, no wider than the document. One without a texture, or in the middle of
    /// text, shows its alt text instead.
    /// </summary>
    public MarkdownBuilder Images(Func<string, object> resolver)
    {
        _images = resolver;
        return this;
    }

    /// <summary>Creates the document as a column of elements in the current context.</summary>
    public ElementBuilder Build(Paper paper, string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
    {
        if (paper == null) throw new ArgumentNullException(nameof(paper));

        EnsureParsed();
        _paper = paper;

        var root = paper.Column(stringID, intID, lineID)
            .Width(UnitValue.Stretch())
            .Height(UnitValue.Auto)
            .Gap(_spacing);

        using (root.Enter())
        {
            BuildChildren(paper, _document.Root, _text);
        }

        return root;
    }

    private void EnsureParsed()
    {
        if (_parsed) return;

        _document.Load(_source);
        _blockText.Clear();
        _imageSource.Clear();
        for (int i = 0; i < _document.Blocks.Count; i++)
        {
            _blockText.Add(null);
            _imageSource.Add(null);
        }
        _parsed = true;
    }

    private void BuildChildren(Paper paper, int parent, Color color)
    {
        foreach (int block in _document.ChildrenOf(parent))
        {
            BuildBlock(paper, block, color);
        }
    }

    private void BuildBlock(Paper paper, int index, Color color)
    {
        var block = _document.GetBlock(index);
        switch (block.Kind)
        {
            case BlockKind.Paragraph:
                if (!TryBuildImage(paper, index))
                {
                    Paragraph(paper, index, RichTextOf(index, bold: false), _fontSize, color);
                }
                break;

            case BlockKind.Heading:
                Paragraph(paper, index, RichTextOf(index, bold: true), _fontSize * HeadingScale(block.Level), color);
                break;

            case BlockKind.CodeBlock:
                paper.Box("md", index)
                    .Width(UnitValue.Stretch())
                    .Height(UnitValue.Auto)
                    .BackgroundColor(_codeBackground)
                    .Rounded(4f)
                    .Padding(_spacing)
                    .Clip()
                    .Text(CodeOf(index), _mono ?? _regular)
                    .FontSize(_fontSize)
                    .TextColor(color);
                break;

            case BlockKind.BlockQuote:
                using (paper.Row("md", index).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(_spacing).Enter())
                {
                    paper.Box("md-bar", index).Width(4f).Height(UnitValue.Stretch()).BackgroundColor(_rule).Rounded(2f);
                    using (paper.Column("md-body", index).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(_spacing).Enter())
                    {
                        BuildChildren(paper, index, _muted);
                    }
                }
                break;

            case BlockKind.List:
                using (paper.Column("md", index).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(_spacing * 0.5f).Enter())
                {
                    foreach (int item in _document.ChildrenOf(index))
                    {
                        ListItem(paper, item, block.Flag, color);
                    }
                }
                break;

            case BlockKind.Table:
                Table(paper, index, color);
                break;

            case BlockKind.HorizontalRule:
                paper.Box("md", index).Width(UnitValue.Stretch()).Height(1f).BackgroundColor(_rule);
                break;
        }
    }

    private void Paragraph(Paper paper, int index, string rich, float size, Color color)
    {
        var element = paper.Box("md", index)
            .Width(UnitValue.Stretch())
            .Height(UnitValue.Auto)
            .Text(rich, _regular)
            .RichText(_bold, _italic, _boldItalic, _mono)
            .Wrap(TextWrapMode.Wrap)
            .FontSize(size)
            .TextColor(color);

        WireLinks(element, index);
    }

    private void WireLinks(ElementBuilder element, int block)
    {
        if (_onLink != null && HasLink(_document.GetBlock(block).FirstInline))
        {
            element.OnClick(_linkClick).OnHover(_linkHover);
        }
    }

    private void ListItem(Paper paper, int item, bool ordered, Color color)
    {
        var node = _document.GetBlock(item);
        using (paper.Row("md", item).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(_spacing * 0.5f).Enter())
        {
            float markerWidth = _fontSize * 1.5f;
            if (!node.Info.IsEmpty)
            {
                // A task: an outlined box, filled when done. Drawn rather than typed, so it does not
                // depend on the font having a checkbox glyph.
                float box = _fontSize * 0.8f;
                using (paper.Box("md-marker", item).Width(markerWidth).Height(_fontSize * 1.2f).Enter())
                {
                    var outline = paper.Box("md-check", item)
                        .Width(box).Height(box)
                        .BorderWidth(1.5f).BorderColor(color).Rounded(2f);
                    if (node.Flag) outline.BackgroundColor(color);
                }
            }
            else
            {
                string marker = ordered ? OrdinalText(node.Level) : "\u2022";
                paper.Box("md-marker", item)
                    .Width(markerWidth)
                    .Height(UnitValue.Auto)
                    .Text(marker, _regular)
                    .FontSize(_fontSize)
                    .TextColor(color)
                    .Alignment(ordered ? TextAlignment.Right : TextAlignment.Center);
            }

            using (paper.Column("md-body", item).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(_spacing * 0.5f).Enter())
            {
                BuildChildren(paper, item, color);
            }
        }
    }

    private void Table(Paper paper, int table, Color color)
    {
        using (paper.Column("md", table).Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .BorderWidth(1f).BorderColor(_rule).Rounded(4f).Clip().Enter())
        {
            foreach (int row in _document.ChildrenOf(table))
            {
                bool header = _document.GetBlock(row).Flag;
                var rowElement = paper.Row("md", row).Width(UnitValue.Stretch()).Height(UnitValue.Auto);
                if (header) rowElement.BackgroundColor(_codeBackground);

                using (rowElement.Enter())
                {
                    foreach (int cell in _document.ChildrenOf(row))
                    {
                        var cellElement = paper.Box("md", cell)
                            .Width(UnitValue.Stretch())
                            .Height(UnitValue.Auto)
                            .Padding(_spacing * 0.75f, _spacing * 0.5f)
                            .Text(RichTextOf(cell, bold: header), _regular)
                            .RichText(_bold, _italic, _boldItalic, _mono)
                            .Wrap(TextWrapMode.Wrap)
                            .FontSize(_fontSize)
                            .TextColor(color)
                            .Alignment(CellAlignment(_document.GetBlock(cell).Align));
                        WireLinks(cellElement, cell);
                    }
                }
            }
        }
    }

    private bool TryBuildImage(Paper paper, int index)
    {
        if (_images == null) return false;

        int only = SoleImage(_document.GetBlock(index).FirstInline);
        if (only < 0) return false;

        _imageSource[index] ??= _document.TextOf(_document.GetInline(only).Href);
        object texture = _images(_imageSource[index]);
        if (texture == null) return false;

        var size = paper.Renderer.GetTextureSize(texture);
        if (size.X <= 0 || size.Y <= 0) return false;

        paper.Box("md", index)
            .Width((float)size.X)
            .Height(UnitValue.Auto)
            .MaxWidth(UnitValue.Percentage(100))
            .AspectRatio((float)size.X / size.Y)
            .Image(texture, scaleMode: ImageScaleMode.Fit);
        return true;
    }

    // An image with nothing but whitespace around it, or -1.
    private int SoleImage(int first)
    {
        int image = -1;
        for (int i = first; i >= 0; i = _document.GetInline(i).NextSibling)
        {
            var node = _document.GetInline(i);
            if (node.Kind == InlineKind.Image)
            {
                if (image >= 0) return -1;
                image = i;
            }
            else if (node.Kind != InlineKind.Text || !node.Text.AsSpan(_document.Source).IsWhiteSpace())
            {
                return -1;
            }
        }

        return image;
    }

    private bool HasLink(int first)
    {
        for (int i = first; i >= 0; i = _document.GetInline(i).NextSibling)
        {
            var node = _document.GetInline(i);
            if (node.Kind == InlineKind.Link || HasLink(node.FirstChild)) return true;
        }

        return false;
    }

    private void OnParagraphClicked(ClickEvent e)
    {
        string href = LinkUnder(e);
        if (href != null) _onLink?.Invoke(href);
    }

    // Hover runs every frame just before Paper settles the cursor, so the pointer shows over the link
    // itself and not over the rest of the paragraph.
    private void OnParagraphHovered(ElementEvent e)
    {
        e.Source.Data.Cursor = LinkUnder(e) != null ? PaperCursor.Pointer : PaperCursor.Inherit;
    }

    private string LinkUnder(ElementEvent e)
    {
        var paper = _paper;
        if (paper == null) return null;

        var block = paper.GetElementStorageById<RichTextBlock>(e.Source.Data.ID, Paper.RichTextBlockKey, null);
        if (block == null) return null;

        // Text is drawn inside the padding, and the block is laid out in physical pixels while the
        // event is in logical ones.
        var content = e.Source.Data.ContentRect.Min;
        float scale = paper.Canvas.FramebufferScale;
        return block.LinkAt(new Float2((e.RelativePosition.X - content.X) * scale, (e.RelativePosition.Y - content.Y) * scale));
    }

    private string CodeOf(int block)
    {
        return _blockText[block] ??= TrimTrailingNewline(_document.TextOf(_document.GetBlock(block).Text));
    }

    private string RichTextOf(int block, bool bold)
    {
        var cached = _blockText[block];
        if (cached != null) return cached;

        _scratch.Clear();
        if (bold) _scratch.Append("<b>");
        AppendInlines(_document.GetBlock(block).FirstInline);
        if (bold) _scratch.Append("</>");

        cached = _scratch.ToString();
        _blockText[block] = cached;
        return cached;
    }

    private void AppendInlines(int first)
    {
        for (int i = first; i >= 0; i = _document.GetInline(i).NextSibling)
        {
            var node = _document.GetInline(i);
            switch (node.Kind)
            {
                case InlineKind.Text:
                    AppendEscaped(node.Text);
                    break;

                case InlineKind.Span:
                    int opened = 0;
                    if ((node.Style & InlineStyle.Strong) != 0) { _scratch.Append("<b>"); opened++; }
                    if ((node.Style & InlineStyle.Emphasis) != 0) { _scratch.Append("<i>"); opened++; }
                    if ((node.Style & InlineStyle.Underline) != 0) { _scratch.Append("<u>"); opened++; }
                    if ((node.Style & InlineStyle.Strike) != 0) { _scratch.Append("<s>"); opened++; }
                    AppendInlines(node.FirstChild);
                    for (int k = 0; k < opened; k++) _scratch.Append("</>");
                    break;

                case InlineKind.Code:
                    _scratch.Append("<mono>");
                    AppendEscaped(node.Text);
                    _scratch.Append("</>");
                    break;

                case InlineKind.Link:
                    _scratch.Append("<link ");
                    // A tag ends at the first '>', which a valid URL never contains unencoded.
                    foreach (char c in node.Href.AsSpan(_document.Source))
                    {
                        if (c == '>') _scratch.Append("%3E");
                        else _scratch.Append(c);
                    }
                    _scratch.Append('>');
                    AppendColor(_link);
                    _scratch.Append("<u>");
                    AppendInlines(node.FirstChild);
                    _scratch.Append("</></></>");
                    break;

                case InlineKind.Image:
                    // The alt text, since there is no texture to show here.
                    AppendColor(_muted);
                    AppendEscaped(node.Text);
                    _scratch.Append("</>");
                    break;

                case InlineKind.LineBreak:
                    _scratch.Append('\n');
                    break;

                case InlineKind.SoftBreak:
                    _scratch.Append(' ');
                    break;
            }
        }
    }

    private void AppendEscaped(TextSpan span)
    {
        foreach (char c in span.AsSpan(_document.Source))
        {
            if (c == '<' || c == '\\') _scratch.Append('\\');
            _scratch.Append(c);
        }
    }

    private void AppendColor(Color color)
    {
        _scratch.Append("<#");
        AppendHex(color.R);
        AppendHex(color.G);
        AppendHex(color.B);
        AppendHex(color.A);
        _scratch.Append('>');
    }

    private void AppendHex(float channel)
    {
        int v = Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);
        _scratch.Append("0123456789abcdef"[v >> 4]);
        _scratch.Append("0123456789abcdef"[v & 15]);
    }

    private static float HeadingScale(int level) => level switch
    {
        1 => 1.6f,
        2 => 1.35f,
        3 => 1.2f,
        _ => 1f
    };

    private static TextAlignment CellAlignment(TableAlign align) => align switch
    {
        TableAlign.Center => TextAlignment.Center,
        TableAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left
    };

    private static string TrimTrailingNewline(string text)
    {
        int end = text.Length;
        while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;
        return end == text.Length ? text : text.Substring(0, end);
    }

    private static readonly string[] Ordinals = BuildOrdinals();

    private static string[] BuildOrdinals()
    {
        var ordinals = new string[100];
        for (int i = 0; i < ordinals.Length; i++) ordinals[i] = i + ".";
        return ordinals;
    }

    private static string OrdinalText(int ordinal) =>
        ordinal >= 0 && ordinal < Ordinals.Length ? Ordinals[ordinal] : ordinal + ".";
}
