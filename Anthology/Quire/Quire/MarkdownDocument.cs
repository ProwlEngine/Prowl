using System;
using System.Collections.Generic;

namespace Prowl.Quire;

/// <summary>
/// A parsed Markdown document: a flat array of blocks and one of inlines, linked into trees by
/// index. Nothing here copies the source, so a document is cheap to keep and cheap to re-parse
/// over the same instance.
/// </summary>
public sealed partial class MarkdownDocument
{
    private readonly List<Block> _blocks = new();
    private readonly List<Inline> _inlines = new();
    private string _source = string.Empty;

    public string Source => _source;
    public IReadOnlyList<Block> Blocks => _blocks;
    public IReadOnlyList<Inline> Inlines => _inlines;

    /// <summary>The document node. Its children are the top-level blocks.</summary>
    public int Root => _blocks.Count > 0 ? 0 : -1;

    public static MarkdownDocument Parse(string source)
    {
        var document = new MarkdownDocument();
        document.Load(source);
        return document;
    }

    /// <summary>Re-parse into this instance, reusing its storage. Every buffer a parse touches
    /// belongs to the document, so a document that is loaded again costs nothing.</summary>
    public void Load(string source)
    {
        _source = source ?? string.Empty;
        _blocks.Clear();
        _inlines.Clear();
        _linkDepth = 0;
        ParseDocument();
    }

    public Block GetBlock(int index) => _blocks[index];
    public Inline GetInline(int index) => _inlines[index];

    /// <summary>Walk a block's children.</summary>
    public BlockChildren ChildrenOf(int block) => new(this, block < 0 ? -1 : _blocks[block].FirstChild);

    /// <summary>Walk an inline's children.</summary>
    public InlineChildren InlineChildrenOf(int inline) => new(this, inline < 0 ? -1 : _inlines[inline].FirstChild);

    /// <summary>Walk the inlines attached to a block.</summary>
    public InlineChildren InlinesOf(int block) => new(this, block < 0 ? -1 : _blocks[block].FirstInline);

    public string TextOf(TextSpan span) => span.ToString(_source);

    /// <summary>The literal text of one inline and its descendants, ignoring styling.
    /// Siblings are not included; use <see cref="BlockText"/> for a whole run.</summary>
    public string PlainText(int inline)
    {
        var builder = new System.Text.StringBuilder();
        AppendPlain(inline, builder);
        return builder.ToString();
    }

    /// <summary>The literal text of an inline and everything after it at the same level, which is
    /// what a link label or any other child run is made of.</summary>
    public string PlainTextRun(int firstInline)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = firstInline; i >= 0; i = _inlines[i].NextSibling)
        {
            AppendPlain(i, builder);
        }

        return builder.ToString();
    }

    /// <summary>The literal text of every inline attached to a block, ignoring styling.</summary>
    public string BlockText(int block) => PlainTextRun(block < 0 ? -1 : _blocks[block].FirstInline);

    /// <summary>Appends the literal text of an inline and its descendants to a builder you own,
    /// which is how to read text out without a string being allocated for you.</summary>
    public void AppendPlain(int inline, System.Text.StringBuilder builder)
    {
        if (inline < 0)
        {
            return;
        }

        var node = _inlines[inline];
        if (node.Kind == InlineKind.Text || node.Kind == InlineKind.Code)
        {
            builder.Append(_source, node.Text.Start, node.Text.Length);
        }
        else if (node.Kind == InlineKind.LineBreak)
        {
            builder.Append('\n');
        }
        else if (node.Kind == InlineKind.SoftBreak)
        {
            builder.Append(' ');
        }

        for (int child = node.FirstChild; child >= 0; child = _inlines[child].NextSibling)
        {
            AppendPlain(child, builder);
        }
    }

    public readonly struct BlockChildren
    {
        private readonly MarkdownDocument _document;
        private readonly int _first;
        internal BlockChildren(MarkdownDocument document, int first) => (_document, _first) = (document, first);
        public Enumerator GetEnumerator() => new(_document, _first);

        public struct Enumerator
        {
            private readonly MarkdownDocument _document;
            private int _next;
            internal Enumerator(MarkdownDocument document, int first) => (_document, _next, Current) = (document, first, -1);
            public int Current { get; private set; }
            public bool MoveNext()
            {
                if (_next < 0)
                {
                    return false;
                }

                Current = _next;
                _next = _document._blocks[_next].NextSibling;
                return true;
            }
        }
    }

    public readonly struct InlineChildren
    {
        private readonly MarkdownDocument _document;
        private readonly int _first;
        internal InlineChildren(MarkdownDocument document, int first) => (_document, _first) = (document, first);
        public Enumerator GetEnumerator() => new(_document, _first);

        public struct Enumerator
        {
            private readonly MarkdownDocument _document;
            private int _next;
            internal Enumerator(MarkdownDocument document, int first) => (_document, _next, Current) = (document, first, -1);
            public int Current { get; private set; }
            public bool MoveNext()
            {
                if (_next < 0)
                {
                    return false;
                }

                Current = _next;
                _next = _document._inlines[_next].NextSibling;
                return true;
            }
        }
    }
}
