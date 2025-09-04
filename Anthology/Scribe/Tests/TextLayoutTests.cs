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

        [Fact]
        public void CursorPositionAtVariousPixelSizes()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var testText = "Hello World";
            
            var pixelSizes = new float[] { 8, 12, 16, 24, 32, 48 };
            
            foreach (var pixelSize in pixelSizes)
            {
                var settings = TextLayoutSettings.Default;
                settings.PixelSize = pixelSize;
                settings.Font = font;

                var layout = fs.CreateLayout(testText, settings);
                
                for (int i = 0; i <= testText.Length; i++)
                {
                    var pos = layout.GetCursorPosition(i);
                    var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
                    var idx = layout.GetCursorIndex(hit);
                    
                    Assert.Equal(i, idx);
                }
                
                Assert.True(layout.Size.Y >= pixelSize, $"Layout height should be at least pixel size {pixelSize}, got {layout.Size.Y}");
            }
        }

        [Fact]
        public void CursorPositionWithDifferentLetterSpacing()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var testText = "Spacing Test";
            
            var letterSpacings = new float[] { -2, -1, 0, 1, 2, 5 };
            
            foreach (var letterSpacing in letterSpacings)
            {
                var settings = TextLayoutSettings.Default;
                settings.PixelSize = 16;
                settings.Font = font;
                settings.LetterSpacing = letterSpacing;

                var layout = fs.CreateLayout(testText, settings);
                
                for (int i = 0; i <= testText.Length; i++)
                {
                    var pos = layout.GetCursorPosition(i);
                    var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
                    var idx = layout.GetCursorIndex(hit);
                    
                    Assert.Equal(i, idx);
                }
            }
        }

        [Fact]
        public void CursorPositionWithMultilineWrapping()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;
            settings.WrapMode = TextWrapMode.Wrap;
            settings.MaxWidth = 100;

            var testText = "This is a long line of text that should wrap to multiple lines when the max width is reached";
            var layout = fs.CreateLayout(testText, settings);
            
            Assert.True(layout.Lines.Count > 1, "Text should wrap to multiple lines");
            
            for (int i = 0; i <= testText.Length; i++)
            {
                var pos = layout.GetCursorPosition(i);
                var lineIdx = 0;
                for (int li = 0; li < layout.Lines.Count; li++)
                {
                    if (pos.Y <= layout.Lines[li].Position.Y + layout.Lines[li].Height)
                    {
                        lineIdx = li;
                        break;
                    }
                }
                
                var hit = new Vector2(pos.X, pos.Y + layout.Lines[lineIdx].Height * 0.5f);
                var idx = layout.GetCursorIndex(hit);
                
                Assert.Equal(i, idx);
            }
        }

        [Fact]
        public void CursorPositionWithTabsAndVariousTabSizes()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var testText = "a\tb\tc\td";
            
            var tabSizes = new int[] { 2, 4, 8 };
            
            foreach (var tabSize in tabSizes)
            {
                var settings = TextLayoutSettings.Default;
                settings.PixelSize = 16;
                settings.Font = font;
                settings.TabSize = tabSize;

                var layout = fs.CreateLayout(testText, settings);
                
                for (int i = 0; i <= testText.Length; i++)
                {
                    var pos = layout.GetCursorPosition(i);
                    var hit = new Vector2(pos.X, pos.Y + layout.Lines[0].Height * 0.5f);
                    var idx = layout.GetCursorIndex(hit);
                    
                    Assert.Equal(i, idx);
                }
            }
        }

        [Fact]
        public void StringSizeConsistencyAcrossPixelSizes()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var testStrings = new string[] 
            {
                "A",
                "Hello",
                "Hello World",
                "The quick brown fox jumps over the lazy dog"
            };
            
            var pixelSizes = new float[] { 8, 16, 24, 32 };
            
            foreach (var testText in testStrings)
            {
                var previousSize = Vector2.Zero;
                
                foreach (var pixelSize in pixelSizes)
                {
                    var settings = TextLayoutSettings.Default;
                    settings.PixelSize = pixelSize;
                    settings.Font = font;

                    var layout = fs.CreateLayout(testText, settings);
                    
                    Assert.True(layout.Size.X > 0, $"Layout width should be positive for '{testText}' at size {pixelSize}");
                    Assert.True(layout.Size.Y > 0, $"Layout height should be positive for '{testText}' at size {pixelSize}");
                    
                    if (previousSize != Vector2.Zero)
                    {
                        var ratio = pixelSize / (pixelSize == 8 ? 8 : pixelSizes[Array.IndexOf(pixelSizes, pixelSize) - 1]);
                        Assert.True(layout.Size.Y >= previousSize.Y, 
                            $"Layout height should increase with pixel size. Previous: {previousSize.Y}, Current: {layout.Size.Y}");
                    }
                    
                    previousSize = layout.Size;
                }
            }
        }

        [Fact]
        public void StringSizeWithEmptyAndWhitespace()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var emptyLayout = fs.CreateLayout("", settings);
            Assert.Equal(Vector2.Zero, emptyLayout.Size);

            var spaceLayout = fs.CreateLayout(" ", settings);
            Assert.True(spaceLayout.Size.X > 0, "Space should have positive width");
            Assert.True(spaceLayout.Size.Y > 0, "Space should have positive height");

            var tabLayout = fs.CreateLayout("\t", settings);
            Assert.True(tabLayout.Size.X > spaceLayout.Size.X, "Tab should be wider than space");

            // Test standalone newline - should create two lines and increase height
            var newlineLayout = fs.CreateLayout("\n", settings);
            Assert.True(newlineLayout.Size.Y > spaceLayout.Size.Y, 
                $"Standalone newline should increase height. Space height: {spaceLayout.Size.Y}, Newline height: {newlineLayout.Size.Y}");

            var multilineLayout = fs.CreateLayout("a\nb", settings);
            Assert.True(multilineLayout.Size.Y > spaceLayout.Size.Y, "Text with newline should increase height");
        }

        [Fact]
        public void StringSizeScalingWithLetterSpacing()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var testText = "ABC";
            
            var baseSettings = TextLayoutSettings.Default;
            baseSettings.PixelSize = 16;
            baseSettings.Font = font;
            baseSettings.LetterSpacing = 0;

            var baseLayout = fs.CreateLayout(testText, baseSettings);
            var baseWidth = baseLayout.Size.X;

            var spacingValues = new float[] { 1, 2, 5, 10 };
            
            foreach (var letterSpacing in spacingValues)
            {
                var settings = baseSettings;
                settings.LetterSpacing = letterSpacing;

                var layout = fs.CreateLayout(testText, settings);
                
                Assert.True(layout.Size.X > baseWidth, 
                    $"Layout with letter spacing {letterSpacing} should be wider than base. Base: {baseWidth}, Current: {layout.Size.X}");
            }
        }

        [Fact]
        public void StringSizeWithDifferentAlignments()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var testText = "Alignment Test";
            
            var alignments = new TextAlignment[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right };
            Vector2? previousSize = null;
            
            foreach (var alignment in alignments)
            {
                var settings = TextLayoutSettings.Default;
                settings.PixelSize = 16;
                settings.Font = font;
                settings.Alignment = alignment;

                var layout = fs.CreateLayout(testText, settings);
                
                if (previousSize.HasValue)
                {
                    // "Layout size should be consistent across different alignments"
                    Assert.Equal(previousSize.Value, layout.Size);
                }
                
                previousSize = layout.Size;
            }
        }

        [Fact]
        public void CursorPositionOutOfBounds()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var testText = "Test";
            var layout = fs.CreateLayout(testText, settings);
            
            var negativePos = layout.GetCursorPosition(-1);
            var zeroPos = layout.GetCursorPosition(0);
            Assert.Equal(zeroPos, negativePos);

            var beyondEndPos = layout.GetCursorPosition(testText.Length + 10);
            var endPos = layout.GetCursorPosition(testText.Length);
            Assert.Equal(endPos, beyondEndPos);
        }

        [Fact]
        public void CursorIndexFromExtremePositions()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var testText = "Test String";
            var layout = fs.CreateLayout(testText, settings);
            
            var farLeftIdx = layout.GetCursorIndex(new Vector2(-1000, layout.Lines[0].Position.Y + layout.Lines[0].Height * 0.5f));
            Assert.Equal(0, farLeftIdx);

            var farRightIdx = layout.GetCursorIndex(new Vector2(1000, layout.Lines[0].Position.Y + layout.Lines[0].Height * 0.5f));
            Assert.Equal(testText.Length, farRightIdx);

            var aboveIdx = layout.GetCursorIndex(new Vector2(layout.Size.X * 0.5f, -100));
            Assert.True(aboveIdx >= 0 && aboveIdx <= testText.Length);

            var belowIdx = layout.GetCursorIndex(new Vector2(layout.Size.X * 0.5f, layout.Size.Y + 100));
            Assert.True(belowIdx >= 0 && belowIdx <= testText.Length);
        }

        [Fact]
        public void NewlineBehaviorTest()
        {
            var fs = CreateSystem();
            var font = fs.FallbackFonts.First();
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = 16;
            settings.Font = font;

            var singleLineLayout = fs.CreateLayout("a", settings);
            var doubleLineLayout = fs.CreateLayout("a\nb", settings);
            var trailingNewlineLayout = fs.CreateLayout("a\n", settings);
            var leadingNewlineLayout = fs.CreateLayout("\na", settings);
            var standaloneNewlineLayout = fs.CreateLayout("\n", settings);

            // Test proper line counts for different newline scenarios
            Assert.Equal(1, singleLineLayout.Lines.Count);
            Assert.Equal(2, doubleLineLayout.Lines.Count);
            Assert.Equal(2, trailingNewlineLayout.Lines.Count);
            Assert.Equal(2, leadingNewlineLayout.Lines.Count);
            Assert.Equal(2, standaloneNewlineLayout.Lines.Count);
            
            // Height should be proportional to line count
            Assert.True(doubleLineLayout.Size.Y > singleLineLayout.Size.Y, "Double line should be taller than single line");
            Assert.True(trailingNewlineLayout.Size.Y > singleLineLayout.Size.Y, "Trailing newline should be taller than single line");
            Assert.True(leadingNewlineLayout.Size.Y > singleLineLayout.Size.Y, "Leading newline should be taller than single line");
            Assert.True(standaloneNewlineLayout.Size.Y > singleLineLayout.Size.Y, "Standalone newline should be taller than single line");
        }
    }
}