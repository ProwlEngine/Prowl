using System.IO;
using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace OrigamiSample
{
    // Draws a fixed set of text lines (Geist-Regular, the chat-bubble font) at exact sizes and
    // positions so an HTML DOM render and this Paper/Quill render can be compared side by side.
    // The line spec MUST stay identical to Samples/OpenTK/_textcompare.html.
    public static class TextCompare
    {
        // (fontSizePx, topY, text) — identical to the HTML.
        public static readonly (float size, float y, string text)[] Lines =
        {
            (12f,   18f,  "The quick brown fox jumps over the lazy dog"),
            (14f,   48f,  "The quick brown fox jumps over the lazy dog"),
            (16f,   82f,  "The quick brown fox jumps over the lazy dog"),
            (20f,   122f, "The quick brown fox jumps over the lazy dog"),
            (13f,   172f, "Did the atmosphere shader compile?"),
            (24f,   205f, "Abg Prowl 0123 - Widget"),
        };

        public const int Width = 700, Height = 260;
        public const float MarginX = 24f;

        private static FontFile _font;

        public static void Render(Paper P, int w, int h)
        {
            if (_font == null)
            {
                string dir = Path.Combine(System.AppContext.BaseDirectory, "Fonts");
                using var s = File.OpenRead(Path.Combine(dir, "Geist-Regular.ttf"));
                _font = new FontFile(s);
            }

            using (P.Box("tc").PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(P.Percent(100)).Height(P.Percent(100)).Enter())
            {
                P.Draw((vg, rect) =>
                {
                    float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
                    vg.BeginPath();
                    vg.Rect(ox, oy, (float)rect.Size.X, (float)rect.Size.Y);
                    vg.SetFillColor(Color32.FromArgb(255, 21, 18, 29)); // #15121d
                    vg.Fill();

                    var ink = Color32.FromArgb(255, 240, 238, 247); // #f0eef7
                    foreach (var (size, y, text) in Lines)
                        vg.DrawText(text, ox + MarginX, oy + y, ink, size, _font);
                });
            }
        }
    }
}
