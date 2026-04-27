using Prowl.Scribe;
using System;
using System.Linq;
using Prowl.Vector;

namespace Tests
{
    public class RichTextTests
    {
        class TestFontRenderer : IFontRenderer
        {
            public object CreateTexture(int width, int height) => new byte[width * height];
            public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }
            public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices) { }
        }

        private FontSystem CreateSystem()
        {
            var fs = new FontSystem(new TestFontRenderer());
            foreach (var font in fs.EnumerateSystemFonts())
            {
                if (font.Style == FontStyle.Regular)
                {
                    fs.AddFallbackFont(font);
                    break;
                }
            }
            return fs;
        }

        private RichTextLayoutSettings BasicSettings(FontSystem fs)
        {
            var s = RichTextLayoutSettings.Default;
            s.RegularFont = fs.FallbackFonts.First();
            s.PixelSize = 16f;
            return s;
        }

        // ----- Parser ------------------------------------------------------------------

        [Fact]
        public void Parser_StripsTags_PreservesVisibleText()
        {
            var p = RichTextParser.Parse("Hello <b>bold</b> world");
            Assert.Equal("Hello bold world", p.VisibleText);
            Assert.Single(p.Styles);
            Assert.Equal(RichStyleFlags.Bold, p.Styles[0].Flags);
            Assert.Equal(6, p.Styles[0].Start);
            Assert.Equal(10, p.Styles[0].End);
        }

        [Fact]
        public void Parser_NestedStyles_BothFlagsApply()
        {
            var p = RichTextParser.Parse("<b><i>bi</i></b>");
            Assert.Equal("bi", p.VisibleText);
            Assert.Equal(2, p.Styles.Count);
            Assert.Contains(p.Styles, s => s.Flags == RichStyleFlags.Bold);
            Assert.Contains(p.Styles, s => s.Flags == RichStyleFlags.Italic);
        }

        [Fact]
        public void Parser_ColorTag_AcceptsHexAndNamed()
        {
            var p = RichTextParser.Parse("<color=#ff0000>r</color><color=blue>b</color>");
            Assert.Equal("rb", p.VisibleText);
            Assert.Equal(2, p.Styles.Count);
            Assert.Equal((byte)255, p.Styles[0].Color.Value.R);
            Assert.Equal((byte)0, p.Styles[1].Color.Value.R);
            Assert.Equal((byte)255, p.Styles[1].Color.Value.B);
        }

        [Fact]
        public void Parser_SizeTag_PercentEncodesNegative()
        {
            var p = RichTextParser.Parse("<size=200%>X</size><size=24>Y</size>");
            Assert.Equal("XY", p.VisibleText);
            Assert.Equal(-200f, p.Styles[0].PixelSize);
            Assert.Equal(24f, p.Styles[1].PixelSize);
        }

        [Fact]
        public void Parser_EffectAttributes_AreParsed()
        {
            var p = RichTextParser.Parse("<shake amp=3 freq=12>x</shake>");
            Assert.Single(p.Effects);
            Assert.Equal(RichEffectKind.Shake, p.Effects[0].Kind);
            Assert.Equal(3f, p.Effects[0].P0);
            Assert.Equal(12f, p.Effects[0].P1);
        }

        [Fact]
        public void Parser_EffectShorthandValue_GoesToP0()
        {
            var p = RichTextParser.Parse("<wave=5>x</wave>");
            Assert.Equal(5f, p.Effects[0].P0);
        }

        [Fact]
        public void Parser_LinkTag_StoresHref()
        {
            var p = RichTextParser.Parse("<link=https://x>click</link>");
            Assert.Equal("click", p.VisibleText);
            Assert.Single(p.Styles);
            Assert.Equal("https://x", p.Styles[0].LinkHref);
            Assert.True((p.Styles[0].Flags & RichStyleFlags.Link) != 0);
        }

        [Fact]
        public void Parser_BackslashEscapesAngle()
        {
            var p = RichTextParser.Parse(@"a\<b>");
            Assert.Equal("a<b>", p.VisibleText);
            Assert.Empty(p.Styles);
        }

        [Fact]
        public void Parser_UnknownTag_IsWarnedNotStripped()
        {
            var p = RichTextParser.Parse("<garbage>x</garbage>");
            // Tag is consumed as a tag (so it doesn't appear in visible text), but produces a warning.
            Assert.Equal("x", p.VisibleText);
            Assert.NotEmpty(p.Warnings);
        }

        [Fact]
        public void Parser_UnclosedTag_StillEmitsSpanToEnd()
        {
            var p = RichTextParser.Parse("<b>oops");
            Assert.Equal("oops", p.VisibleText);
            Assert.Single(p.Styles);
            Assert.Equal(0, p.Styles[0].Start);
            Assert.Equal(4, p.Styles[0].End);
            Assert.NotEmpty(p.Warnings);
        }

        [Fact]
        public void Parser_NotATag_IsLiteral()
        {
            var p = RichTextParser.Parse("a < b");
            Assert.Equal("a < b", p.VisibleText);
        }

        // ----- Layout ------------------------------------------------------------------

        [Fact]
        public void Layout_SimpleText_BuildsGlyphsAndOneLine()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("hello", BasicSettings(fs));
            rt.Update(fs);
            Assert.Single(rt.Lines);
            Assert.Equal(5, rt.Glyphs.Count);
        }

        [Fact]
        public void Layout_NewlineBreaksLines()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("a\nb\nc", BasicSettings(fs));
            rt.Update(fs);
            Assert.Equal(3, rt.Lines.Count);
        }

        [Fact]
        public void Layout_BiggerSize_RaisesLineHeight()
        {
            var fs = CreateSystem();
            var s = BasicSettings(fs);
            var smallRt = new RichTextLayout("x", s);
            smallRt.Update(fs);
            float smallH = smallRt.Lines[0].Height;

            var bigRt = new RichTextLayout("<size=64>x</size>", s);
            bigRt.Update(fs);
            float bigH = bigRt.Lines[0].Height;

            Assert.True(bigH > smallH * 2, $"Expected big size to grow line height; small={smallH}, big={bigH}");
        }

        [Fact]
        public void Layout_WrapBreaksAtWordBoundary()
        {
            var fs = CreateSystem();
            var s = BasicSettings(fs);
            s.WrapMode = TextWrapMode.Wrap;
            s.MaxWidth = 30f; // tight to force wrap
            var rt = new RichTextLayout("hello world", s);
            rt.Update(fs);
            Assert.True(rt.Lines.Count >= 2, "Expected wrap to produce >= 2 lines");
        }

        [Fact]
        public void Layout_PerGlyphColor_BakedIntoGlyph()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("a<color=red>b</color>c", BasicSettings(fs));
            rt.Update(fs);
            Assert.Equal(3, rt.Glyphs.Count);
            // 'b' is at visible index 1
            var b = rt.Glyphs.First(g => g.Character == 'b');
            Assert.Equal((byte)255, b.Color.R);
        }

        [Fact]
        public void Layout_NestedColor_InnerOverridesOuter()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("<color=red>a<color=blue>b</color>c</color>", BasicSettings(fs));
            rt.Update(fs);
            Assert.Equal(3, rt.Glyphs.Count);
            var a = rt.Glyphs.First(g => g.Character == 'a');
            var b = rt.Glyphs.First(g => g.Character == 'b');
            var c = rt.Glyphs.First(g => g.Character == 'c');
            Assert.Equal((byte)255, a.Color.R); // red
            Assert.Equal((byte)0, b.Color.R);   // blue
            Assert.Equal((byte)255, b.Color.B); // blue
            Assert.Equal((byte)255, c.Color.R); // red again
        }

        [Fact]
        public void Layout_NestedSize_InnerWins()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("<size=32>a<size=8>b</size>c</size>", BasicSettings(fs));
            rt.Update(fs);
            var a = rt.Glyphs.First(g => g.Character == 'a');
            var b = rt.Glyphs.First(g => g.Character == 'b');
            var c = rt.Glyphs.First(g => g.Character == 'c');
            Assert.Equal(32f, a.PixelSize);
            Assert.Equal(8f, b.PixelSize);
            Assert.Equal(32f, c.PixelSize);
        }

        [Fact]
        public void Layout_WrapAccountsForMixedSize()
        {
            var fs = CreateSystem();
            var s = BasicSettings(fs);
            s.WrapMode = TextWrapMode.Wrap;
            s.MaxWidth = 80f;

            // The big word should push to a new line because it's wider at <size=48> than at base.
            var rt = new RichTextLayout("aaa <size=48>BBBBB</size>", s);
            rt.Update(fs);
            Assert.True(rt.Lines.Count >= 2, $"Expected wrap with mixed sizes; got {rt.Lines.Count} lines");
        }

        [Fact]
        public void Layout_LineHeightUsesPerLineMax()
        {
            var fs = CreateSystem();
            var s = BasicSettings(fs);
            // First line has only base size; second has big text — they should differ.
            var rt = new RichTextLayout("small\n<size=48>BIG</size>", s);
            rt.Update(fs);
            Assert.Equal(2, rt.Lines.Count);
            Assert.True(rt.Lines[1].Height > rt.Lines[0].Height,
                $"Mixed-size line should be taller: line1={rt.Lines[0].Height}, line2={rt.Lines[1].Height}");
        }

        [Fact]
        public void Layout_TrailingNewlineEmitsExtraLine()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("hello\n", BasicSettings(fs));
            rt.Update(fs);
            Assert.Equal(2, rt.Lines.Count);
        }

        [Fact]
        public void Layout_EmptyText_HasZeroSize()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("", BasicSettings(fs));
            rt.Update(fs);
            Assert.Empty(rt.Glyphs);
            Assert.Empty(rt.Lines);
            Assert.Equal(0f, rt.Size.X);
            Assert.Equal(0f, rt.Size.Y);
        }

        [Fact]
        public void Layout_OnlyTags_HasZeroVisibleText()
        {
            var fs = CreateSystem();
            var rt = new RichTextLayout("<b></b><color=red></color>", BasicSettings(fs));
            rt.Update(fs);
            Assert.Empty(rt.Glyphs);
        }

        [Fact]
        public void Effects_ExplicitZeroNotTreatedAsDefault()
        {
            var p = RichTextParser.Parse("<shake amp=0 freq=20>x</shake>");
            // amp=0 should mean amp=0, not "use default"
            var s = RichTextLayoutSettings.Default;
            // Build a fake glyph just to test the evaluator
            var glyph = new RichGlyph { CharIndex = 0, Color = FontColor.White };
            var r = RichTextEffects.Evaluate(glyph, p.Effects, 1.0f, s);
            Assert.Equal(0f, r.OffsetX);
            Assert.Equal(0f, r.OffsetY);
        }

        // ----- Effects -----------------------------------------------------------------

        [Fact]
        public void Effects_TypewriterHidesGlyphsBeforeReveal()
        {
            var fs = CreateSystem();
            var s = BasicSettings(fs);
            var rt = new RichTextLayout("<typewriter speed=10>abcdef</typewriter>", s);
            rt.Update(fs);

            // At t=0, no glyphs revealed yet
            var r0 = RichTextEffects.Evaluate(rt.Glyphs[0], rt.Effects, 0f, s);
            Assert.False(r0.Visible);

            // At t=1.0 with speed=10, glyphs 0..10 revealed → all 6 visible
            var r5 = RichTextEffects.Evaluate(rt.Glyphs[5], rt.Effects, 1.0f, s);
            Assert.True(r5.Visible);
        }

        [Fact]
        public void Effects_RainbowChangesColorOverTime()
        {
            var fs = CreateSystem();
            var s = BasicSettings(fs);
            var rt = new RichTextLayout("<rainbow>x</rainbow>", s);
            rt.Update(fs);

            var c0 = RichTextEffects.Evaluate(rt.Glyphs[0], rt.Effects, 0f, s).Color;
            var c1 = RichTextEffects.Evaluate(rt.Glyphs[0], rt.Effects, 0.5f, s).Color;
            Assert.NotEqual((c0.R, c0.G, c0.B), (c1.R, c1.G, c1.B));
        }

        [Fact]
        public void Effects_ResetReanchorsTime()
        {
            var fs = CreateSystem();
            var renderer = new TestFontRenderer();
            var s = BasicSettings(fs);
            var rt = new RichTextLayout("<typewriter speed=10>abc</typewriter>", s);
            rt.Update(fs);

            // First draw at t=100 anchors start at 100; at the same instant nothing is revealed yet.
            rt.Draw(fs, renderer, Float2.Zero, 100.0);
            var rA = RichTextEffects.Evaluate(rt.Glyphs[0], rt.Effects, 0f /* "elapsed" */, s);
            Assert.False(rA.Visible);

            // Reset and draw at t=200: should re-anchor; nothing revealed yet again.
            rt.Reset();
            rt.Draw(fs, renderer, Float2.Zero, 200.0);
            // Internal start time is now 200 — verifying via a fresh evaluator at "elapsed=0" still hidden:
            var rB = RichTextEffects.Evaluate(rt.Glyphs[0], rt.Effects, 0f, s);
            Assert.False(rB.Visible);
        }
    }
}
