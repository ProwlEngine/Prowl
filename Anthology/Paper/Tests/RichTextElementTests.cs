using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class RichTextElementTests
{
    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls) { }
    }

    private static readonly FontFile Font = LoadFont();

    private static FontFile LoadFont()
    {
        using var stream = typeof(RichTextElementTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    private static Paper NewPaper() => new(new NullRenderer(), 400, 300, new FontAtlasSettings());

    private static float Width(Paper p, string text, bool rich)
    {
        p.BeginFrame(.01f);
        var b = p.Box("text", lineID: 1).Size(UnitValue.Auto).Text(text, Font).FontSize(20);
        if (rich) b.RichText();
        p.EndFrame();
        return b._handle.Data.LayoutWidth;
    }

    [Fact]
    public void TagsTakeNoRoom()
    {
        float plain = Width(NewPaper(), "hello world", rich: false);
        float rich = Width(NewPaper(), "<b>hello</> <wave>world</>", rich: true);

        // No bold face was given, so bold falls back to the regular font and the widths agree.
        Assert.Equal(plain, rich, 2);
    }

    [Fact]
    public void WithoutTheToggleTagsAreJustText()
    {
        float tagged = Width(NewPaper(), "<b>hello</>", rich: false);
        float plain = Width(NewPaper(), "hello", rich: false);
        Assert.True(tagged > plain, $"{plain} vs {tagged}");
    }

    [Fact]
    public void SizeTagsGrowTheElement()
    {
        float plain = Width(NewPaper(), "big", rich: true);
        float sized = Width(NewPaper(), "<size 2>big</>", rich: true);
        Assert.True(sized > plain * 1.5f, $"{plain} -> {sized}");
    }

    [Fact]
    public void TheElementFollowsItsTextAcrossFrames()
    {
        var p = NewPaper();
        float short_ = Width(p, "<wave>hi</>", rich: true);
        float long_ = Width(p, "<wave>hi there</>", rich: true);
        float back = Width(p, "<wave>hi</>", rich: true);

        Assert.True(long_ > short_);
        Assert.Equal(short_, back, 2);
    }

    [Fact]
    public void AnUnchangedRichTextElementIsNotMeasuredAgain()
    {
        var p = NewPaper();
        Width(p, "<shake>steady</>", rich: true);
        Width(p, "<shake>steady</>", rich: true);
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
    }

    [Fact]
    public void AMaskHidesRichTextInsteadOfDrawingIt()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        var masked = p.Box("m", lineID: 1).Size(UnitValue.Auto).Text("<b>secret</>", Font).FontSize(20).RichText().IsPassword('W');
        var plain = p.Box("p", lineID: 2).Size(UnitValue.Auto).Text("WWWWWWWWWWWW", Font).FontSize(20);
        p.EndFrame();

        // Twelve characters of source, all drawn as the mask, tags included.
        Assert.Equal(plain._handle.Data.LayoutWidth, masked._handle.Data.LayoutWidth, 2);
    }

    [Fact]
    public void TogglingAMaskRemeasures()
    {
        var p = NewPaper();
        float Measure(bool masked)
        {
            p.BeginFrame(.01f);
            var b = p.Box("t", lineID: 1).Size(UnitValue.Auto).Text("iiii", Font).FontSize(20);
            if (masked) b.IsPassword('W');
            p.EndFrame();
            return b._handle.Data.LayoutWidth;
        }

        float plain = Measure(false);
        float masked = Measure(true);
        Assert.True(masked > plain * 2f, $"{plain} -> {masked}");
        Assert.Equal(plain, Measure(false), 2);
    }

    [Fact]
    public void CentredRichTextSitsWhereCentredPlainTextDoes()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        var rich = p.Box("r", lineID: 1).Width(300).Height(40).Alignment(Prowl.PaperUI.TextAlignment.Center).Text("<wave>mid</>", Font).FontSize(20).RichText();
        var plain = p.Box("p", lineID: 2).Width(300).Height(40).Alignment(Prowl.PaperUI.TextAlignment.Center).Text("mid", Font).FontSize(20);
        p.EndFrame();

        float richX = rich._handle.Data._richText.Layout.Lines[0].Glyphs[0].Position.X;
        float plainX = plain._handle.Data._textLayout.Lines[0].Glyphs[0].Position.X;
        Assert.True(plainX > 50f, $"plain text was not centred at all ({plainX})");
        Assert.Equal(plainX, richX, 1);
    }

    [Fact]
    public void AnimatedRichTextDrawsFrameAfterFrame()
    {
        var p = NewPaper();
        for (int i = 0; i < 30; i++)
            Width(p, "<wave>a</> <rainbow>b</> <u>c</> <#f80>d</> <pendulum 2 3>e</>", rich: true);
    }
}
