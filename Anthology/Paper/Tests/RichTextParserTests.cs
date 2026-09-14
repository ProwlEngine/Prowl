using Prowl.PaperUI.RichText;
using Prowl.Vector;

namespace Tests;

public class RichTextParserTests
{
    private static RichTextParser Parse(string source)
    {
        var parser = new RichTextParser();
        parser.Parse(source);
        return parser;
    }

    [Fact]
    public void StripsTagsFromTheVisibleText()
    {
        var p = Parse("<wave 2 8>hello</> world");
        Assert.Equal("hello world", p.VisibleText);
    }

    [Fact]
    public void RecordsEffectSpansOverVisibleIndices()
    {
        var p = Parse("ab<shake>cd</>ef");
        Assert.Single(p.Effects);
        var span = p.Effects[0];
        Assert.Equal(RichEffect.Shake, span.Kind);
        Assert.Equal(2, span.Start);
        Assert.Equal(4, span.End);
        Assert.Equal("abcdef", p.VisibleText);
    }

    [Fact]
    public void ReadsPositionalStrengthAndSpeed()
    {
        var p = Parse("<wave 2.5 8>x</>");
        Assert.Equal(2.5f, p.Effects[0].Strength);
        Assert.Equal(8f, p.Effects[0].Speed);
    }

    [Fact]
    public void OmittedParametersAreNaNSoDefaultsApply()
    {
        var p = Parse("<wave>x</>");
        Assert.True(float.IsNaN(p.Effects[0].Strength));
        Assert.True(float.IsNaN(p.Effects[0].Speed));
    }

    [Fact]
    public void CommaSeparatedParametersWork()
    {
        var p = Parse("<wave 2,8>x</>");
        Assert.Equal(2f, p.Effects[0].Strength);
        Assert.Equal(8f, p.Effects[0].Speed);
    }

    [Theory]
    [InlineData("shake", RichEffect.Shake)]
    [InlineData("wiggle", RichEffect.Wiggle)]
    [InlineData("wave", RichEffect.Wave)]
    [InlineData("bounce", RichEffect.Bounce)]
    [InlineData("slide", RichEffect.Slide)]
    [InlineData("swell", RichEffect.Swell)]
    [InlineData("sizewave", RichEffect.Swell)]
    [InlineData("pulse", RichEffect.Pulse)]
    [InlineData("rotate", RichEffect.Rotate)]
    [InlineData("spin", RichEffect.Rotate)]
    [InlineData("swing", RichEffect.Swing)]
    [InlineData("pendulum", RichEffect.Pendulum)]
    [InlineData("dangle", RichEffect.Dangle)]
    [InlineData("rainbow", RichEffect.Rainbow)]
    public void EveryEffectNameResolves(string tag, RichEffect expected)
    {
        var p = Parse($"<{tag}>x</>");
        Assert.Single(p.Effects);
        Assert.Equal(expected, p.Effects[0].Kind);
    }

    [Theory]
    [InlineData("b", RichStyle.Bold)]
    [InlineData("i", RichStyle.Italic)]
    [InlineData("u", RichStyle.Underline)]
    [InlineData("s", RichStyle.Strike)]
    [InlineData("mono", RichStyle.Mono)]
    [InlineData("code", RichStyle.Mono)]
    [InlineData("bold", RichStyle.Bold)]
    [InlineData("italic", RichStyle.Italic)]
    [InlineData("underline", RichStyle.Underline)]
    [InlineData("strike", RichStyle.Strike)]
    public void EveryStyleNameResolves(string tag, RichStyle expected)
    {
        var p = Parse($"<{tag}>x</>");
        Assert.Single(p.Styles);
        Assert.Equal(expected, p.Styles[0].Style);
    }

    [Fact]
    public void ParsesShortAndLongHexColors()
    {
        var shortForm = Parse("<#f80>x</>").Colors[0].Color;
        var longForm = Parse("<#ff8800>x</>").Colors[0].Color;
        Assert.Equal(longForm, shortForm);
    }

    [Fact]
    public void ParsesAlphaHexColor()
    {
        var p = Parse("<#ff000080>x</>");
        Assert.Single(p.Colors);
        Assert.True(p.Colors[0].Color.A < 1f);
    }

    [Fact]
    public void ParsesNamedColors()
    {
        var p = Parse("<red>x</> <blue>y</>");
        Assert.Equal(2, p.Colors.Count);
    }

    [Fact]
    public void EffectAndColorInOneTagBothApply()
    {
        var p = Parse("<wave 2 8 #f80>x</>");
        Assert.Single(p.Effects);
        Assert.Single(p.Colors);
        Assert.Equal(0, p.Colors[0].Start);
        Assert.Equal(1, p.Colors[0].End);
    }

    [Fact]
    public void UniversalCloseClosesMostRecent()
    {
        var p = Parse("<b><i>both</></>");
        Assert.Equal(2, p.Styles.Count);
        Assert.All(p.Styles, s => Assert.Equal(0, s.Start));
        Assert.All(p.Styles, s => Assert.Equal(4, s.End));
    }

    [Fact]
    public void NamedCloseCanUnwindOutOfOrder()
    {
        var p = Parse("<b>a<i>b</b>c</i>");
        Assert.Equal("abc", p.VisibleText);
        var bold = p.Styles.Single(s => s.Style == RichStyle.Bold);
        var italic = p.Styles.Single(s => s.Style == RichStyle.Italic);
        Assert.Equal(0, bold.Start);
        Assert.Equal(2, bold.End);
        Assert.Equal(1, italic.Start);
        Assert.Equal(3, italic.End);
    }

