// Program.cs
using System;

namespace SFMLExample
{
    /// <summary>
    /// Main entry point for the SFML Quill example
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Create SFML window
            using (var window = new SFMLWindow(1280, 720, "SFML Quill Example"))
            {
                window.Run();
            }
        }
    }
}