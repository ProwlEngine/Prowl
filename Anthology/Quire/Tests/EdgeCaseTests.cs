using Prowl.Quire;

namespace Quire.Tests;

/// <summary>Awkward, malformed and real-world Markdown. Nothing here may throw, hang or lose text.</summary>
public class EdgeCaseTests
{
    private static MarkdownDocument Doc(string source) => MarkdownDocument.Parse(source);

    private static List<int> Kids(MarkdownDocument d, int block)
    {
        var blocks = new List<int>();
        foreach (int b in d.ChildrenOf(block))
        {
            blocks.Add(b);
        }

        return blocks;
    }

    private static List<int> Top(MarkdownDocument d) => Kids(d, d.Root);
    private static BlockKind Kind(MarkdownDocument d, int b) => d.GetBlock(b).Kind;

    // ---- line endings and framing ----

    [Fact]
    public void HandlesCrlfLineEndings()
    {
        var d = Doc("# Title\r\n\r\nbody text\r\n");
        var top = Top(d);
        Assert.Equal(2, top.Count);
        Assert.Equal("Title", d.BlockText(top[0]));
        Assert.Equal("body text", d.BlockText(top[1]));
    }

    [Fact]
    public void HandlesMissingTrailingNewline()
    {
        var d = Doc("# Title");
        Assert.Single(Top(d));
        Assert.Equal("Title", d.BlockText(Top(d)[0]));
    }

    [Fact]
    public void HandlesLeadingAndTrailingBlankLines()
    {
        var d = Doc("\n\n\nonly\n\n\n");
        Assert.Single(Top(d));
        Assert.Equal("only", d.BlockText(Top(d)[0]));
    }

    // ---- headings ----

    [Fact]
    public void HashWithoutSpaceIsNotAHeading()
    {
        var d = Doc("#hashtag not a heading\n");
        Assert.Equal(BlockKind.Paragraph, Kind(d, Top(d)[0]));
    }

    [Fact]
    public void SevenHashesIsNotAHeading()
    {
        var d = Doc("####### too deep\n");
        Assert.Equal(BlockKind.Paragraph, Kind(d, Top(d)[0]));
    }

    [Fact]
    public void SetextHeadingsAreRecognised()
    {
        var d = Doc("Title\n=====\n\nSub\n-----\n");
        var top = Top(d);
        Assert.Equal(BlockKind.Heading, Kind(d, top[0]));
        Assert.Equal(1, d.GetBlock(top[0]).Level);
        Assert.Equal("Title", d.BlockText(top[0]));
        Assert.Equal(BlockKind.Heading, Kind(d, top[1]));
        Assert.Equal(2, d.GetBlock(top[1]).Level);
        Assert.Equal("Sub", d.BlockText(top[1]));
    }

    [Fact]
    public void EmptyHeadingIsStillAHeading()
    {
        var d = Doc("## \n");
        Assert.Equal(BlockKind.Heading, Kind(d, Top(d)[0]));
        Assert.Equal(string.Empty, d.BlockText(Top(d)[0]));
    }

    // ---- code ----

    [Fact]
    public void IndentedCodeBlocksAreRecognised()
    {
        var d = Doc("text\n\n    code line one\n    code line two\n\nafter\n");
        var kinds = Top(d).Select(b => Kind(d, b)).ToList();
        Assert.Contains(BlockKind.CodeBlock, kinds);
    }

    [Fact]
    public void UnclosedFenceRunsToEndOfDocument()
    {
        var d = Doc("```\nnever closed\nstill code\n");
        var top = Top(d);
        Assert.Single(top);
        Assert.Equal(BlockKind.CodeBlock, Kind(d, top[0]));
        Assert.Contains("never closed", d.TextOf(d.GetBlock(top[0]).Text));
    }

    [Fact]
    public void LongerFenceIsNotClosedByShorterOne()
    {
        var d = Doc("````\n```\nstill inside\n````\n");
        var top = Top(d);
        Assert.Single(top);
        Assert.Contains("still inside", d.TextOf(d.GetBlock(top[0]).Text));
    }

    [Fact]
    public void TildeFencesWork()
    {
        var d = Doc("~~~python\nx = 1\n~~~\n");
        var block = d.GetBlock(Top(d)[0]);
        Assert.Equal(BlockKind.CodeBlock, block.Kind);
        Assert.Equal("python", d.TextOf(block.Info));
    }

    [Fact]
    public void EmptyFencedBlockIsEmptyNotCorrupt()
    {
        var d = Doc("```\n```\n");
        var block = d.GetBlock(Top(d)[0]);
        Assert.Equal(BlockKind.CodeBlock, block.Kind);
        Assert.Equal(string.Empty, d.TextOf(block.Text).Trim());
    }

    // ---- emphasis ----

    [Fact]
    public void IntrawordUnderscoresDoNotEmphasise()
    {
        var d = Doc("call snake_case_name and MAX_INT here\n");
        Assert.Equal("call snake_case_name and MAX_INT here", d.BlockText(Top(d)[0]));
    }

