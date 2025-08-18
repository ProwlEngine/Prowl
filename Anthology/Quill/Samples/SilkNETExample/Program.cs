using Silk.NET.Windowing;

namespace SilkExample
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Create window with simpler configuration
            var options = WindowOptions.Default with
            {
                Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
                Title = "Silk.NET Quill Example",
                VSync = true
            };

            // Create and run the window
            using var window = new SilkWindow(options);
            window.Run();
        }
    }
}