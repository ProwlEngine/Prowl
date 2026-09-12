// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class GlyphModifierTests
{
    private sealed class CapturingRenderer : IFontRenderer
    {
        public readonly List<IFontRenderer.Vertex> Vertices = [];

        public object CreateTexture(int width, int height) => new byte[width * height];
        public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }

        public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
        {
            foreach (var v in vertices) Vertices.Add(v);
        }

        public void Clear() => Vertices.Clear();
    }

    private static (FontSystem fonts, CapturingRenderer renderer, TextLayoutSettings settings) Setup(bool underline = false)
    {
        var renderer = new CapturingRenderer();
        var fonts = new FontSystem(renderer);
        foreach (var f in fonts.EnumerateSystemFonts())
        {
            if (f.Style != FontStyle.Regular) continue;
            fonts.AddFallbackFont(f);
            break;
        }

        var settings = TextLayoutSettings.Default;
        settings.Font = fonts.FallbackFonts.First();
        settings.PixelSize = 32f;
        settings.Underline = underline;
        return (fonts, renderer, settings);
    }

    private static List<Float2> Positions(CapturingRenderer renderer) =>
        renderer.Vertices.ConvertAll(v => new Float2(v.Position.X, v.Position.Y));

    [Fact]
    public void AModifierThatChangesNothingDrawsTheSameGeometry()
    {
        var (fonts, renderer, settings) = Setup();
        var layout = fonts.CreateLayout("Hello", settings);

        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255));
        var plain = Positions(renderer);

        renderer.Clear();
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) => { });
        var modified = Positions(renderer);

        Assert.NotEmpty(plain);
        Assert.Equal(plain.Count, modified.Count);
        for (int i = 0; i < plain.Count; i++)
        {
            Assert.Equal(plain[i].X, modified[i].X, 5);
            Assert.Equal(plain[i].Y, modified[i].Y, 5);
        }
    }

    [Fact]
    public void EveryGlyphArrivesWithItsCharacterIndexAndSize()
    {
        var (fonts, _, settings) = Setup();
        var layout = fonts.CreateLayout("abcd", settings);

        var seen = new List<int>();
        float size = 0f;
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            seen.Add(g.CharIndex);
            size = g.PixelSize;
        });

        Assert.Equal([0, 1, 2, 3], seen);
        Assert.Equal(32f, size, 3);
    }

    [Fact]
    public void CornersCanBeMovedIndependently()
    {
        var (fonts, renderer, settings) = Setup();
        var layout = fonts.CreateLayout("A", settings);

        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            // Shear: the top edge leans right, the bottom stays put.
            g.SetCorners(
                new Float2(g.TopLeft.X + 10f, g.TopLeft.Y),
                new Float2(g.TopRight.X + 10f, g.TopRight.Y),
                g.BottomLeft,
                g.BottomRight);
        });

        var v = renderer.Vertices;
        Assert.Equal(4, v.Count);
        Assert.Equal(v[0].Position.X - 10f, v[2].Position.X, 4); // top left vs bottom left
        Assert.Equal(v[1].Position.X - 10f, v[3].Position.X, 4);
    }

    [Fact]
    public void AGlyphCanBeHidden()
    {
        var (fonts, renderer, settings) = Setup();
        var layout = fonts.CreateLayout("abcd", settings);

        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            if (g.CharIndex == 1) g.Visible = false;
        });

        Assert.Equal(12, renderer.Vertices.Count); // three of four glyphs
    }

    [Fact]
    public void ColourIsPerGlyph()
    {
        var (fonts, renderer, settings) = Setup();
        var layout = fonts.CreateLayout("ab", settings);

        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            if (g.CharIndex == 1) g.Color = new FontColor((byte)255, (byte)0, (byte)0);
        });

        Assert.Equal(255, renderer.Vertices[0].Color.G);
        Assert.Equal(0, renderer.Vertices[^1].Color.G);
    }

    [Fact]
    public void DecorationBarsArriveWithoutACharacterIndex()
    {
        var (fonts, _, settings) = Setup(underline: true);
        var layout = fonts.CreateLayout("Hello", settings);

        int bars = 0;
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255), (ref GlyphDraw g) =>
        {
            if (g.CharIndex < 0) bars++;
        });

        Assert.Equal(1, bars);
    }

    [Fact]
    public void ModifyingDoesNotDisturbALaterPlainDraw()
    {
        var (fonts, renderer, settings) = Setup();
        var layout = fonts.CreateLayout("Hello", settings);

        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255));
        var before = Positions(renderer);

        renderer.Clear();
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255),
            (ref GlyphDraw g) => g.SetCorners(Float2.Zero, Float2.Zero, Float2.Zero, Float2.Zero));

        renderer.Clear();
        fonts.DrawLayout(layout, Float2.Zero, new FontColor(255, 255, 255));
        var after = Positions(renderer);

        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].X, after[i].X, 5);
            Assert.Equal(before[i].Y, after[i].Y, 5);
        }
    }
}
