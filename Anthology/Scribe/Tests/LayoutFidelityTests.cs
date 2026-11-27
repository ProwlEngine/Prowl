// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Scribe;

namespace Tests;

/// <summary>
/// Layout behaviour checked against what a browser does with the same font, size and wrap width.
/// Each of these started as a measured difference between the two.
/// </summary>
public class LayoutFidelityTests
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

    private static TextLayoutSettings Settings(FontFile font, float pixelSize = 32f)
    {
        var s = TextLayoutSettings.Default;
        s.Font = font;
        s.PixelSize = pixelSize;
        return s;
    }

    /// <summary>
    /// Space a line is given beyond the font's own ascent-to-descent box is leading, and half of it
    /// belongs above the text. Putting it all below leaves raised line heights looking top-heavy and
    /// disagrees with every browser.
    /// </summary>
    [Fact]
    public void ExtraLineHeightIsSplitAboveAndBelowTheText()
    {
        var fs = CreateSystem(out FontFile font);
        fs.GetScaledVMetrics(font, 32f, out float asc, out float desc, out _);

        var settings = Settings(font);
        settings.LineHeight = 2.0f;

        var layout = fs.CreateLayout("Hxg", settings);
        Line line = layout.Lines[0];

        float baseline = BaselineOf(fs, layout, line);
        float above = baseline - asc;
        float below = line.Height - (baseline - desc);

        Assert.True(above > 0.5f, $"nothing was left above the text (above={above})");
        Assert.Equal(above, below, 2);
    }

    /// <summary>At the natural line height the font fills the line, so there is no leading to split.</summary>
    [Fact]
    public void ANaturalLineHeightPutsTheBaselineAtTheAscender()
    {
        var fs = CreateSystem(out FontFile font);
        fs.GetScaledVMetrics(font, 32f, out float asc, out float desc, out float gap);

        var layout = fs.CreateLayout("Hxg", Settings(font));
        float baseline = BaselineOf(fs, layout, layout.Lines[0]);

        // The only leading at LineHeight 1 is the font's own line gap.
        Assert.Equal(asc + gap * 0.5f, baseline, 2);
        Assert.Equal(asc - desc + gap, layout.Lines[0].Height, 2);
    }

    /// <summary>
    /// A line has one baseline. Glyphs that came from a fallback font sit on it too, rather than each
    /// font placing its own glyphs at its own ascender and leaving mixed text stepped.
    /// </summary>
    [Fact]
    public void EveryGlyphOnALineSharesOneBaseline()
    {
        var fs = new FontSystem(new TestFontRenderer());
        foreach (var f in fs.EnumerateSystemFonts().Where(f => f.Style == FontStyle.Regular).Take(4))
            fs.AddFallbackFont(f);

        FontFile font = fs.FallbackFonts.First();
        var layout = fs.CreateLayout("Aa Bb Cc", Settings(font));

        foreach (Line line in layout.Lines)
        {
            float[] baselines = line.Glyphs
                .Select(g => g.Position.Y - (fs.GetGlyphMetricsByIndex(g.Glyph.Font, g.Glyph.GlyphIndex, g.PixelSize)?.OffsetY ?? 0f))
                .Distinct()
                .ToArray();

            Assert.Single(baselines);
        }
    }

    /// <summary>
    /// A line is as wide as the pen travelled. Adding the last glyph's left side bearing on top of
    /// that measures from the pen origin to past the glyph's ink, which over-reports the width and
    /// pushes centred and right-aligned text off by that bearing.
    /// </summary>
    [Fact]
    public void LineWidthIsTheAdvanceWidthNotTheInkBox()
    {
        var fs = CreateSystem(out FontFile font);
        var layout = fs.CreateLayout("owo", Settings(font));

        Line line = layout.Lines[0];
        float advances = line.Glyphs.Sum(g => g.AdvanceWidth);

        Assert.Equal(advances, line.Width, 2);
    }

    /// <summary>
    /// Words are shaped one at a time, so the kerning pairs that straddle a space are not covered by
    /// that shaping and have to be applied by the layout. Without it every space in a line is a
    /// fraction of a pixel too wide and the error accumulates across the line.
    /// </summary>
    [Fact]
    public void KerningIsAppliedAcrossASpace()
    {
        var fs = CreateSystem(out FontFile font);

        var space = fs.GetOrCreateGlyph(' ', font, FontQuality.Normal);
        var v = fs.GetOrCreateGlyph('V', font, FontQuality.Normal);
        var t = fs.GetOrCreateGlyph('T', font, FontQuality.Normal);
        Assert.NotNull(space);

        float pairKern = fs.GetKerningByGlyph(font, v!.GlyphIndex, space!.GlyphIndex, 32f)
                       + fs.GetKerningByGlyph(font, space.GlyphIndex, t!.GlyphIndex, 32f);

        if (pairKern == 0f) return; // this font kerns neither pair, so there is nothing to assert

        var layout = fs.CreateLayout("V T", Settings(font));
        Line line = layout.Lines[0];

        float vAdvance = line.Glyphs[0].AdvanceWidth;
        float spaceWidth = fs.GetGlyphMetricsByIndex(font, space.GlyphIndex, 32f)!.Value.AdvanceWidth;
        float unkerned = vAdvance + spaceWidth;

        float penOfT = line.Glyphs[1].Position.X
                     - (fs.GetGlyphMetricsByIndex(font, t.GlyphIndex, 32f)?.OffsetX ?? 0f);

        Assert.Equal(unkerned + pairKern, penOfT, 2);
    }

    /// <summary>Wrapping onto a new line drops the kern, since there is no longer a space to its left.</summary>
    [Fact]
    public void AWordThatWrapsDoesNotCarryTheKernFromThePreviousLine()
    {
        var fs = CreateSystem(out FontFile font);

        // Sized from the text itself rather than a guess, since which system font this picks up
        // decides how wide any of it is.
        const string Text = "V Top";
        float full = fs.CreateLayout(Text, Settings(font)).Size.X;
        float firstWord = fs.CreateLayout("V", Settings(font)).Size.X;

        var settings = Settings(font);
        settings.WrapMode = TextWrapMode.Wrap;
        settings.MaxWidth = (full + firstWord) * 0.5f;

        var layout = fs.CreateLayout(Text, settings);
        Assert.True(layout.Lines.Count > 1, "the text was expected to wrap");

        Line second = layout.Lines[1];
        float offsetX = fs.GetGlyphMetricsByIndex(second.Glyphs[0].Glyph.Font, second.Glyphs[0].Glyph.GlyphIndex, 32f)?.OffsetX ?? 0f;

        Assert.Equal(0f, second.Glyphs[0].Position.X - offsetX, 3);
    }

    private static float BaselineOf(FontSystem fs, TextLayout layout, Line line)
    {
        GlyphInstance g = line.Glyphs[0];
        float offsetY = fs.GetGlyphMetricsByIndex(g.Glyph.Font, g.Glyph.GlyphIndex, g.PixelSize)?.OffsetY ?? 0f;
        return g.Position.Y - offsetY;
    }
}
