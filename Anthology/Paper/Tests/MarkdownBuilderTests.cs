using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Markdown;
using Prowl.PaperUI.RichText;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class MarkdownBuilderTests
{
    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => texture is Int2 size ? size : new Int2(0, 0);
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls) { }
    }

    private static readonly FontFile Font = LoadFont();

    private static FontFile LoadFont()
    {
        using var stream = typeof(MarkdownBuilderTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    private static Paper NewPaper() => new(new NullRenderer(), 600, 800, new FontAtlasSettings());

    private static ElementHandle Frame(Paper p, MarkdownBuilder md)
    {
        p.BeginFrame(.01f);
        ElementBuilder root;
        using (p.Column("page", lineID: 1).Width(600).Height(800).Enter())
        {
            root = md.Build(p, "doc");
        }
        p.EndFrame();
        return root._handle;
    }

    private static List<ElementHandle> Children(ElementHandle parent)
    {
        var list = new List<ElementHandle>();
        foreach (int index in parent.Data.ChildIndices) list.Add(new ElementHandle(parent.Owner, index));
        return list;
    }

    // Paper sees a click as a press and a release over the same element, one frame apart, after a
    // frame in which the pointer has already arrived there.
    private static void Click(Paper p, MarkdownBuilder md, float x, float y)
    {
        p.SetPointerState(PaperMouseBtn.Left, x, y, false, true);
        Frame(p, md);
        p.SetPointerState(PaperMouseBtn.Left, x, y, true, false);
        Frame(p, md);
        p.SetPointerState(PaperMouseBtn.Left, x, y, false, false);
        Frame(p, md);
    }

    private static string Visible(Paper p, ElementHandle element) =>
        p.GetElementStorageById<RichTextBlock>(element.Data.ID, Paper.RichTextBlockKey, null)!.VisibleText;

    [Fact]
    public void EveryTopLevelBlockBecomesAnElement()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("# Title\n\nsome prose\n\n---\n\n```\ncode\n```\n"));
        Assert.Equal(4, Children(doc).Count);
    }

    [Fact]
    public void InlineStylingBecomesRichTextThatDrawsOnlyTheWords()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("a **bold** and *italic* `code` ~~gone~~ word\n"));
        var paragraph = Children(doc)[0];

        Assert.True(paragraph.Data.IsRichText);
        Assert.Equal("a bold and italic code gone word", Visible(p, paragraph));
    }

    [Fact]
    public void AngleBracketsInProseAreNotMistakenForTags()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("if a <b and c> d, 2 < 3 \\ done\n"));
        Assert.Equal("if a <b and c> d, 2 < 3 \\ done", Visible(p, Children(doc)[0]));
    }

    [Fact]
    public void CodeBlocksAreLiteralText()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("```\n<b>not bold</b>\n```\n"));
        var code = Children(doc)[0];

        Assert.False(code.Data.IsRichText);
        Assert.Equal("<b>not bold</b>", code.Data.Paragraph);
    }

    [Fact]
    public void HeadingsAreLargerThanBodyText()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).FontSize(16).Source("# Big\n\nsmall\n"));
        var kids = Children(doc);
        Assert.True(kids[0].Data.LayoutHeight > kids[1].Data.LayoutHeight * 1.3f,
            $"{kids[1].Data.LayoutHeight} vs {kids[0].Data.LayoutHeight}");
    }

    [Fact]
    public void ListsAreARowPerItemWithAMarker()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("1. one\n2. two\n3. three\n"));
        var items = Children(Children(doc)[0]);

        Assert.Equal(3, items.Count);
        Assert.Equal("2.", Children(items[1])[0].Data.Paragraph);
    }

    [Fact]
    public void TaskItemsDrawACheckboxAndPlainItemsDoNot()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("- [ ] todo\n- [x] done\n- plain\n"));
        var items = Children(Children(doc)[0]);

        // A task marker is a box holding the checkbox, and a bullet is text.
        Assert.Single(Children(Children(items[0])[0]));
        Assert.Single(Children(Children(items[1])[0]));
        Assert.Equal("\u2022", Children(items[2])[0].Data.Paragraph);
    }

    [Fact]
    public void TablesAreRowsOfCells()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("| a | b |\n|---|--:|\n| 1 | 2 |\n| 3 | 4 |\n"));
        var rows = Children(Children(doc)[0]);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(2, Children(r).Count));
        Assert.Equal(Prowl.PaperUI.TextAlignment.Right, Children(rows[1])[1].Data.TextAlignment);
    }

    [Fact]
    public void BuildingTheSameDocumentAgainDoesNotRemeasureIt()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).Source("# Title\n\n- a\n- b\n\nsome **prose** here\n");
        Frame(p, md);
        Frame(p, md.Source("# Title\n\n- a\n- b\n\nsome **prose** here\n"));
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
    }

    [Fact]
    public void ChangingTheSourceRebuilds()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).Source("one\n");
        Assert.Single(Children(Frame(p, md)));
        Assert.Equal(3, Children(Frame(p, md.Source("one\n\ntwo\n\nthree\n"))).Count);
    }

    [Fact]
    public void ClickingALinkReportsItsHref()
    {
        var p = NewPaper();
        string? clicked = null;
        var md = new MarkdownBuilder(Font).FontSize(20).OnLink(h => clicked = h).Source("[Prowl](https://prowlengine.com)\n");

        var paragraph = Children(Frame(p, md))[0];
        var block = p.GetElementStorageById<RichTextBlock>(paragraph.Data.ID, Paper.RichTextBlockKey, null)!;
        float x = paragraph.Data.X + block.Size.X * 0.5f / p.Canvas.FramebufferScale;
        float y = paragraph.Data.Y + block.Size.Y * 0.5f / p.Canvas.FramebufferScale;

        Click(p, md, x, y);

        Assert.Equal("https://prowlengine.com", clicked);
    }

    [Fact]
    public void ClickingPlainTextBesideALinkReportsNothing()
    {
        var p = NewPaper();
        string? clicked = null;
        var md = new MarkdownBuilder(Font).FontSize(20).OnLink(h => clicked = h)
            .Source("[Prowl](https://prowlengine.com) and then a great deal of ordinary words\n");

        var paragraph = Children(Frame(p, md))[0];
        var block = p.GetElementStorageById<RichTextBlock>(paragraph.Data.ID, Paper.RichTextBlockKey, null)!;
        float x = paragraph.Data.X + block.Size.X * 0.9f / p.Canvas.FramebufferScale;
        float y = paragraph.Data.Y + block.Size.Y * 0.5f / p.Canvas.FramebufferScale;

        Click(p, md, x, y);

        Assert.Null(clicked);
    }

    private static PaperCursor HoverAt(Paper p, MarkdownBuilder md, float x, float y)
    {
        p.SetPointerState(PaperMouseBtn.Left, x, y, false, true);
        Frame(p, md);
        return p.CurrentCursor;
    }

    [Fact]
    public void HoveringALinkShowsThePointer()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).FontSize(20).OnLink(_ => { }).Source("[Prowl](https://prowlengine.com) and ordinary words after it\n");

        var paragraph = Children(Frame(p, md))[0];
        var block = p.GetElementStorageById<RichTextBlock>(paragraph.Data.ID, Paper.RichTextBlockKey, null)!;
        float scale = p.Canvas.FramebufferScale;
        float midY = paragraph.Data.Y + block.Size.Y * 0.5f / scale;

        // Somewhere inside "Prowl", then well into the plain words, then back.
        Assert.Equal(PaperCursor.Pointer, HoverAt(p, md, paragraph.Data.X + 8, midY));
        Assert.NotEqual(PaperCursor.Pointer, HoverAt(p, md, paragraph.Data.X + block.Size.X * 0.8f / scale, midY));
        Assert.Equal(PaperCursor.Pointer, HoverAt(p, md, paragraph.Data.X + 8, midY));
    }

    [Fact]
    public void HoveringEmptySpacePastALinkDoesNotShowThePointer()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).FontSize(20).OnLink(_ => { }).Source("plain words then [a link](https://prowlengine.com)\n");

        var paragraph = Children(Frame(p, md))[0];
        var block = p.GetElementStorageById<RichTextBlock>(paragraph.Data.ID, Paper.RichTextBlockKey, null)!;
        float scale = p.Canvas.FramebufferScale;
        float midY = paragraph.Data.Y + block.Size.Y * 0.5f / scale;

        // The paragraph stretches to the page, so there is plenty of empty width after the link.
        float pastTheEnd = paragraph.Data.X + block.Size.X / scale + 100;
        Assert.True(pastTheEnd < paragraph.Data.X + paragraph.Data.LayoutWidth, "no room past the text to test");
        Assert.NotEqual(PaperCursor.Pointer, HoverAt(p, md, pastTheEnd, midY));
    }

    [Fact]
    public void WithoutALinkHandlerThereIsNoPointer()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).FontSize(20).Source("[Prowl](https://prowlengine.com)\n");
        var paragraph = Children(Frame(p, md))[0];
        Assert.NotEqual(PaperCursor.Pointer, HoverAt(p, md, paragraph.Data.X + 8, paragraph.Data.Y + 10));
    }

    [Fact]
    public void ALinkInATableCellIsClickable()
    {
        var p = NewPaper();
        string? clicked = null;
        var md = new MarkdownBuilder(Font).FontSize(20).OnLink(h => clicked = h)
            .Source("| name |\n|---|\n| [Prowl](https://prowlengine.com) |\n");

        var row = Children(Children(Frame(p, md))[0])[1];
        var cell = Children(row)[0];
        var block = p.GetElementStorageById<RichTextBlock>(cell.Data.ID, Paper.RichTextBlockKey, null)!;
        float scale = p.Canvas.FramebufferScale;
        var content = cell.Data.ContentRect.Min;
        float x = cell.Data.X + content.X + block.Size.X * 0.5f / scale;
        float y = cell.Data.Y + content.Y + block.Size.Y * 0.5f / scale;

        Click(p, md, x, y);

        Assert.Equal("https://prowlengine.com", clicked);
    }

    [Fact]
    public void SourceWrappedAcrossLinesFlowsAsOneParagraph()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("one line\nand the next\n"));
        Assert.Equal("one line and the next", Visible(p, Children(doc)[0]));
    }

    [Fact]
    public void ABlockQuoteBarRunsTheHeightOfTheQuote()
    {
        var p = NewPaper();
        var doc = Frame(p, new MarkdownBuilder(Font).Source("> first line of the quote\n>\n> and a second paragraph\n"));
        var quote = Children(Children(doc)[0]);
        var bar = quote[0].Data;
        var body = quote[1].Data;

        Assert.True(body.LayoutHeight > 20f, $"the quote body is only {body.LayoutHeight} tall");
        Assert.Equal(body.LayoutHeight, bar.LayoutHeight, 1);
    }

    [Fact]
    public void AnImageOnItsOwnLineIsAnImageAtItsNaturalAspect()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).Images(src => src == "pic.png" ? new Int2(200, 100) : null).Source("![alt](pic.png)\n");
        var image = Children(Frame(p, md))[0];

        Assert.False(image.Data.IsRichText);
        Assert.Equal(200, image.Data.LayoutWidth, 1);
        Assert.Equal(100, image.Data.LayoutHeight, 1);
    }

    [Fact]
    public void AnImageThatDoesNotResolveShowsItsAltText()
    {
        var p = NewPaper();
        var md = new MarkdownBuilder(Font).Images(_ => null!).Source("![a cat](missing.png)\n");
        Assert.Equal("a cat", Visible(p, Children(Frame(p, md))[0]));
    }
}
