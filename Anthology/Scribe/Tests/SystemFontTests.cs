using Prowl.Scribe;
using System.Runtime.InteropServices;

namespace Tests
{
    public class SystemFontTests
    {
        [Fact]
        public void LoadSystemFontsLoadsDejaVuSans()
        {
            var renderer = new TestFontRenderer();
            var fs = new FontSystem(renderer);
            fs.LoadSystemFonts();

            Assert.True(fs.Fonts.Any(), "No system fonts were loaded.");

            string[] expectedFonts;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                expectedFonts = new[] { "Arial", "Segoe UI" };
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                expectedFonts = new[] { "Helvetica", "Arial" };
            else
                expectedFonts = new[] { "DejaVu Sans", "Liberation Sans" };

            bool found = expectedFonts.Any(name => fs.GetFont(name) != null);
            Assert.True(found, $"None of the expected fonts were found. Loaded fonts: {string.Join(", ", fs.Fonts.Select(f => f.FamilyName))}");
        }
    }
}