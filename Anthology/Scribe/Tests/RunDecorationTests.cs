// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class RunDecorationTests
{
    private static (FontSystem fonts, TextLayoutSettings settings) Setup()
    {
        var fonts = new FontSystem(new TestFontRenderer());
        foreach (var f in fonts.EnumerateSystemFonts())
        {
            if (f.Style != FontStyle.Regular) continue;
            fonts.AddFallbackFont(f);
            break;
        }

        var settings = TextLayoutSettings.Default;
        settings.Font = fonts.FallbackFonts.First();
        settings.PixelSize = 32f;
        return (fonts, settings);
    }

    // Decoration bars are the quads with no character behind them.
    private static List<(float X0, float X1, float Y0)> Bars(FontSystem fonts, TextLayout layout)
    {
        var bars = new List<(float, float, float)>();
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            if (g.IsDecoration) bars.Add((g.TopLeft.X, g.TopRight.X, g.TopLeft.Y));
        });
        return bars;
    }

    [Fact]
    public void AnUnderlinedRunGetsABarOfItsOwn()
    {
        var (fonts, settings) = Setup();
        settings.Customizer = (ref GlyphStyle g) => g.Underline = g.CharIndex >= 4 && g.CharIndex < 9;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "one two three", settings);

        var bars = Bars(fonts, layout);
        Assert.Single(bars);

        int first = -1;
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            if (g.IsDecoration) first = g.CharIndex;
        });
        Assert.Equal(4, first);

        // Only "two t" is underlined, so the bar is well short of the whole line.
        Assert.True(bars[0].X0 > 0f, "the bar started at the beginning of the line");
        Assert.True(bars[0].X1 < layout.Size.X, "the bar ran to the end of the line");
    }

    [Fact]
    public void SeparateRunsGetSeparateBars()
    {
        var (fonts, settings) = Setup();
        settings.Customizer = (ref GlyphStyle g) => g.Underline = g.CharIndex < 2 || g.CharIndex >= 6;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "ab cd ef", settings);

        var bars = Bars(fonts, layout);
        Assert.Equal(2, bars.Count);
        Assert.True(bars[0].X1 < bars[1].X0, "the two bars overlap");
    }

    [Fact]
    public void UnderlineAndStrikeOnOneRunAreTwoBars()
    {
        var (fonts, settings) = Setup();
        settings.Customizer = (ref GlyphStyle g) =>
        {
            g.Underline = true;
            g.Strikethrough = true;
        };

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "both", settings);

        var bars = Bars(fonts, layout);
        Assert.Equal(2, bars.Count);
        Assert.NotEqual(bars[0].Y0, bars[1].Y0);
    }

    [Fact]
    public void DecorationDoesNotBreakKerning()
    {
        // Only meaningful with a font that kerns this pair, so find one rather than assume it.
        var fonts = new FontSystem(new TestFontRenderer());
        TextLayoutSettings settings = default;
        bool found = false;
        foreach (var f in fonts.EnumerateSystemFonts())
        {
            if (f.Style != FontStyle.Regular) continue;
            settings = TextLayoutSettings.Default;
            settings.Font = f;
            settings.PixelSize = 64f;
            float pair = fonts.MeasureText("AV", settings).X;
            float apart = fonts.MeasureText("A", settings).X + fonts.MeasureText("V", settings).X;
            if (apart - pair > 1f) { found = true; break; }
        }

        Assert.True(found, "no installed font kerns AV, so this test cannot say anything");

        var plain = new TextLayout();
        fonts.UpdateLayout(plain, "AVAVAV", settings);

        // Decorating every other letter would split shaping runs if decoration took part in them.
        settings.Customizer = (ref GlyphStyle g) => g.Underline = g.CharIndex % 2 == 0;
        var decorated = new TextLayout();
        fonts.UpdateLayout(decorated, "AVAVAV", settings);

        Assert.Equal(plain.Size.X, decorated.Size.X, 3);
    }

    [Fact]
    public void TheLayoutWideSettingStillUnderlinesEachLine()
    {
        var (fonts, settings) = Setup();
        settings.Underline = true;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "first\nsecond", settings);

        Assert.Equal(2, Bars(fonts, layout).Count);
    }

    [Fact]
    public void ACustomizerCanTurnTheLayoutWideUnderlineOff()
    {
        var (fonts, settings) = Setup();
        settings.Underline = true;
        settings.Customizer = (ref GlyphStyle g) => g.Underline = g.CharIndex != 2;

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, "abcde", settings);

        Assert.Equal(2, Bars(fonts, layout).Count);
    }

    [Fact]
    public void ABiggerGlyphInTheRunThickensTheBar()
    {
        var (fonts, settings) = Setup();
        settings.Customizer = (ref GlyphStyle g) => g.Underline = true;

        var small = new TextLayout();
        fonts.UpdateLayout(small, "abc", settings);

        settings.Customizer = (ref GlyphStyle g) =>
        {
            g.Underline = true;
            g.PixelSize = g.CharIndex == 1 ? 96f : 32f;
        };
        var mixed = new TextLayout();
        fonts.UpdateLayout(mixed, "abc", settings);

        float Thickness(TextLayout layout)
        {
            float t = 0f;
            fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
            {
                if (g.IsDecoration) t = g.Height;
            });
            return t;
        }

        Assert.True(Thickness(mixed) > Thickness(small), $"{Thickness(small)} -> {Thickness(mixed)}");
    }

    /// <summary>
    /// Half of a line's leading sits above the text, so raising the line height moves the glyphs
    /// down. An underline is drawn from the same baseline and has to move with them. Computing it
    /// from the ascent alone leaves it floating above the text it belongs to.
    /// </summary>
    [Fact]
    public void AnUnderlineMovesWithTheTextWhenTheLineHeightGrows()
    {
        var (fonts, settings) = Setup();
        settings.Underline = true;

        (float Glyph, float Bar) Tops(float lineHeight)
        {
            settings.LineHeight = lineHeight;
            var layout = new TextLayout();
            fonts.UpdateLayout(layout, "Underlined", settings);

            float glyph = float.NaN, bar = float.NaN;
            fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
            {
                if (g.IsDecoration) bar = g.TopLeft.Y;
                else if (float.IsNaN(glyph)) glyph = g.TopLeft.Y;
            });
            return (glyph, bar);
        }

        var tight = Tops(1f);
        var loose = Tops(2f);

        float glyphShift = loose.Glyph - tight.Glyph;
        Assert.True(glyphShift > 0.5f, $"the text did not move down at all (shift={glyphShift})");
        Assert.Equal(glyphShift, loose.Bar - tight.Bar, 2);
    }
}