    [Fact]
    public void UnclosedTagsRunToTheEnd()
    {
        var p = Parse("<b>never closed");
        Assert.Single(p.Styles);
        Assert.Equal(0, p.Styles[0].Start);
        Assert.Equal("never closed".Length, p.Styles[0].End);
    }

    [Fact]
    public void UnknownTagsStayVisible()
    {
        var p = Parse("a <notatag> b");
        Assert.Equal("a <notatag> b", p.VisibleText);
        Assert.Empty(p.Effects);
        Assert.Empty(p.Styles);
    }

    [Fact]
    public void UnterminatedAngleBracketIsLiteral()
    {
        var p = Parse("2 < 3 and 4 > 1");
        Assert.Equal("2 < 3 and 4 > 1", p.VisibleText);
    }

    [Fact]
    public void EscapedAngleBracketIsLiteral()
    {
        var p = Parse(@"a \<b> c");
        Assert.Equal("a <b> c", p.VisibleText);
        Assert.Empty(p.Styles);
    }

    [Fact]
    public void StrayCloseTagIsIgnoredNotCrashing()
    {
        // A close tag with nothing open is consumed, same as any other close tag.
        var p = Parse("</>text</b>");
        Assert.Equal("text", p.VisibleText);
    }

    [Fact]
    public void ParsesSizeAndLinkSpans()
    {
        var p = Parse("<size 1.5>big</> <link https://x.dev>click</>");
        Assert.Single(p.Sizes);
        Assert.Equal(1.5f, p.Sizes[0].Scale);
        Assert.Single(p.Links);
        Assert.Equal("https://x.dev", p.Links[0].Href);
        Assert.Equal("big click", p.VisibleText);
    }

    [Fact]
    public void StaticMarkupIsFlaggedAsNotAnimated()
    {
        Assert.False(Parse("<b>bold</> and <#f80>orange</>").HasAnimatedEffects);
        Assert.True(Parse("<wave>moving</>").HasAnimatedEffects);
    }

    [Fact]
    public void PlainTextWithNoTagsIsUnchanged()
    {
        const string source = "just some ordinary text";
        var p = Parse(source);
        Assert.Equal(source, p.VisibleText);
        Assert.False(p.HasAnimatedEffects);
    }

    [Fact]
    public void EmptyAndNullSourcesAreSafe()
    {
        Assert.Equal(string.Empty, Parse(string.Empty).VisibleText);
        Assert.Equal(string.Empty, Parse(null!).VisibleText);
    }

    [Fact]
    public void NestedEffectsAllCoverTheirOwnRange()
    {
        var p = Parse("<wave>a<shake>b</>c</>");
        Assert.Equal("abc", p.VisibleText);
        var wave = p.Effects.Single(e => e.Kind == RichEffect.Wave);
        var shake = p.Effects.Single(e => e.Kind == RichEffect.Shake);
        Assert.Equal((0, 3), (wave.Start, wave.End));
        Assert.Equal((1, 2), (shake.Start, shake.End));
    }

    [Fact]
    public void AnEffectWithAColourClosesBothWithOneTag()
    {
        var p = Parse("<wave #f80>a</>b");
        Assert.Equal(1, p.Effects[0].End);
        Assert.Single(p.Colors);
        Assert.Equal(1, p.Colors[0].End);
    }

    [Fact]
    public void ANamedCloseEndsAnEffectAndItsColour()
    {
        var p = Parse("<wave #f80>a</wave>b");
        Assert.Equal(1, p.Effects[0].End);
        Assert.Equal(1, p.Colors[0].End);
    }

    [Fact]
    public void AStrayBracketDoesNotSwallowTheNextTag()
    {
        var p = Parse("2 < 3 <b>bold</>");
        Assert.Equal("2 < 3 bold", p.VisibleText);
        Assert.Single(p.Styles);
        Assert.Equal(6, p.Styles[0].Start);
        Assert.Equal(10, p.Styles[0].End);
    }

    [Fact]
    public void ABracketInsideATagIsNotATag()
    {
        var p = Parse("a <b <i>x</>");
        Assert.Equal("a <b x", p.VisibleText);
        Assert.Single(p.Styles);
        Assert.Equal(RichStyle.Italic, p.Styles[0].Style);
    }

    [Fact]
    public void CloseTagsMatchNamesExactly()
    {
        // Open tags are case sensitive, so closes are too. A close that matches nothing is still
        // consumed, and the bold runs on to the end.
        var p = Parse("<b>x</B>y");
        Assert.Equal("xy", p.VisibleText);
        Assert.Equal(2, p.Styles[0].End);
    }

    [Fact]
    public void RandomGarbageNeverThrows()
    {
        var random = new Random(99);
        const string alphabet = "<>/#abwave0123 ,\\";
        var parser = new RichTextParser();
        for (int i = 0; i < 500; i++)
        {
            var chars = new char[random.Next(0, 120)];
            for (int c = 0; c < chars.Length; c++)
            {
                chars[c] = alphabet[random.Next(alphabet.Length)];
            }

            parser.Parse(new string(chars));
            Assert.NotNull(parser.VisibleText);
        }
    }

    [Fact]
    public void ReparsingIsAllocationFreeInTheSteadyState()
    {
        const string source = "<wave 2 8 #f80>The quick brown fox</> jumps over the <b>lazy</> dog <shake 1>again</>.";
        var parser = new RichTextParser();
        for (int i = 0; i < 200; i++)
        {
            parser.Parse(source);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            parser.Parse(source);
        }

        long perParse = (GC.GetAllocatedBytesForCurrentThread() - before) / 50;
        // The visible string is the only unavoidable allocation; span buffers are reused.
        Assert.True(perParse < source.Length * 4, $"{perParse} bytes per parse of a {source.Length} char source");
    }
}
