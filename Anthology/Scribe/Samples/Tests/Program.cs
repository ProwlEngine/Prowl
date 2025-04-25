using Prowl.Scribe;
using Prowl.Scribe.Geometry;
using Scribe;
using System.Runtime.InteropServices;

namespace Tests
{
    internal class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, int mode);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr handle, out int mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int handle);

        static void Main(string[] args)
        {
            var handle = GetStdHandle(-11);
            int mode;
            GetConsoleMode(handle, out mode);
            SetConsoleMode(handle, mode | 0x4);
            
            using var stream = File.OpenRead("Fonts/Alamak.ttf");
            //var font = new Scribe.FontFace(stream);
            Typeface typeface = new Typeface(stream);

            string charactersToRender = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{};':\",./<>?";
            
            foreach (char character in charactersToRender)
            {
                Console.Clear();
                ushort glyphIndex;
                if (typeface.CharacterToGlyphMap.TryGetValue(character, out glyphIndex))
                {
                    Console.WriteLine($"Glyph index for '{character}': {glyphIndex}");
                }
                else
                {
                    Console.WriteLine($"Glyph index for '{character}' not found.");
                    Console.WriteLine("\nPress any key to render the next glyph...");
                    continue;
                }

                var geometry = typeface.GetGlyphOutline(glyphIndex, 64);

                if (geometry == null)
                {
                    Console.WriteLine("Outline not found.");
                    Console.WriteLine("\nPress any key to render the next glyph...");
                    return;
                }

                BitmapRenderer renderer = new BitmapRenderer();
                // returns a byte rgba array
                var colors = geometry.Render(renderer, 64, 64);

                // display the rendered glyph to the console
                for (int y = 63; y >= 0; y--)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        var r = colors[(y * 64 + x) * 4];
                        var g = colors[(y * 64 + x) * 4 + 1];
                        var b = colors[(y * 64 + x) * 4 + 2];
                        Console.Write($"\x1b[48;2;{r};{g};{b}m ");
                    }
                    Console.WriteLine();
                }

                Console.ResetColor();
                Console.WriteLine("\nPress any key to render the next glyph...");
                Console.ReadLine();
            }





            //void CheckFont(string path)
            //{
            //    Console.WriteLine($"Font: {Path.GetFileNameWithoutExtension(path)}");
            //    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            //    {
            //        Typeface typeface = new Typeface(fs);
            //        if(typeface.CharacterToGlyphMap.TryGetValue('A', out ushort glyphIndex))
            //        {
            //            Console.WriteLine($"Glyph index for 'A': {glyphIndex}");
            //        }
            //        else
            //        {
            //            Console.WriteLine("Glyph index for 'A' not found.");
            //        }
            //    }
            //}
            //
            //Console.WriteLine("\nPress any key to start testing...");
            //Console.ReadKey();
            //
            //// Foreach font
            //string[] fontFiles = Directory.GetFiles("Fonts/Tons", "*.ttf");
            //int fontCount = fontFiles.Length;
            //Console.WriteLine($"Found {fontCount} font files.");
            //Console.WriteLine("Testing fonts...");
            //int passCount = 0;
            //int count = 0;
            //foreach (string fontPath in fontFiles)
            //{
            //    count++;
            //    try
            //    {
            //        CheckFont(fontPath);
            //        passCount++;
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.ForegroundColor = ConsoleColor.Red;
            //        Console.WriteLine($"Error loading font {Path.GetFileNameWithoutExtension(fontPath)}: {ex.Message}");
            //        Console.ResetColor();
            //    }
            //
            //    Console.Title = $"Testing: {count}/{fontCount} - Pass: {passCount}, Fail: {fontCount - passCount}";
            //}
            //
            //Console.WriteLine("\nPress any key to exit...");
            //Console.ReadKey();
        }
    }
}