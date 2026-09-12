using Prowl.PaperUI;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class TextMaskTests
{
    private sealed class NullRenderer : IFontRenderer
    {
        public object CreateTexture(int width, int height) => new byte[width * height];
        public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }
        public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices) { }
    }

    private static FontFile LoadFont()
    {
        using var stream = typeof(TextMaskTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    private static TextLayout Layout(FontSystem fonts, FontFile font, string text, char? mask)
    {
        var settings = TextLayoutSettings.Default;
        settings.Font = font;
        settings.PixelSize = 20f;
        settings.Customizer = TextMask.For(mask);

        var layout = new TextLayout();
        fonts.UpdateLayout(layout, text, settings);
        return layout;
    }

    private static List<GlyphInstance> Glyphs(TextLayout layout)
    {
        var all = new List<GlyphInstance>();
        foreach (var line in layout.Lines) all.AddRange(line.Glyphs);
        return all;
    }

    [Fact]
    public void NoMaskIsNoCustomizer() => Assert.Null(TextMask.For(null));

    [Fact]
    public void TheSameMaskCharacterReusesOneCustomizer()
    {
        // Scribe's layout cache matches customizers by identity, so a fresh delegate per frame
        // would miss the cache every frame.
        Assert.Same(TextMask.For('*'), TextMask.For('*'));
        Assert.NotSame(TextMask.For('*'), TextMask.For('.'));
    }

    [Fact]
    public void EveryCharacterIsDrawnAsTheMask()
    {
        var font = LoadFont();
        var fonts = new FontSystem(new NullRenderer());

        var masked = Layout(fonts, font, "hunter2", '*');

        Assert.Equal("hunter2", masked.Text);
        foreach (var g in Glyphs(masked)) Assert.Equal('*', g.Character);
    }

    [Fact]
    public void MaskedTextMeasuresAsTheMaskNotTheSecret()
    {
        var font = LoadFont();
        var fonts = new FontSystem(new NullRenderer());

        // 'i' and 'W' are nothing like the same width, so an unmasked layout gives the length away.
        var thin = Layout(fonts, font, "iiiiiii", '*');
        var wide = Layout(fonts, font, "WWWWWWW", '*');
        var plain = Layout(fonts, font, "*******", null);

        Assert.Equal(plain.Size.X, thin.Size.X, 3);
        Assert.Equal(plain.Size.X, wide.Size.X, 3);
    }

    [Fact]
    public void CursorIndicesStillPointAtTheRealText()
    {
        var font = LoadFont();
        var fonts = new FontSystem(new NullRenderer());

        var masked = Layout(fonts, font, "hunter2", '*');
        var glyphs = Glyphs(masked);

        Assert.Equal(7, glyphs.Count);
        for (int i = 0; i < glyphs.Count; i++) Assert.Equal(i, glyphs[i].CharIndex);
    }

    [Fact]
    public void LineBreaksSurviveTheMask()
    {
        var font = LoadFont();
        var fonts = new FontSystem(new NullRenderer());

        var masked = Layout(fonts, font, "ab\ncd", '*');

        Assert.Equal(2, masked.Lines.Count);
        Assert.Equal(4, Glyphs(masked).Count);
    }
}
