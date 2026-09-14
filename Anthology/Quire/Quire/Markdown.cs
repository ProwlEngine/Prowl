using System;

namespace Prowl.Quire;

/// <summary>A range of the source text. Quire never copies source text, so every piece of
/// content in a document is one of these.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public bool IsEmpty => Length <= 0;
    public ReadOnlySpan<char> AsSpan(string source) => source.AsSpan(Start, Length);
    public string ToString(string source) => Length <= 0 ? string.Empty : source.Substring(Start, Length);
}

public enum BlockKind : byte
{
    /// <summary>The implicit container holding a document's outermost blocks.</summary>
    Document,
    Paragraph,
    Heading,
    BlockQuote,
    List,
    ListItem,
    CodeBlock,
    Table,
    TableRow,
    TableCell,
    HorizontalRule
}

public enum InlineKind : byte
{
    Text,
    /// <summary>A styled run. Its children carry the content.</summary>
    Span,
    Code,
    Link,
    Image,
    /// <summary>A hard line break: two or more spaces, or a backslash, at the end of a line.</summary>
    LineBreak,
    /// <summary>An ordinary line ending inside a paragraph. Markdown reads it as a space, so text
    /// wrapped in the source flows as one paragraph.</summary>
    SoftBreak
}

[Flags]
public enum InlineStyle : byte
{
    None = 0,
    Emphasis = 1 << 0,
    Strong = 1 << 1,
    Underline = 1 << 2,
    Strike = 1 << 3,
    Overline = 1 << 4
}

public enum TableAlign : byte
{
    None,
    Left,
    Center,
    Right
}

/// <summary>A block node. Children are a linked list through
/// <see cref="FirstChild"/> and <see cref="NextSibling"/>, ended by -1.</summary>
public readonly struct Block
{
    public readonly BlockKind Kind;
    /// <summary>Heading level 1-6, or a list item's ordinal.</summary>
    public readonly int Level;
    /// <summary>Ordered list, a checked task item, or a table's header row.</summary>
    public readonly bool Flag;
    public readonly TableAlign Align;
    /// <summary>Code block body.</summary>
    public readonly TextSpan Text;
    /// <summary>A fenced code block's info string, or a task item's <c>[ ]</c> marker. A list item
    /// with an empty <see cref="Info"/> is not a task, whatever <see cref="Flag"/> says.</summary>
    public readonly TextSpan Info;
    public readonly int FirstInline;
    public readonly int FirstChild;
    public readonly int NextSibling;

    internal Block(BlockKind kind, int level, bool flag, TableAlign align, TextSpan text, TextSpan info, int firstInline, int firstChild, int nextSibling)
    {
        Kind = kind;
        Level = level;
        Flag = flag;
        Align = align;
        Text = text;
        Info = info;
        FirstInline = firstInline;
        FirstChild = firstChild;
        NextSibling = nextSibling;
    }
}

/// <summary>An inline node. Children are a linked list through <see cref="FirstChild"/> and
/// <see cref="NextSibling"/>, ended by -1.</summary>
public readonly struct Inline
{
    public readonly InlineKind Kind;
    public readonly InlineStyle Style;
    /// <summary>Literal text, or the body of a code span.</summary>
    public readonly TextSpan Text;
    public readonly TextSpan Href;
    public readonly TextSpan Title;
    public readonly int FirstChild;
    public readonly int NextSibling;

    internal Inline(InlineKind kind, InlineStyle style, TextSpan text, TextSpan href, TextSpan title, int firstChild, int nextSibling)
    {
        Kind = kind;
        Style = style;
        Text = text;
        Href = href;
        Title = title;
        FirstChild = firstChild;
        NextSibling = nextSibling;
    }
}
