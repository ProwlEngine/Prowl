using Prowl.PaperUI.RichText;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class RichTextBlockTests
{
    private sealed class RecordingRenderer : IFontRenderer
    {
        public readonly List<IFontRenderer.Vertex> Vertices = new();

        public object CreateTexture(int width, int height) => new object();
        public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }

        public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
        {
            foreach (var v in vertices)
                Vertices.Add(v);
        }

        public void Clear() => Vertices.Clear();
    }

    private static FontFile LoadFont()
    {
        using var stream = typeof(RichTextBlockTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    private static (RichTextBlock block, FontSystem fonts, RecordingRenderer renderer, FontFile font) Build(
        string source, float maxWidth = 0f, float pixelSize = 20f)
    {
        var font = LoadFont();
        var renderer = new RecordingRenderer();
        var fonts = new FontSystem(renderer);
        var block = new RichTextBlock();

        var settings = RichTextSettings.Default(font, pixelSize, new Color(1f, 1f, 1f, 1f));
        settings.MaxWidth = maxWidth;
        settings.WrapMode = maxWidth > 0f ? TextWrapMode.Wrap : TextWrapMode.NoWrap;

        block.Update(source, settings, fonts);
        return (block, fonts, renderer, font);
    }

    private static List<Float2> DrawPositions(RichTextBlock block, FontSystem fonts, RecordingRenderer renderer, float time)
    {
        renderer.Clear();
        block.Draw(fonts, Float2.Zero, time);
        return renderer.Vertices.ConvertAll(v => new Float2(v.Position.X, v.Position.Y));
    }

    [Fact]
    public void TagsAreStrippedFromTheLaidOutText()
    {
        var (block, _, _, _) = Build("<b>Hello</> <shake>world</>");
        Assert.Equal("Hello world", block.VisibleText);
        Assert.Equal("Hello world", block.Layout.Text);
    }

    [Fact]
    public void UnstyledTextMeasuresExactlyAsScribeWouldOnItsOwn()
    {
        var font = LoadFont();
        var fonts = new FontSystem(new RecordingRenderer());

        var settings = TextLayoutSettings.Default;
        settings.Font = font;
        settings.PixelSize = 20f;
        var expected = fonts.MeasureText("Hello world", settings);

        var block = new RichTextBlock();
        block.Update("Hello world", RichTextSettings.Default(font, 20f, new Color(1f, 1f, 1f, 1f)), fonts);

        Assert.Equal(expected.X, block.Size.X, 4);
        Assert.Equal(expected.Y, block.Size.Y, 4);
    }

    [Fact]
    public void EffectTagsDoNotChangeWhereGlyphsAreShaped()
    {
        var (plain, _, _, _) = Build("Hello world");
        var (tagged, _, _, _) = Build("<wave>Hello</> <rainbow>world</>");

        Assert.Equal(plain.Size.X, tagged.Size.X, 4);
        Assert.Equal(plain.Layout.Lines.Count, tagged.Layout.Lines.Count);
    }

    [Fact]
    public void WrappingUsesScribesLineBreaking()
    {
        var (block, _, _, _) = Build("the quick brown fox jumps over the lazy dog", maxWidth: 80f);
        Assert.True(block.Layout.Lines.Count > 1, "text did not wrap");
        Assert.True(block.Size.X <= 80f + 1f, $"wrapped block was {block.Size.X} wide");
    }

    [Fact]
    public void StaticTextDrawsTheSameGeometryEveryFrame()
    {
        var (block, fonts, renderer, _) = Build("<b>steady</>");
        Assert.False(block.IsAnimated);

        var first = DrawPositions(block, fonts, renderer, 0f);
        var later = DrawPositions(block, fonts, renderer, 4.25f);

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, later.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].X, later[i].X, 5);
            Assert.Equal(first[i].Y, later[i].Y, 5);
        }
    }

    [Fact]
    public void AnimatedTextMovesGlyphsOverTime()
    {
        var (block, fonts, renderer, _) = Build("<wave>swaying</>");
        Assert.True(block.IsAnimated);

        var first = DrawPositions(block, fonts, renderer, 0f);
        var later = DrawPositions(block, fonts, renderer, 0.4f);

        Assert.Equal(first.Count, later.Count);
        bool moved = false;
        for (int i = 0; i < first.Count && !moved; i++)
            moved = Math.Abs(first[i].Y - later[i].Y) > 0.01f;

        Assert.True(moved, "no glyph moved between frames");
    }

    [Fact]
    public void AnimationNeverReshapesTheLayout()
    {
        var (block, fonts, renderer, _) = Build("<shake>jitter</>");
        var size = block.Size;

        DrawPositions(block, fonts, renderer, 1.5f);
        DrawPositions(block, fonts, renderer, 9f);

        Assert.Equal(size.X, block.Size.X, 5);
        Assert.Equal(size.Y, block.Size.Y, 5);
    }

    [Fact]
    public void ColorTagsRecolourOnlyTheirOwnRun()
    {
        var (block, fonts, renderer, _) = Build("aa<#ff0000>bb</>");
        var positions = DrawPositions(block, fonts, renderer, 0f);
        Assert.Equal(16, positions.Count); // four glyphs, four vertices each

        var first = renderer.Vertices[0].Color;
        var last = renderer.Vertices[^1].Color;

        Assert.Equal(255, first.R);
        Assert.Equal(255, first.G);
        Assert.Equal(255, last.R);
        Assert.Equal(0, last.G);
        Assert.Equal(0, last.B);
    }

    [Fact]
    public void LinksReportTheirHrefByCharacterIndex()
    {
        var (block, _, _, _) = Build("see <link https://prowlengine.com>Prowl</> now");
        Assert.Equal("see Prowl now", block.VisibleText);
        Assert.Null(block.LinkAtIndex(0));
        Assert.Equal("https://prowlengine.com", block.LinkAtIndex(4));
        Assert.Null(block.LinkAtIndex(12));
    }

    [Fact]
    public void LinksHitTestThroughScribe()
    {
        var (block, _, _, _) = Build("<link https://prowlengine.com>Prowl</>");
        var centre = new Float2(block.Size.X * 0.5f, block.Size.Y * 0.5f);
        Assert.Equal("https://prowlengine.com", block.LinkAt(centre));
    }

    [Fact]
    public void ChangingPixelSizeReshapes()
    {
        var (block, fonts, _, font) = Build("resize me");
        float small = block.Size.X;

        block.Update("resize me", RichTextSettings.Default(font, 40f, new Color(1f, 1f, 1f, 1f)), fonts);

        Assert.True(block.Size.X > small * 1.5f, $"{small} -> {block.Size.X}");
    }

    [Fact]
    public void SizeTagsChangeShapingRatherThanJustStretchingTheQuads()
    {
        var (plain, _, _, _) = Build("big text");
        var (sized, _, _, _) = Build("<size 2>big</> text");

        Assert.Equal("big text", sized.VisibleText);
        Assert.True(sized.Size.X > plain.Size.X, $"{plain.Size.X} -> {sized.Size.X}");
        Assert.True(sized.Size.Y > plain.Size.Y, "the line did not grow to fit the larger text");
    }

    [Fact]
    public void NestedSizeTagsMultiply()
    {
        var (plain, _, _, _) = Build("abcd");
        var (back, _, _, _) = Build("<size 2><size 0.5>abcd</></>");

        Assert.Equal(plain.Size.X, back.Size.X, 3);
        Assert.Equal(plain.Size.Y, back.Size.Y, 3);
    }

    [Fact]
    public void UnderlineAndStrikeTagsDrawBarsOnlyUnderTheirRun()
    {
        var (block, fonts, renderer, _) = Build("plain <u>under</> and <s>struck</>");
        var (plain, plainFonts, plainRenderer, _) = Build("plain under and struck");

        int Quads(RichTextBlock b, FontSystem f, RecordingRenderer r)
        {
            r.Clear();
            b.Draw(f, Float2.Zero, 0f);
            return r.Vertices.Count / 4;
        }

        // Same glyphs either way, plus exactly one bar for each decorated run.
        Assert.Equal(Quads(plain, plainFonts, plainRenderer) + 2, Quads(block, fonts, renderer));
        Assert.Equal(plain.Size.X, block.Size.X, 3);
    }

    [Fact]
    public void AnUnderlineTakesTheColourOfItsText()
    {
        var (block, fonts, renderer, _) = Build("<#ff0000><u>red</></>");
        renderer.Clear();
        block.Draw(fonts, Float2.Zero, 0f);

        // Three glyphs then the bar, and every vertex of the bar is red.
        Assert.Equal(16, renderer.Vertices.Count);
        for (int v = 12; v < 16; v++)
        {
            Assert.Equal(255, renderer.Vertices[v].Color.R);
            Assert.Equal(0, renderer.Vertices[v].Color.G);
        }
    }

    [Fact]
    public void ChangingOnlyTheColourStillRecolours()
    {
        var (block, fonts, renderer, font) = Build("tint");

        var red = RichTextSettings.Default(font, 20f, new Color(1f, 0f, 0f, 1f));
        block.Update("tint", red, fonts);

        renderer.Clear();
        block.Draw(fonts, Float2.Zero, 0f);
        Assert.Equal(255, renderer.Vertices[0].Color.R);
        Assert.Equal(0, renderer.Vertices[0].Color.G);
    }

    [Fact]
    public void TheInnermostColourWins()
    {
        var (block, fonts, renderer, _) = Build("<#ff0000>a<#0000ff>b</>c</>");
        renderer.Clear();
        block.Draw(fonts, Float2.Zero, 0f);

        // Glyphs a, b, c: the middle one is inside the blue tag.
        var b = renderer.Vertices[4].Color;
        Assert.Equal(0, b.R);
        Assert.Equal(255, b.B);
        Assert.Equal(255, renderer.Vertices[8].Color.R);
    }

    [Fact]
    public void ALinkIsHitWhereverItsOwnGlyphsAre()
    {
        var (block, _, _, _) = Build("ab<link x>cd</>ef");
        var layout = block.Layout;

        Float2 RightHalf(int i) { var r = layout.GetCharacterRect(i); return new Float2(r.X + r.Width * 0.75f, r.Y + r.Height * 0.5f); }
        Float2 LeftHalf(int i) { var r = layout.GetCharacterRect(i); return new Float2(r.X + r.Width * 0.25f, r.Y + r.Height * 0.5f); }

        // A caret would put these on the wrong side of the link's edges.
        Assert.Null(block.LinkAt(RightHalf(1)));
        Assert.Equal("x", block.LinkAt(LeftHalf(2)));
        Assert.Equal("x", block.LinkAt(RightHalf(3)));
        Assert.Null(block.LinkAt(LeftHalf(4)));
    }

    [Fact]
    public void NothingBelowTheTextIsALink()
    {
        var (block, _, _, _) = Build("<link x>link</>");
        var r = block.Layout.GetCharacterRect(1);
        Assert.Null(block.LinkAt(new Float2(r.X + 1f, r.Y + r.Height + 20f)));
    }

    [Fact]
    public void EmptyTextIsHarmless()
    {
        var (block, fonts, renderer, _) = Build("");
        Assert.Equal(string.Empty, block.VisibleText);
        Assert.Empty(DrawPositions(block, fonts, renderer, 0f));
    }
}
