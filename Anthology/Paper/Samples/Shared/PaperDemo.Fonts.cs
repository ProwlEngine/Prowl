using System.Reflection;

using Prowl.PaperUI;

namespace Shared
{
    public static class Fonts
    {
        public static void Initialize(Paper gui)
        {
            // Load fonts with different sizes
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream? stream = assembly.GetManifestResourceStream("Shared.EmbeddedResources.font.ttf"))
            {
                if (stream == null) throw new Exception("Could not load font resource");
                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                gui.AddFont(data);
            }
            using (Stream? stream = assembly.GetManifestResourceStream("Shared.EmbeddedResources.fa-regular-400.ttf"))
            {
                if (stream == null) throw new Exception("Could not load font resource");
                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                gui.AddFont(data);
            }
            using (Stream? stream = assembly.GetManifestResourceStream("Shared.EmbeddedResources.fa-solid-900.ttf"))
            {
                if (stream == null) throw new Exception("Could not load font resource");
                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                gui.AddFont(data);
            }
        }
    }
}
