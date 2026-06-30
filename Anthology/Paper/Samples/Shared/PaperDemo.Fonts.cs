using System.Reflection;

using Prowl.PaperUI;
using Prowl.Scribe;

namespace Shared
{
    public static class Fonts
    {
        public static FontFile arial;
        public static FontFile arialb;
        public static FontFile ariali;
        public static FontFile arialbi;

        public static FontFile consola;

        public static void Initialize(Paper gui)
        {
            // Load fonts with different sizes
            arial = LoadEmbeddedFont("arial");
            arialb = LoadEmbeddedFont("arialb");
            ariali = LoadEmbeddedFont("ariali");
            arialbi = LoadEmbeddedFont("arialbi");

            consola = LoadEmbeddedFont("consola");

            // Add FontAwesome as a Fallback font
            var faReg = LoadEmbeddedFont("fa-regular-400");
            var faSolid = LoadEmbeddedFont("fa-solid-900");

            gui.AddFallbackFont(faReg);
            gui.AddFallbackFont(faSolid);
        }

        private static FontFile LoadEmbeddedFont(string fontName)
        {
            using (Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Shared.EmbeddedResources.{fontName}.ttf"))
            {
                if (stream == null) throw new Exception("Could not load font resource");
                return new FontFile(stream);
            }
        }
    }
}
