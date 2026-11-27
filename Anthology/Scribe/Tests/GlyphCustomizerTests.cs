// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class GlyphCustomizerTests
{
    private static FontSystem Setup(out FontFile font)
    {
        var fonts = new FontSystem(new TestFontRenderer());
        foreach (var f in fonts.EnumerateSystemFonts())
        {
            if (f.Style != FontStyle.Regular) continue;
            fonts.AddFallbackFont(f);
            break;
        }

        font = fonts.FallbackFonts.First();
        return fonts;
    }

    private static TextLayoutSettings Settings(FontFile font, float pixelSize = 20f)
    {
        var s = TextLayoutSettings.Default;
        s.Font = font;
        s.PixelSize = pixelSize;
        return s;
    }

    private static List<GlyphInstance> AllGlyphs(TextLayout layout)
    {
        var all = new List<GlyphInstance>();
        foreach (var line in layout.Lines) all.AddRange(line.Glyphs);
        return all;
    }

    [Fact]
    public void ACharacterCanBeReplaced()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var stars = new TextLayout();
        fonts.UpdateLayout(stars, "****", settings);

        settings.Customizer = (ref GlyphStyle g) => g.Codepoint = '*';
        var masked = new TextLayout();
        fonts.UpdateLayout(masked, "hunter2", settings);

        // The source is what is measured against, so masking is a real relayout rather than a
        // different string handed to Scribe.
        Assert.Equal("hunter2", masked.Text);
        var glyphs = AllGlyphs(masked);
        Assert.Equal(7, glyphs.Count);
        foreach (var g in glyphs) Assert.Equal('*', g.Character);

        // Seven asterisks are wider than four, but each one is the same width as a real asterisk.
        float perStar = stars.Size.X / 4f;
        Assert.Equal(perStar * 7f, masked.Size.X, 2);
    }

    [Fact]
    public void AReplacedCharacterKeepsItsSourceIndex()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);
        settings.Customizer = (ref GlyphStyle g) => g.Codepoint = '*';

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "abcd", settings);

        var glyphs = AllGlyphs(layout);
        for (int i = 0; i < glyphs.Count; i++) Assert.Equal(i, glyphs[i].CharIndex);
    }

    [Fact]
    public void MaskingSwallowsTheSpacesToo()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);
        settings.Customizer = (ref GlyphStyle g) => g.Codepoint = '*';

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "a b c", settings);

        // Nothing is whitespace any more, so there is no gap and nowhere to break.
        Assert.Single(layout.Lines);
        Assert.Equal(5, AllGlyphs(layout).Count);
    }

    [Fact]
    public void LetterSpacingCanVaryPerCharacter()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var tight = new TextLayout();
        fonts.UpdateLayout(tight, "abcd", settings);

        settings.Customizer = (ref GlyphStyle g) => g.LetterSpacing = g.CharIndex < 2 ? 10f : 0f;
        var loose = new TextLayout();
        fonts.UpdateLayout(loose, "abcd", settings);

        Assert.Equal(tight.Size.X + 20f, loose.Size.X, 2);
    }

    [Fact]
    public void WordSpacingCanVaryPerSpace()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var tight = new TextLayout();
        fonts.UpdateLayout(tight, "a b c", settings);

        settings.Customizer = (ref GlyphStyle g) => g.WordSpacing = 10f;
        var loose = new TextLayout();
        fonts.UpdateLayout(loose, "a b c", settings);

        Assert.Equal(tight.Size.X + 20f, loose.Size.X, 2);
    }

    [Fact]
    public void ANullFontFallsBackToTheLayoutFont()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var plain = new TextLayout();
        fonts.UpdateLayout(plain, "abc", settings);

        settings.Customizer = (ref GlyphStyle g) => g.Font = null;
        var nulled = new TextLayout();
        fonts.UpdateLayout(nulled, "abc", settings);

        Assert.Equal(plain.Size.X, nulled.Size.X, 4);
    }

    [Fact]
    public void TheCustomizerSeesEveryCharacterOnceIncludingWhitespace()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var seen = new List<int>();
        settings.Customizer = (ref GlyphStyle g) => seen.Add(g.CharIndex);

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "ab c\nd", settings);

        Assert.Equal([0, 1, 2, 3, 4, 5], seen);
    }

    [Fact]
    public void ACustomizerThatChangesNothingLeavesLayoutUntouched()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var plain = new TextLayout();
        fonts.UpdateLayout(plain, "Hamburgefonstiv quick", settings);

        // A customizer that changes nothing has to agree with having none at all.
        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = 20f;
        var selected = new TextLayout();
        fonts.UpdateLayout(selected, "Hamburgefonstiv quick", settings);

        Assert.Equal(plain.Size.X, selected.Size.X, 4);
        Assert.Equal(plain.Size.Y, selected.Size.Y, 4);

        var a = AllGlyphs(plain);
        var b = AllGlyphs(selected);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Position.X, b[i].Position.X, 4);
            Assert.Equal(a[i].Position.Y, b[i].Position.Y, 4);
        }
    }

    [Fact]
    public void ABiggerSizeWidensTheRunItCovers()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var small = new TextLayout();
        fonts.UpdateLayout(small, "aaaa", settings);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = g.CharIndex >= 2 ? 40f : 20f;
        var mixed = new TextLayout();
        fonts.UpdateLayout(mixed, "aaaa", settings);

        Assert.True(mixed.Size.X > small.Size.X * 1.3f, $"{small.Size.X} -> {mixed.Size.X}");
    }

    [Fact]
    public void GlyphsCarryTheSizeTheyWereShapedAt()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);
        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = g.CharIndex < 2 ? 20f : 40f;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "abcd", settings);

        var glyphs = AllGlyphs(layout);
        Assert.Equal(4, glyphs.Count);
        Assert.Equal(20f, glyphs[0].PixelSize, 3);
        Assert.Equal(20f, glyphs[1].PixelSize, 3);
        Assert.Equal(40f, glyphs[2].PixelSize, 3);
        Assert.Equal(40f, glyphs[3].PixelSize, 3);
    }

    [Fact]
    public void TheLargestGlyphSetsTheLineBox()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var uniform = new TextLayout();
        fonts.UpdateLayout(uniform, "abcd", settings);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = g.CharIndex == 2 ? 60f : 20f;
        var tall = new TextLayout();
        fonts.UpdateLayout(tall, "abcd", settings);

        Assert.True(tall.Lines[0].Height > uniform.Lines[0].Height * 2f,
            $"{uniform.Lines[0].Height} -> {tall.Lines[0].Height}");
    }

    [Fact]
    public void MixedSizesShareOneBaseline()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);
        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = g.CharIndex < 2 ? 20f : 60f;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "xxxx", settings);

        var glyphs = AllGlyphs(layout);

        // 'x' has no descender, so every glyph's bottom edge is the baseline.
        static float Bottom(FontSystem fonts, GlyphInstance g)
        {
            var m = fonts.GetGlyphMetricsByIndex(g.Glyph.Font, g.Glyph.GlyphIndex, g.PixelSize, null)!.Value;
            return g.Position.Y + m.Height;
        }

        float first = Bottom(fonts, glyphs[0]);
        for (int i = 1; i < glyphs.Count; i++)
            Assert.Equal(first, Bottom(fonts, glyphs[i]), 2);

        // That shared baseline has to sit inside the line, with the tall glyphs fitting above it.
        float height = layout.Lines[0].Height;
        Assert.InRange(first, 0f, height);
        foreach (var g in glyphs) Assert.True(g.Position.Y >= 0f, $"glyph above the line at {g.Position.Y}");
    }

    [Fact]
    public void EachLineIsMeasuredOnItsOwnContents()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);
        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = g.CharIndex > 4 ? 60f : 20f;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "aaaa\nbbbb", settings);

        Assert.Equal(2, layout.Lines.Count);
        Assert.True(layout.Lines[1].Height > layout.Lines[0].Height * 2f,
            $"{layout.Lines[0].Height} vs {layout.Lines[1].Height}");
        Assert.Equal(layout.Lines[0].Height, layout.Lines[1].Position.Y, 3);
    }

    [Fact]
    public void SpacesTakeTheirOwnSize()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var small = new TextLayout();
        fonts.UpdateLayout(small, "a a", settings);

        // Only the space grows, so the gap between the two letters is the only thing that changes.
        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = g.CharIndex == 1 ? 60f : 20f;
        var wide = new TextLayout();
        fonts.UpdateLayout(wide, "a a", settings);

        Assert.True(wide.Size.X > small.Size.X, $"{small.Size.X} -> {wide.Size.X}");
    }

    [Fact]
    public void WrappingAccountsForTheLargerText()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);
        settings.WrapMode = TextWrapMode.Wrap;
        settings.MaxWidth = 120f;

        var small = new TextLayout();
        fonts.UpdateLayout(small, "one two three four", settings);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = 40f;
        var big = new TextLayout();
        fonts.UpdateLayout(big, "one two three four", settings);

        Assert.True(big.Lines.Count > small.Lines.Count,
            $"{small.Lines.Count} lines -> {big.Lines.Count}");
        Assert.True(big.Size.X <= 120f + 1f);
    }

    [Fact]
    public void AllSmallTextGetsASmallLine()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var uniform = new TextLayout();
        fonts.UpdateLayout(uniform, "abc", settings);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = 10f;
        var small = new TextLayout();
        fonts.UpdateLayout(small, "abc", settings);

        Assert.True(small.Lines[0].Height < uniform.Lines[0].Height,
            $"{uniform.Lines[0].Height} -> {small.Lines[0].Height}");
    }

    [Fact]
    public void ANonPositiveSizeFallsBackToTheBase()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        var plain = new TextLayout();
        fonts.UpdateLayout(plain, "abc", settings);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = 0f;
        var zeroed = new TextLayout();
        fonts.UpdateLayout(zeroed, "abc", settings);

        Assert.Equal(plain.Size.X, zeroed.Size.X, 4);
        Assert.Equal(plain.Size.Y, zeroed.Size.Y, 4);
    }

    [Fact]
    public void TwoLayoutsWithTheSameTextButDifferentSizesDoNotShareACacheEntry()
    {
        var fonts = Setup(out var font);
        var settings = Settings(font);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = 20f;
        var a = fonts.CreateLayout("abc", settings);

        settings.Customizer = (ref GlyphStyle g) => g.PixelSize = 50f;
        var b = fonts.CreateLayout("abc", settings);

        Assert.True(b.Size.X > a.Size.X * 1.5f, $"{a.Size.X} vs {b.Size.X}");
    }
}
