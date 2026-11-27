using Prowl.Quire;

namespace Quire.Tests;

public class LineBreakTests
{
    private static List<InlineKind> Kinds(MarkdownDocument d)
    {
        var kinds = new List<InlineKind>();
        foreach (int i in d.InlinesOf(d.GetBlock(d.Root).FirstChild)) kinds.Add(d.GetInline(i).Kind);
        return kinds;
    }

    private static string Text(MarkdownDocument d) => d.BlockText(d.GetBlock(d.Root).FirstChild);

    [Fact]
    public void AnOrdinaryLineEndingIsASoftBreak()
    {
        var d = MarkdownDocument.Parse("one line\nand the next\n");
        Assert.Equal(new[] { InlineKind.Text, InlineKind.SoftBreak, InlineKind.Text }, Kinds(d));
        Assert.Equal("one line and the next", Text(d));
    }

    [Fact]
    public void TwoTrailingSpacesMakeAHardBreak()
    {
        var d = MarkdownDocument.Parse("one line  \nand the next\n");
        Assert.Equal(new[] { InlineKind.Text, InlineKind.LineBreak, InlineKind.Text }, Kinds(d));
        Assert.Equal("one line\nand the next", Text(d));
    }

    [Fact]
    public void ATrailingBackslashMakesAHardBreak()
    {
        var d = MarkdownDocument.Parse("one line\\\nand the next\n");
        Assert.Equal(new[] { InlineKind.Text, InlineKind.LineBreak, InlineKind.Text }, Kinds(d));
        Assert.Equal("one line\nand the next", Text(d));
    }

    [Fact]
    public void ASingleTrailingSpaceIsTrimmedAndStaysSoft()
    {
        var d = MarkdownDocument.Parse("one line \nand the next\n");
        Assert.Equal(InlineKind.SoftBreak, Kinds(d)[1]);
        Assert.Equal("one line and the next", Text(d));
    }

    [Fact]
    public void WindowsLineEndingsLeaveNoCarriageReturnInTheText()
    {
        var d = MarkdownDocument.Parse("one line\r\nand the next\r\n");
        Assert.DoesNotContain('\r', Text(d));
        Assert.Equal("one line and the next", Text(d));
    }

    [Fact]
    public void ABreakInsideEmphasisIsStillABreak()
    {
        var d = MarkdownDocument.Parse("**bold across\nlines**\n");
        Assert.Equal("bold across lines", Text(d));
    }
}
