using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace OrigamiSample
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // Optional headless screenshot mode: --shot <path> [--frames N] [--cat N]
            // Text-renderer comparison mode: --textcompare <path> (fixed 700x260 canvas).
            string shotPath = null;
            int shotFrames = 12;
            int initialCat = 0;
            bool textCompare = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shotPath = args[++i];
                else if (args[i] == "--frames" && i + 1 < args.Length) int.TryParse(args[++i], out shotFrames);
                else if (args[i] == "--cat" && i + 1 < args.Length) int.TryParse(args[++i], out initialCat);
                else if (args[i] == "--textcompare" && i + 1 < args.Length) { shotPath = args[++i]; textCompare = true; }
            }

            var size = textCompare ? new Vector2i(TextCompare.Width, TextCompare.Height) : new Vector2i(1280, 832);
            var nativeWindowSettings = new NativeWindowSettings
            {
                ClientSize = size,
                Title = "Prowl Origami - Widget Playground",
                Flags = ContextFlags.ForwardCompatible,
                Vsync = VSyncMode.On,
                StartVisible = shotPath == null,
            };

            using var app = new PaperTKWindow(GameWindowSettings.Default, nativeWindowSettings, shotPath, shotFrames, initialCat, textCompare);
            app.Run();
        }
    }
}
