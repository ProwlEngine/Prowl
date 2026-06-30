using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace OpenTKExample
{
    /// <summary>
    /// Main entry point for the Quill example
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Create window with appropriate settings
            var nativeWindowSettings = new NativeWindowSettings {
                ClientSize = new(1280, 720),
                Title = "OpenTK Quill Example",
                WindowBorder = WindowBorder.Resizable,
                API = ContextAPI.OpenGL,
                Profile = ContextProfile.Core,
                APIVersion = new Version(3, 3)
            };

            // Run the application
            using (var window = new OpenTKWindow(GameWindowSettings.Default, nativeWindowSettings))
            {
                window.Run();
            }
        }
    }
}