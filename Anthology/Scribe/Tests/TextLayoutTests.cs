using Prowl.Scribe;
using System.Numerics;
using System.Linq;

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
            foreach(var font in fs.EnumerateSystemFonts())
            {
                if(font.Style == FontStyle.Regular)
                {
                    fs.AddFallbackFont(font);
                    break;
                }
            }
            return fs;
        }

        [Fact]
        public void CursorPositionRoundtrip()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var layout = fs.CreateLayout("Just testing to make sure this works\nas it should.", settings);

            for (int i = 0; i <= layout.Text.Length; i++)
            {
                var pos = layout.GetCursorPosition(i);
                // ensure Y inside line for hit testing
                var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
                var idx = layout.GetCursorIndex(hit);
                if (idx != i)
                {
                    Assert.True(false, $"Mismatch at index {i}: expected {i}, got {idx}. Position: {pos}, Hit: {hit}, Text: '{layout.Text.Substring(Math.Max(0, i-2), Math.Min(5, layout.Text.Length - Math.Max(0, i-2)))}'");
                }
            }
        }

        [Fact]
        public void CursorPositionRoundtripWithTrailingNewline()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var layout = fs.CreateLayout("line\n", settings);

            // Debug information
            Assert.Equal(5, layout.Text.Length); // "line\n" has length 5
            Assert.True(layout.Lines.Count >= 1);
            
            // Check line boundaries
            var firstLine = layout.Lines[0];
            
            // Test cursor at the end after newline
            int endIndex = layout.Text.Length;
            var pos = layout.GetCursorPosition(endIndex);
            var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
            var idx = layout.GetCursorIndex(hit);
            
            // More detailed assertion with debug info
            if (idx != endIndex)
            {
                Assert.True(false, $"Expected cursor index {endIndex}, got {idx}. Line count: {layout.Lines.Count}, First line EndIndex: {firstLine.EndIndex}, Position: {pos}, Hit: {hit}");
            }
        }

        [Fact]
        public void CursorPositionRoundtripWithTrailingTab()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var layout = fs.CreateLayout("text\t", settings);

            // Debug information
            Assert.Equal(5, layout.Text.Length); // "text\t" has length 5
            Assert.True(layout.Lines.Count >= 1);
            
            // Check line boundaries
            var firstLine = layout.Lines[0];
            
            // Test cursor at the end after tab
            int endIndex = layout.Text.Length;
            var pos = layout.GetCursorPosition(endIndex);
            var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
            var idx = layout.GetCursorIndex(hit);
            
            // More detailed assertion with debug info
            if (idx != endIndex)
            {
                Assert.True(false, $"Expected cursor index {endIndex}, got {idx}. Line count: {layout.Lines.Count}, First line EndIndex: {firstLine.EndIndex}, Position: {pos}, Hit: {hit}");
            }
        }
    }
}