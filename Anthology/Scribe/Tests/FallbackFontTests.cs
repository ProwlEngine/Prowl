// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Scribe;

namespace Tests;

/// <summary>
/// How a glyph the chosen font does not have gets drawn: which font supplies it, and at what size.
/// </summary>
public class FallbackFontTests
{
    private static FontSystem LoadSeveral(out FontFile[] fonts, int count = 8)
    {
        var fs = new FontSystem(new TestFontRenderer());
        foreach (var f in fs.EnumerateSystemFonts().Take(count))
            fs.AddFallbackFont(f);

        fonts = fs.FallbackFonts.ToArray();
        return fs;
    }

    /// <summary>
    /// Every font in a chain is drawn at one em, taken from the font the text asked for. Fonts
    /// disagree considerably about how tall a pixel size is, so scaling a borrowed glyph by its own
    /// idea of that leaves it visibly the wrong size next to the text around it.
    /// </summary>
    [Fact]
    public void AFallbackGlyphIsScaledToThePrimarysEm()
    {
        var fs = LoadSeveral(out FontFile[] fonts);

        FontFile primary = fonts[0];
        float primaryEm = fs.GetScale(primary, primary, 32f) * primary.UnitsPerEm;

        foreach (FontFile other in fonts)
        {
            float em = fs.GetScale(other, primary, 32f) * other.UnitsPerEm;
            Assert.Equal(primaryEm, em, 3);
        }
    }

    [Fact]
    public void APrimaryFontIsScaledByItsOwnPixelHeight()
    {
        var fs = LoadSeveral(out FontFile[] fonts);
        FontFile primary = fonts[0];

        Assert.Equal(primary.ScaleForPixelHeight(32f), fs.GetScale(primary, primary, 32f), 6);
        Assert.Equal(primary.ScaleForPixelHeight(32f), fs.GetScale(primary, null, 32f), 6);
    }

    /// <summary>
    /// Symbol and emoji fonts ship in one style only. Insisting a fallback match the requested style
    /// drops those characters from every bold and italic run, which is worse than drawing them
    /// upright.
    /// </summary>
    [Fact]
    public void ABoldRunFallsBackToARegularFontRatherThanDroppingTheGlyph()
    {
        var fs = new FontSystem(new TestFontRenderer());
        var all = fs.EnumerateSystemFonts().ToArray();

        FontFile? bold = all.FirstOrDefault(f => f.Style == FontStyle.Bold);
        FontFile? regular = all.FirstOrDefault(f => f.Style == FontStyle.Regular);
        if (bold is null || regular is null) return;

        // A character the bold face has no glyph for but some regular face does.
        int codepoint = -1;
        FontFile? donor = null;
        foreach (FontFile candidate in all.Where(f => f.Style == FontStyle.Regular))
        {
            for (int cp = 0x2500; cp < 0x2600 && codepoint < 0; cp++)
            {
                if (bold.FindGlyphIndex(cp) > 0) continue;
                if (candidate.FindGlyphIndex(cp) <= 0) continue;
                codepoint = cp;
                donor = candidate;
            }
            if (codepoint >= 0) break;
        }

        if (codepoint < 0) return; // no such character on this machine

        fs.AddFallbackFont(bold);
        fs.AddFallbackFont(donor!);

        AtlasGlyph? glyph = fs.GetOrCreateGlyph(codepoint, bold, FontQuality.Normal);

        Assert.NotNull(glyph);
        Assert.Equal(donor, glyph!.Font);
    }

    /// <summary>A font that does have the character in the right style is still preferred.</summary>
    [Fact]
    public void AStyleMatchIsPreferredOverAnyOtherFont()
    {
        var fs = new FontSystem(new TestFontRenderer());
        var all = fs.EnumerateSystemFonts().ToArray();

        FontFile? regular = all.FirstOrDefault(f => f.Style == FontStyle.Regular);
        FontFile? bold = all.FirstOrDefault(f => f.Style == FontStyle.Bold && f.FindGlyphIndex('A') > 0);
        if (regular is null || bold is null) return;

        // Regular first in the list, so only the style preference can pick the bold one.
        fs.AddFallbackFont(regular);
        fs.AddFallbackFont(bold);

        var probe = all.FirstOrDefault(f => f.Style == FontStyle.Bold && f != bold && f.FindGlyphIndex('A') <= 0);
        AtlasGlyph? glyph = fs.GetOrCreateGlyph('A', probe ?? bold, FontQuality.Normal);

        Assert.NotNull(glyph);
        Assert.Equal(FontStyle.Bold, glyph!.Font.Style);
    }
}
