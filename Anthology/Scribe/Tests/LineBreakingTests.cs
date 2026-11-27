// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Scribe;

namespace Tests;

/// <summary>
/// Where a line is allowed to break, and which characters take up room. Every case here was
/// measured out of Chrome first.
/// </summary>
public class LineBreakingTests
{
    private static FontSystem CreateSystem(out FontFile font)
    {
        var fs = new FontSystem(new TestFontRenderer());
        foreach (var f in fs.EnumerateSystemFonts())
        {
            if (f.Style != FontStyle.Regular) continue;
            fs.AddFallbackFont(f);
            break;
        }
        font = fs.FallbackFonts.First();
        return fs;
    }

    private static TextLayoutSettings Settings(FontFile font, float wrap = 0f, float pixelSize = 16f)
    {
        var s = TextLayoutSettings.Default;
        s.Font = font;
        s.PixelSize = pixelSize;
        if (wrap > 0f)
        {
            s.WrapMode = TextWrapMode.Wrap;
            s.MaxWidth = wrap;
        }
        return s;
    }

    private static string[] LinesOf(TextLayout layout) =>
        layout.Lines.Select(l =>
        {
            int start = Math.Clamp(l.StartIndex, 0, layout.Text.Length);
            int end = Math.Clamp(l.EndIndex, start, layout.Text.Length);
            return layout.Text[start..end];
        }).ToArray();

    /// <summary>A hyphen is a break opportunity; a browser will start the next line after one.</summary>
    [Fact]
    public void ALineMayBreakAfterAHyphen()
    {
        var fs = CreateSystem(out FontFile font);
        float wide = fs.CreateLayout("well-known", Settings(font)).Size.X;

        var layout = fs.CreateLayout("well-known", Settings(font, wide * 0.75f));

        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal("well-", LinesOf(layout)[0]);
    }

    /// <summary>
    /// A no-break space looks like a space and behaves like a letter: the line does not break there.
    /// It may still break somewhere, because a word wider than the line has to give way somewhere,
    /// but not at the space.
    /// </summary>
    [Fact]
    public void ALineDoesNotBreakAtANoBreakSpace()
    {
        var fs = CreateSystem(out FontFile font);
        const string Ordinary = "keep together";
        const string NoBreak = "keep together";

        float wide = fs.CreateLayout(Ordinary, Settings(font)).Size.X;
        float wrap = wide * 0.75f;

        var ordinary = fs.CreateLayout(Ordinary, Settings(font, wrap));
        var noBreak = fs.CreateLayout(NoBreak, Settings(font, wrap));

        // The space is at index 4, so breaking there starts the next line at 5.
        Assert.Equal(5, ordinary.Lines[1].StartIndex);
        Assert.NotEqual(5, noBreak.Lines[1].StartIndex);
    }

    /// <summary>A zero-width space is a break opportunity and nothing else.</summary>
    [Fact]
    public void AZeroWidthSpaceTakesNoRoom()
    {
        var fs = CreateSystem(out FontFile font);

        float plain = fs.CreateLayout("abcdef", Settings(font)).Size.X;
        float withZwsp = fs.CreateLayout("abc​def", Settings(font)).Size.X;

        Assert.Equal(plain, withZwsp, 3);
    }

    [Fact]
    public void AZeroWidthSpaceOffersABreak()
    {
        var fs = CreateSystem(out FontFile font);
        const string Text = "abcdef​ghijkl";

        float wide = fs.CreateLayout(Text, Settings(font)).Size.X;
        var layout = fs.CreateLayout(Text, Settings(font, wide * 0.7f));

        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal("ghijkl", LinesOf(layout)[1]);
    }

    /// <summary>A soft hyphen is invisible until the line breaks at it, and then it is a hyphen.</summary>
    [Fact]
    public void ASoftHyphenTakesNoRoomUntilItBreaks()
    {
        var fs = CreateSystem(out FontFile font);

        float plain = fs.CreateLayout("abcdef", Settings(font)).Size.X;
        float withSoft = fs.CreateLayout("abc­def", Settings(font)).Size.X;

        Assert.Equal(plain, withSoft, 3);
    }

    [Fact]
    public void ASoftHyphenBecomesAHyphenAtTheBreak()
    {
        var fs = CreateSystem(out FontFile font);
        const string Text = "abcdef­ghijkl";

        float wide = fs.CreateLayout(Text, Settings(font)).Size.X;
        var layout = fs.CreateLayout(Text, Settings(font, wide * 0.7f));

        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal('-', layout.Lines[0].Glyphs[^1].Character);
    }

