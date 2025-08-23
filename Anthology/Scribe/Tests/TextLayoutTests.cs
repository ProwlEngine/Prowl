using Prowl.Scribe;
using System.Numerics;

namespace Tests
{
    public class TextLayoutTests
    {
        class TestFontRenderer : IFontRenderer
        {
            public object CreateTexture(int width, int height) => new byte[width * height];
            public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }
            public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices) { }
        }

        FontSystem CreateSystem()
        {
            var renderer = new TestFontRenderer();
            var fs = new FontSystem(renderer);
            fs.LoadSystemFonts("arial");
            return fs;
        }

        [Fact]
        public void CursorPositionRoundtrip()
        {
            var fs = CreateSystem();
            var font = fs.Fonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.PreferredFont = font;

            var layout = fs.CreateLayout("Just testing to make sure this works\nas it should.", settings);

            for (int i = 0; i <= layout.Text.Length; i++)
            {
                var pos = layout.GetCursorPosition(i);
                // ensure Y inside line for hit testing
                var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
                var idx = layout.GetCursorIndex(hit);
                Assert.Equal(i, idx);
            }
        }
    }
}