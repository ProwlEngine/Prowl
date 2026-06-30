using Prowl.Scribe;
using System.Runtime.InteropServices;

namespace Tests
{
    public class SystemFontTests
    {
        [Fact]
        public void LoadSystemFontsLoadsDejaVuSans()
        {
            //var renderer = new TestFontRenderer();
            //var fs = new FontSystem(renderer);
            //fs.LoadSystemFonts();
            //
            //Assert.True(fs.Fonts.Any(), "No system fonts were loaded.");
            //
            //FontFilter[] expectedFonts;
            //
            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            //    expectedFonts = new[] { new FontFilter(FontStyle.Regular, "Arial"), new FontFilter(FontStyle.Regular, "Segoe UI") };
            //else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            //    expectedFonts = new[] { new FontFilter(FontStyle.Regular, "Helvetica"), new FontFilter(FontStyle.Regular, "Arial") };
            //else
            //    expectedFonts = new[] { new FontFilter(FontStyle.Regular, "DejaVu Sans"), new FontFilter(FontStyle.Regular, "Liberation Sans") };
            //
            //bool found = expectedFonts.Any(name => fs.GetFont(name) != null);
            //Assert.True(found, $"None of the expected fonts were found. Loaded fonts: {string.Join(", ", fs.Fonts.Select(f => f.FamilyName))}");
        }
    }
}