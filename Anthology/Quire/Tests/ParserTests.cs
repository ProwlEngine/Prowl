using Prowl.Quire;

namespace Quire.Tests;

public class ParserTests
{
    private static MarkdownDocument Doc(string source) => MarkdownDocument.Parse(source);

    private static List<int> TopLevel(MarkdownDocument d) => Kids(d, d.Root);

    private static List<int> Kids(MarkdownDocument d, int block)
    {
        var blocks = new List<int>();
        foreach (int b in d.ChildrenOf(block))
        {
            blocks.Add(b);
        }

        return blocks;
    }

    private static string Text(MarkdownDocument d, int block) => d.BlockText(block);

    [Fact]
    public void ParsesHeadingsAtEveryLevel()
    {
        var d = Doc("# One\n\n### Three\n\n###### Six\n");
        var blocks = TopLevel(d);
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(BlockKind.Heading, d.GetBlock(b).Kind));
        Assert.Equal(1, d.GetBlock(blocks[0]).Level);
        Assert.Equal(3, d.GetBlock(blocks[1]).Level);
        Assert.Equal(6, d.GetBlock(blocks[2]).Level);
        Assert.Equal("One", Text(d, blocks[0]));
        Assert.Equal("Six", Text(d, blocks[2]));
    }

    [Fact]
    public void HeadingDropsTrailingHashes()
    {
        var d = Doc("## Title ##\n");
        Assert.Equal("Title", Text(d, TopLevel(d)[0]));
    }

    [Fact]
    public void ParagraphsJoinWrappedLinesAndSplitOnBlank()
    {
        var d = Doc("one\ntwo\n\nthree\n");
        var blocks = TopLevel(d);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(BlockKind.Paragraph, d.GetBlock(blocks[0]).Kind);
        Assert.Equal("one two", Text(d, blocks[0]));
        Assert.Equal("three", Text(d, blocks[1]));
    }

    [Fact]
    public void ParsesEmphasisStrongAndStrike()
    {
        var d = Doc("a *em* b **strong** c ~~gone~~\n");
        var styles = new List<InlineStyle>();
        foreach (int i in d.InlinesOf(TopLevel(d)[0]))
        {
            if (d.GetInline(i).Kind == InlineKind.Span)
            {
                styles.Add(d.GetInline(i).Style);
            }
        }

        Assert.Equal(new[] { InlineStyle.Emphasis, InlineStyle.Strong, InlineStyle.Strike }, styles);
    }

    [Fact]
    public void ParsesInlineCodeIncludingBackticksInside()
    {
        var d = Doc("call `a + b` then ``has ` tick``\n");
        var codes = new List<string>();
        foreach (int i in d.InlinesOf(TopLevel(d)[0]))
        {
            if (d.GetInline(i).Kind == InlineKind.Code)
            {
                codes.Add(d.TextOf(d.GetInline(i).Text));
            }
        }

        Assert.Equal(new[] { "a + b", "has ` tick" }, codes);
    }

    [Fact]
    public void ParsesLinksWithTitlesAndImages()
    {
        var d = Doc("see [the docs](https://x.dev \"Docs\") and ![alt](pic.png)\n");
        int link = -1, image = -1;
        foreach (int i in d.InlinesOf(TopLevel(d)[0]))
        {
            if (d.GetInline(i).Kind == InlineKind.Link) link = i;
            if (d.GetInline(i).Kind == InlineKind.Image) image = i;
        }

        Assert.True(link >= 0 && image >= 0);
        Assert.Equal("https://x.dev", d.TextOf(d.GetInline(link).Href));
        Assert.Equal("Docs", d.TextOf(d.GetInline(link).Title));
        Assert.Equal("the docs", d.PlainTextRun(d.GetInline(link).FirstChild));
        Assert.Equal("pic.png", d.TextOf(d.GetInline(image).Href));
        Assert.Equal("alt", d.TextOf(d.GetInline(image).Text));
    }

    [Fact]
    public void LinkLabelKeepsItsOwnFormatting()
    {
        var d = Doc("[a **bold** label](x)\n");
        int link = -1;
        foreach (int i in d.InlinesOf(TopLevel(d)[0]))
        {
            if (d.GetInline(i).Kind == InlineKind.Link) link = i;
        }

        Assert.Equal("a bold label", d.PlainTextRun(d.GetInline(link).FirstChild));
    }

    [Fact]
    public void ParsesFencedCodeWithLanguage()
    {
        var d = Doc("```csharp\nvar x = 1;\nvar y = 2;\n```\n");
        var block = d.GetBlock(TopLevel(d)[0]);
        Assert.Equal(BlockKind.CodeBlock, block.Kind);
        Assert.Equal("csharp", d.TextOf(block.Info));
        Assert.Equal("var x = 1;\nvar y = 2;", d.TextOf(block.Text).Replace("\r\n", "\n").TrimEnd());
    }

    [Fact]
    public void FencedCodeDoesNotParseMarkdownInside()
    {
        var d = Doc("```\n# not a heading\n**not bold**\n```\n");
        var blocks = TopLevel(d);
        Assert.Single(blocks);
        Assert.Equal(BlockKind.CodeBlock, d.GetBlock(blocks[0]).Kind);
    }

    [Fact]
    public void ParsesHorizontalRulesButNotBullets()
    {
        var d = Doc("---\n\n- item\n\n***\n");
        var kinds = TopLevel(d).Select(b => d.GetBlock(b).Kind).ToList();
        Assert.Equal(new[] { BlockKind.HorizontalRule, BlockKind.List, BlockKind.HorizontalRule }, kinds);
    }

    [Fact]
    public void ParsesUnorderedAndOrderedLists()
    {
        var d = Doc("- a\n- b\n\n1. one\n2. two\n");
        var blocks = TopLevel(d);
        Assert.Equal(2, blocks.Count);
        Assert.False(d.GetBlock(blocks[0]).Flag);
        Assert.True(d.GetBlock(blocks[1]).Flag);

        var items = new List<string>();
        foreach (int item in d.ChildrenOf(blocks[0]))
        {
            foreach (int p in d.ChildrenOf(item))
            {
                items.Add(Text(d, p));
            }
        }

        Assert.Equal(new[] { "a", "b" }, items);
    }

    [Fact]
    public void ParsesTaskListItems()
    {
        var d = Doc("- [ ] todo\n- [x] done\n- plain\n");
        var list = TopLevel(d)[0];
        var items = Kids(d, list).ToList();
        Assert.Equal(3, items.Count);
        Assert.False(d.GetBlock(items[0]).Flag);
        Assert.True(d.GetBlock(items[1]).Flag);

        // An unchecked task and a plain item both have Flag false; the marker is what tells them apart.
        Assert.Equal("[ ]", d.TextOf(d.GetBlock(items[0]).Info));
        Assert.Equal("[x]", d.TextOf(d.GetBlock(items[1]).Info));
        Assert.True(d.GetBlock(items[2]).Info.IsEmpty);
        Assert.Equal("todo", Text(d, d.GetBlock(items[0]).FirstChild));
    }

    [Fact]
    public void ParsesNestedLists()
    {
        var d = Doc("- outer\n  - inner\n");
        var outer = TopLevel(d)[0];
        int item = d.GetBlock(outer).FirstChild;
        var kinds = Kids(d, item).Select(b => d.GetBlock(b).Kind).ToList();
        Assert.Contains(BlockKind.Paragraph, kinds);
        Assert.Contains(BlockKind.List, kinds);
    }

    [Fact]
    public void ParsesBlockQuotes()
    {
        var d = Doc("> quoted\n> more\n");
        var quote = TopLevel(d)[0];
        Assert.Equal(BlockKind.BlockQuote, d.GetBlock(quote).Kind);
        var lines = Kids(d, quote).Select(b => Text(d, b)).ToList();
        Assert.Equal(new[] { "quoted", "more" }, lines);
    }

    [Fact]
    public void ParsesTablesWithAlignment()
    {
        var d = Doc("| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |\n");
        var table = TopLevel(d)[0];
        Assert.Equal(BlockKind.Table, d.GetBlock(table).Kind);
        var rows = Kids(d, table).ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(d.GetBlock(rows[0]).Flag, "first row should be the header");

        var aligns = Kids(d, rows[1]).Select(c => d.GetBlock(c).Align).ToList();
        Assert.Equal(new[] { TableAlign.Left, TableAlign.Center, TableAlign.Right }, aligns);
        var cells = Kids(d, rows[1]).Select(c => Text(d, c)).ToList();
        Assert.Equal(new[] { "1", "2", "3" }, cells);
    }

    [Fact]
    public void HonoursBackslashEscapes()
    {
        var d = Doc(@"not \*emphasis\* here" + "\n");
        Assert.Equal("not *emphasis* here", Text(d, TopLevel(d)[0]));
    }

    [Fact]
    public void SpansNeverCopySourceText()
    {
        const string source = "# Title\n\nbody **bold**\n";
        var d = MarkdownDocument.Parse(source);
        Assert.Same(source, d.Source);
        var heading = d.GetBlock(TopLevel(d)[0]);
        int inline = heading.FirstInline;
        var span = d.GetInline(inline).Text;
        Assert.Equal("Title", source.AsSpan(span.Start, span.Length).ToString());
    }

    [Fact]
    public void ReparsingReusesTheSameDocument()
    {
        var d = MarkdownDocument.Parse("# One\n");
        Assert.Equal("One", Text(d, TopLevel(d)[0]));

        d.Load("# Two\n\nand a paragraph\n");
        Assert.Equal(2, TopLevel(d).Count);
        Assert.Equal("Two", Text(d, TopLevel(d)[0]));
    }

    [Fact]
    public void EmptyAndWhitespaceInputAreSafe()
    {
        Assert.Empty(TopLevel(Doc(string.Empty)));
        Assert.Empty(TopLevel(Doc("   \n\n  \n")));
    }
}