    [Fact]
    public void UnmatchedDelimitersStayLiteral()
    {
        var d = Doc("a * b and **unclosed here\n");
        Assert.Equal("a * b and **unclosed here", d.BlockText(Top(d)[0]));
    }

    [Fact]
    public void TripleAsteriskIsBoldItalic()
    {
        var d = Doc("***both***\n");
        int span = d.GetBlock(Top(d)[0]).FirstInline;
        Assert.Equal(InlineKind.Span, d.GetInline(span).Kind);
        Assert.True(d.GetInline(span).Style.HasFlag(InlineStyle.Strong));
        Assert.True(d.GetInline(span).Style.HasFlag(InlineStyle.Emphasis));
        Assert.Equal("both", d.PlainTextRun(d.GetInline(span).FirstChild));
    }

    [Fact]
    public void EmphasisNestsInsideStrong()
    {
        var d = Doc("**a *b* c**\n");
        Assert.Equal("a b c", d.BlockText(Top(d)[0]));
    }

    [Fact]
    public void EmphasisDoesNotSpanBlankLines()
    {
        var d = Doc("*start\n\nend*\n");
        Assert.Equal(2, Top(d).Count);
    }

    [Fact]
    public void AsteriskSurroundedBySpacesIsLiteral()
    {
        var d = Doc("2 * 3 * 4 = 24\n");
        Assert.Equal("2 * 3 * 4 = 24", d.BlockText(Top(d)[0]));
    }

    // ---- code spans ----

    [Fact]
    public void CodeSpanKeepsMarkdownLiteral()
    {
        var d = Doc("use `**not bold**` here\n");
        var codes = new List<string>();
        foreach (int i in d.InlinesOf(Top(d)[0]))
        {
            if (d.GetInline(i).Kind == InlineKind.Code)
            {
                codes.Add(d.TextOf(d.GetInline(i).Text));
            }
        }

        Assert.Equal(new[] { "**not bold**" }, codes);
    }

    [Fact]
    public void UnclosedCodeSpanStaysLiteral()
    {
        var d = Doc("a ` b c\n");
        Assert.Equal("a ` b c", d.BlockText(Top(d)[0]));
    }

    // ---- links ----

    [Fact]
    public void LinkWithParensInUrlIsBalanced()
    {
        var d = Doc("[wiki](https://x.dev/a_(b)_c)\n");
        int link = FirstOfKind(d, Top(d)[0], InlineKind.Link);
        Assert.Equal("https://x.dev/a_(b)_c", d.TextOf(d.GetInline(link).Href));
    }

    [Fact]
    public void LinkWithNestedBracketsInLabel()
    {
        var d = Doc("[a [b] c](x)\n");
        int link = FirstOfKind(d, Top(d)[0], InlineKind.Link);
        Assert.Equal("a [b] c", d.PlainTextRun(d.GetInline(link).FirstChild));
    }

    [Fact]
    public void UnclosedLinkStaysLiteral()
    {
        var d = Doc("[not a link\n");
        Assert.Equal("[not a link", d.BlockText(Top(d)[0]));
        Assert.Equal(-1, FirstOfKind(d, Top(d)[0], InlineKind.Link));
    }

    [Fact]
    public void BracketsWithoutParensStayLiteral()
    {
        var d = Doc("an [array] index\n");
        Assert.Equal("an [array] index", d.BlockText(Top(d)[0]));
    }

    [Fact]
    public void EmptyLinkTargetIsAllowed()
    {
        var d = Doc("[label]()\n");
        int link = FirstOfKind(d, Top(d)[0], InlineKind.Link);
        Assert.True(link >= 0);
        Assert.Equal(string.Empty, d.TextOf(d.GetInline(link).Href));
    }

    [Fact]
    public void AutolinksAreRecognised()
    {
        var d = Doc("visit <https://prowl.dev> now\n");
        int link = FirstOfKind(d, Top(d)[0], InlineKind.Link);
        Assert.True(link >= 0, "autolink was not recognised");
        Assert.Equal("https://prowl.dev", d.TextOf(d.GetInline(link).Href));
    }

    // ---- lists ----

    [Fact]
    public void OrderedListKeepsItsStartingNumber()
    {
        var d = Doc("5. five\n6. six\n");
        var items = Kids(d, Top(d)[0]);
        Assert.Equal(5, d.GetBlock(items[0]).Level);
        Assert.Equal(6, d.GetBlock(items[1]).Level);
    }

    [Fact]
    public void ParenDelimitedOrderedListWorks()
    {
        var d = Doc("1) one\n2) two\n");
        Assert.Equal(BlockKind.List, Kind(d, Top(d)[0]));
        Assert.Equal(2, Kids(d, Top(d)[0]).Count);
    }

