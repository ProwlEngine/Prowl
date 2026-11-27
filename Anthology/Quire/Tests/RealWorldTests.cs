using Prowl.Quire;

namespace Quire.Tests;

/// <summary>Cases the old Scribe parser handled, kept so replacing it loses none of them.</summary>
public class RealWorldTests
{
    private static MarkdownDocument Doc(string source) => MarkdownDocument.Parse(source);

    private static List<int> Kids(MarkdownDocument d, int block)
    {
        var list = new List<int>();
        foreach (int child in d.ChildrenOf(block)) list.Add(child);
        return list;
    }

    private static int Link(MarkdownDocument d, int block)
    {
        foreach (int i in d.InlinesOf(block))
        {
            if (d.GetInline(i).Kind == InlineKind.Link) return i;
        }

        return -1;
    }

    [Fact]
    public void ALinkUrlMayContainBalancedParentheses()
    {
        var d = Doc("[link](http://example.com/foo_(bar))\n");
        int link = Link(d, Kids(d, d.Root)[0]);
        Assert.True(link >= 0);
        Assert.Equal("http://example.com/foo_(bar)", d.TextOf(d.GetInline(link).Href));
    }

    [Fact]
    public void AnAutolinkLeavesTrailingPunctuationOut()
    {
        var d = Doc("Visit http://example.com.\n");
        int paragraph = Kids(d, d.Root)[0];
        int link = Link(d, paragraph);
        Assert.True(link >= 0);
        Assert.Equal("http://example.com", d.TextOf(d.GetInline(link).Href));
        Assert.EndsWith(".", d.BlockText(paragraph));
    }

    [Fact]
    public void AnAutolinkInsideParenthesesStopsBeforeTheClose()
    {
        var d = Doc("(http://x.com/q) end\n");
        int link = Link(d, Kids(d, d.Root)[0]);
        Assert.True(link >= 0);
        Assert.Equal("http://x.com/q", d.TextOf(d.GetInline(link).Href));
    }

    [Fact]
    public void AnEmptyListItemIsStillAnItem()
    {
        var d = Doc("- \n- b\n");
        var top = Kids(d, d.Root);
        Assert.Single(top);
        Assert.Equal(BlockKind.List, d.GetBlock(top[0]).Kind);
        Assert.Equal(2, Kids(d, top[0]).Count);
    }

    [Fact]
    public void ATabMaySeparateTheMarkerFromTheItem()
    {
        var d = Doc("-\tfirst\n-\tsecond\n");
        var top = Kids(d, d.Root);
        Assert.Single(top);
        var items = Kids(d, top[0]);
        Assert.Equal(2, items.Count);
        Assert.Equal("first", d.BlockText(Kids(d, items[0])[0]));
        Assert.Equal("second", d.BlockText(Kids(d, items[1])[0]));
    }

    [Fact]
    public void EscapedEmphasisMarkersAreLiteral()
    {
        var d = Doc(@"\*not italic\* and \*\*not bold\*\*");
        int paragraph = Kids(d, d.Root)[0];
        foreach (int i in d.InlinesOf(paragraph)) Assert.NotEqual(InlineKind.Span, d.GetInline(i).Kind);
        Assert.Equal("*not italic* and **not bold**", d.BlockText(paragraph));
    }

    [Fact]
    public void AsteriskBulletsMakeAList()
    {
        var d = Doc("* alpha\n* beta\n");
        var top = Kids(d, d.Root);
        Assert.Single(top);
        Assert.False(d.GetBlock(top[0]).Flag);
        Assert.Equal(2, Kids(d, top[0]).Count);
    }

    [Fact]
    public void ACodeFenceKeepsItsLanguage()
    {
        var d = Doc("```csharp\nConsole.WriteLine(\"hi\");\n```\n");
        var block = d.GetBlock(Kids(d, d.Root)[0]);
        Assert.Equal(BlockKind.CodeBlock, block.Kind);
        Assert.Equal("csharp", d.TextOf(block.Info));
        Assert.StartsWith("Console.WriteLine(\"hi\");", d.TextOf(block.Text));
    }

    private static int CountKind(MarkdownDocument d, InlineKind kind)
    {
        int n = 0;
        foreach (var inline in d.Inlines) if (inline.Kind == kind) n++;
        return n;
    }

    [Fact]
    public void BalancedParenthesesStayInABareUrl()
    {
        var d = Doc("see https://en.wikipedia.org/wiki/Foo_(bar) for more\n");
        int link = Link(d, Kids(d, d.Root)[0]);
        Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", d.TextOf(d.GetInline(link).Href));
    }

    [Fact]
    public void ASchemeGluedToAWordIsNotAUrl()
    {
        Assert.Equal(0, CountKind(Doc("xhttp://example.com\n"), InlineKind.Link));
    }

    [Fact]
    public void ASchemeWithNoHostIsNotAUrl()
    {
        Assert.Equal(0, CountKind(Doc("just http:// and nothing\n"), InlineKind.Link));
    }

    [Fact]
    public void AUrlInInlineCodeStaysCode()
    {
        var d = Doc("run `curl http://x.com` now\n");
        Assert.Equal(0, CountKind(d, InlineKind.Link));
        Assert.Equal(1, CountKind(d, InlineKind.Code));
    }

    [Fact]
    public void AUrlInALinkLabelDoesNotNestALink()
    {
        var d = Doc("[see http://x.com](http://y.com)\n");
        Assert.Equal(1, CountKind(d, InlineKind.Link));
        Assert.Equal("see http://x.com", d.BlockText(Kids(d, d.Root)[0]));
    }

    [Fact]
    public void AUrlEndingInThousandsOfParenthesesParsesQuickly()
    {
        string source = "http://x.com/" + new string(')', 20000) + " end";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var d = Doc(source);
        watch.Stop();

        int link = Link(d, Kids(d, d.Root)[0]);
        Assert.Equal("http://x.com/", d.TextOf(d.GetInline(link).Href));
        Assert.True(watch.ElapsedMilliseconds < 200, $"took {watch.ElapsedMilliseconds}ms");
    }
}