    /// <summary>A combining mark sits on the character before it and adds nothing to the width.</summary>
    [Fact]
    public void ACombiningMarkTakesNoRoom()
    {
        var fs = CreateSystem(out FontFile font);

        float plain = fs.CreateLayout("cafe", Settings(font)).Size.X;
        float marked = fs.CreateLayout("café", Settings(font)).Size.X;

        Assert.Equal(plain, marked, 3);
    }

    [Fact]
    public void ACombiningMarkIsPlacedOverTheCharacterItFollows()
    {
        var fs = CreateSystem(out FontFile font);
        var layout = fs.CreateLayout("aéb", Settings(font));

        GlyphInstance[] glyphs = layout.Lines[0].Glyphs.ToArray();
        GlyphInstance? mark = glyphs.Cast<GlyphInstance?>().FirstOrDefault(g => g!.Value.CharIndex == 2);

        // A font whose shaping folds the pair into one precomposed glyph has nothing to place.
        if (mark is null) return;

        GlyphInstance baseGlyph = glyphs.First(g => g.CharIndex == 1);
        float basePen = baseGlyph.Position.X - Offset(fs, baseGlyph);
        float markPen = mark.Value.Position.X - Offset(fs, mark.Value);

        Assert.Equal(basePen, markPen, 3);
    }

    /// <summary>CRLF is one line break, and so is a lone CR. Neither is a space.</summary>
    [Fact]
    public void CarriageReturnsAreLineBreaksNotSpaces()
    {
        var fs = CreateSystem(out FontFile font);

        Assert.Equal(3, fs.CreateLayout("one\r\ntwo\r\nthree", Settings(font)).Lines.Count);
        Assert.Equal(3, fs.CreateLayout("one\rtwo\rthree", Settings(font)).Lines.Count);
        Assert.Equal(3, fs.CreateLayout("one\ntwo\nthree", Settings(font)).Lines.Count);
    }

    [Fact]
    public void ACarriageReturnAddsNoWidthToTheLine()
    {
        var fs = CreateSystem(out FontFile font);

        float plain = fs.CreateLayout("one", Settings(font)).Lines[0].Width;
        float withCr = fs.CreateLayout("one\r\ntwo", Settings(font)).Lines[0].Width;

        Assert.Equal(plain, withCr, 3);
    }

    /// <summary>
    /// A tab stop so close it would barely move the pen is passed over for the next one, which is
    /// what a browser does at every font and size.
    /// </summary>
    [Fact]
    public void ATabSkipsAStopItWouldBarelyReach()
    {
        var fs = CreateSystem(out FontFile font);

        var settings = Settings(font);
        settings.TabSize = 4;

        var space = fs.GetOrCreateGlyph(' ', font, settings.Quality);
        float spaceWidth = fs.GetGlyphMetricsByIndex(font, space!.GlyphIndex, settings.PixelSize)!.Value.AdvanceWidth;
        float tabWidth = spaceWidth * settings.TabSize;

        // Text sized to land just short of a stop, inside the half-space the browser refuses.
        string prefix = new string('x', 1);
        float prefixWidth = fs.CreateLayout(prefix, settings).Size.X;
        while (prefixWidth < tabWidth * 4)
        {
            float toNext = (MathF.Floor(prefixWidth / tabWidth) + 1f) * tabWidth - prefixWidth;
            if (toNext < spaceWidth * 0.5f) break;
            prefix += "x";
            prefixWidth = fs.CreateLayout(prefix, settings).Size.X;
        }

        float gap = (MathF.Floor(prefixWidth / tabWidth) + 1f) * tabWidth - prefixWidth;
        if (gap >= spaceWidth * 0.5f) return; // no such length exists for this font

        var layout = fs.CreateLayout(prefix + "\tz", settings);
        GlyphInstance z = layout.Lines[0].Glyphs[^1];
        float pen = z.Position.X - Offset(fs, z);

        float skipped = (MathF.Floor(prefixWidth / tabWidth) + 2f) * tabWidth;
        Assert.Equal(skipped, pen, 2);
    }

    private static float Offset(FontSystem fs, GlyphInstance g)
        => fs.GetGlyphMetricsByIndex(g.Glyph.Font, g.Glyph.GlyphIndex, g.PixelSize)?.OffsetX ?? 0f;
}