    [Fact]
    public void LooseListWithBlankLinesKeepsAllItems()
    {
        var d = Doc("- a\n\n- b\n\n- c\n");
        var lists = Top(d).Where(b => Kind(d, b) == BlockKind.List).ToList();
        int items = lists.Sum(l => Kids(d, l).Count);
        Assert.Equal(3, items);
    }

    [Fact]
    public void EmptyListItemDoesNotCrash()
    {
        var d = Doc("- \n- b\n");
        Assert.Equal(BlockKind.List, Kind(d, Top(d)[0]));
        Assert.Equal(2, Kids(d, Top(d)[0]).Count);
    }

    [Fact]
    public void DashRuleAfterTextIsNotSwallowedByList()
    {
        var d = Doc("- item\n\n---\n");
        var kinds = Top(d).Select(b => Kind(d, b)).ToList();
        Assert.Contains(BlockKind.List, kinds);
        Assert.Contains(BlockKind.HorizontalRule, kinds);
    }

    // ---- quotes ----

    [Fact]
    public void NestedBlockQuotesNest()
    {
        var d = Doc("> outer\n>> inner\n");
        int quote = Top(d)[0];
        Assert.Equal(BlockKind.BlockQuote, Kind(d, quote));
        Assert.Contains("outer", d.BlockText(Kids(d, quote)[0]));
    }

    [Fact]
    public void QuoteWithoutSpaceAfterMarkerWorks()
    {
        var d = Doc(">tight\n");
        Assert.Equal(BlockKind.BlockQuote, Kind(d, Top(d)[0]));
        Assert.Equal("tight", d.BlockText(Kids(d, Top(d)[0])[0]));
    }

    // ---- tables ----

    [Fact]
    public void TableWithoutOuterPipesWorks()
    {
        var d = Doc("a | b\n--- | ---\n1 | 2\n");
        Assert.Equal(BlockKind.Table, Kind(d, Top(d)[0]));
        var rows = Kids(d, Top(d)[0]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, Kids(d, rows[1]).Count);
    }

    [Fact]
    public void PipeInsideCodeSpanDoesNotSplitCell()
    {
        var d = Doc("| a | b |\n|---|---|\n| `x \\| y` | 2 |\n");
        var rows = Kids(d, Top(d)[0]);
        Assert.Equal(2, Kids(d, rows[1]).Count);
    }

    [Fact]
    public void PipeTextThatIsNotATableStaysAParagraph()
    {
        var d = Doc("a | b | c\nno delimiter row here\n");
        Assert.Equal(BlockKind.Paragraph, Kind(d, Top(d)[0]));
    }

    // ---- robustness ----

    [Fact]
    public void DeeplyNestedStructuresDoNotOverflow()
    {
        var source = string.Concat(Enumerable.Repeat("> ", 200)) + "deep\n";
        var d = Doc(source);
        Assert.NotEmpty(Top(d));
    }

    [Fact]
    public void ManyUnclosedDelimitersStayLinear()
    {
        var source = string.Concat(Enumerable.Repeat("[", 2000)) + "\n";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var d = Doc(source);
        watch.Stop();
        Assert.NotEmpty(Top(d));
        Assert.True(watch.ElapsedMilliseconds < 500, $"pathological input took {watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void RandomGarbageNeverThrows()
    {
        var random = new Random(1234);
        const string alphabet = "#*_-`[]()|>~\\ \n\ta1";
        for (int i = 0; i < 400; i++)
        {
            var chars = new char[random.Next(0, 200)];
            for (int c = 0; c < chars.Length; c++)
            {
                chars[c] = alphabet[random.Next(alphabet.Length)];
            }

            var source = new string(chars);
            var document = MarkdownDocument.Parse(source);
            Assert.Same(source, document.Source);
        }
    }

    [Fact]
    public void EveryTextSpanStaysInsideTheSource()
    {
        const string source = "# H\n\npara **bold** `code` [l](u)\n\n- item\n\n> quote\n\n| a |\n|---|\n| 1 |\n";
        var d = MarkdownDocument.Parse(source);
        for (int i = 0; i < d.Blocks.Count; i++)
        {
            AssertInside(d.GetBlock(i).Text, source);
            AssertInside(d.GetBlock(i).Info, source);
        }

        for (int i = 0; i < d.Inlines.Count; i++)
        {
            AssertInside(d.GetInline(i).Text, source);
            AssertInside(d.GetInline(i).Href, source);
            AssertInside(d.GetInline(i).Title, source);
        }
    }

    private static void AssertInside(TextSpan span, string source)
    {
        Assert.True(span.Start >= 0, $"negative start {span.Start}");
        Assert.True(span.Length >= 0, $"negative length {span.Length}");
        Assert.True(span.Start + span.Length <= source.Length, $"span {span.Start}+{span.Length} runs past {source.Length}");
    }

    private static int FirstOfKind(MarkdownDocument d, int block, InlineKind kind)
    {
        foreach (int i in d.InlinesOf(block))
        {
            if (d.GetInline(i).Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }
}
