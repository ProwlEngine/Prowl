using Prowl.Quill;
using Prowl.Vector;
using System.Drawing;

namespace Common
{
    /// <summary>
    /// Compact font definition and string drawing implementation
    /// </summary>
    public static class VectorFont
    {
        // Character definitions as arrays of line segments
        // Each segment is defined by two points (x1, y1, x2, y2) as a fraction of character cell (0-1)
        private static readonly Dictionary<char, double[][]> CharacterDefinitions = new Dictionary<char, double[][]> {
            // Upper case letters
            ['A'] = [[0, 1, 0.5, 0, 1, 1], [0.2, 0.6, 0.8, 0.6]],
            ['B'] = [[0, 0, 0, 1, 0.7, 1, 0.9, 0.8, 0.7, 0.5, 0, 0.5], [0, 0, 0.7, 0, 0.9, 0.2, 0.7, 0.5]],
            ['C'] = [[0.9, 0.2, 0.7, 0, 0.3, 0, 0.1, 0.2, 0.1, 0.8, 0.3, 1, 0.7, 1, 0.9, 0.8]],
            ['D'] = [[0, 0, 0, 1, 0.6, 1, 0.9, 0.7, 0.9, 0.3, 0.6, 0, 0, 0]],
            ['E'] = [[0.9, 0, 0, 0, 0, 1, 0.9, 1], [0, 0.5, 0.6, 0.5]],
            ['F'] = [[0.9, 0, 0, 0, 0, 1], [0, 0.5, 0.6, 0.5]],
            ['G'] = [[0.9, 0.2, 0.7, 0, 0.3, 0, 0.1, 0.2, 0.1, 0.8, 0.3, 1, 0.7, 1, 0.9, 0.8, 0.9, 0.5, 0.5, 0.5]],
            ['H'] = [[0, 0, 0, 1], [1, 0, 1, 1], [0, 0.5, 1, 0.5]],
            ['I'] = [[0.2, 0, 0.8, 0], [0.5, 0, 0.5, 1], [0.2, 1, 0.8, 1]],
            ['J'] = [[0.8, 0, 0.8, 0.8, 0.6, 1, 0.3, 1, 0.1, 0.8]],
            ['K'] = [[0, 0, 0, 1], [0, 0.5, 0.8, 0], [0, 0.5, 0.8, 1]],
            ['L'] = [[0, 0, 0, 1, 0.9, 1]],
            ['M'] = [[0, 1, 0, 0, 0.5, 0.4, 1, 0, 1, 1]],
            ['N'] = [[0, 1, 0, 0, 1, 1, 1, 0]],
            ['O'] = [[0.3, 0, 0.7, 0, 1, 0.3, 1, 0.7, 0.7, 1, 0.3, 1, 0, 0.7, 0, 0.3, 0.3, 0]],
            ['P'] = [[0, 1, 0, 0, 0.7, 0, 0.9, 0.2, 0.7, 0.4, 0, 0.4]],
            ['Q'] = [[0.3, 0, 0.7, 0, 1, 0.3, 1, 0.7, 0.7, 1, 0.3, 1, 0, 0.7, 0, 0.3, 0.3, 0], [0.6, 0.8, 1, 1]],
            ['R'] = [[0, 1, 0, 0, 0.7, 0, 0.9, 0.2, 0.7, 0.4, 0, 0.4], [0.4, 0.4, 0.9, 1]],
            ['S'] = [[0.9, 0.2, 0.7, 0, 0.3, 0, 0.1, 0.2, 0.3, 0.4, 0.7, 0.6, 0.9, 0.8, 0.7, 1, 0.3, 1, 0.1, 0.8]],
            ['T'] = [[0, 0, 1, 0], [0.5, 0, 0.5, 1]],
            ['U'] = [[0, 0, 0, 0.7, 0.3, 1, 0.7, 1, 1, 0.7, 1, 0]],
            ['V'] = [[0, 0, 0.5, 1, 1, 0]],
            ['W'] = [[0, 0, 0.2, 1, 0.5, 0.5, 0.8, 1, 1, 0]],
            ['X'] = [[0, 0, 1, 1], [0, 1, 1, 0]],
            ['Y'] = [[0, 0, 0.5, 0.5, 1, 0], [0.5, 0.5, 0.5, 1]],
            ['Z'] = [[0, 0, 1, 0, 0, 1, 1, 1]],

            //// Numbers// Numbers
            ['0'] = [[0.3, 0, 0.7, 0, 1, 0.3, 1, 0.7, 0.7, 1, 0.3, 1, 0, 0.7, 0, 0.3, 0.3, 0], [0, 0.1, 1, 0.9]],
            ['1'] = [[0.3, 0.2, 0.5, 0, 0.5, 1], [0.3, 1, 0.7, 1]],
            ['2'] = [[0.1, 0.2, 0.3, 0, 0.7, 0, 0.9, 0.2, 0.9, 0.4, 0.1, 1, 0.9, 1]],
            ['3'] = [[0.1, 0.2, 0.3, 0, 0.7, 0, 0.9, 0.2, 0.9, 0.4, 0.5, 0.5, 0.9, 0.6, 0.9, 0.8, 0.7, 1, 0.3, 1, 0.1, 0.8]],
            ['4'] = [[0.7, 0, 0.7, 1], [0, 0.7, 0.9, 0.7], [0.7, 0, 0, 0.7]],
            ['5'] = [[0.9, 0, 0.1, 0, 0.1, 0.5, 0.7, 0.5, 0.9, 0.7, 0.9, 0.9, 0.7, 1, 0.3, 1, 0.1, 0.9]],
            ['6'] = [[0.9, 0.1, 0.7, 0, 0.3, 0, 0.1, 0.2, 0.1, 0.8, 0.3, 1, 0.7, 1, 0.9, 0.8, 0.9, 0.6, 0.7, 0.5, 0.3, 0.5, 0.1, 0.6]],
            ['7'] = [[0.1, 0, 0.9, 0, 0.5, 1], [0.3, 0.5, 0.7, 0.5]],
            ['8'] = [[0.3, 0, 0.7, 0, 0.9, 0.2, 0.9, 0.3, 0.7, 0.5, 0.3, 0.5, 0.1, 0.3, 0.1, 0.2, 0.3, 0], [0.3, 0.5, 0.1, 0.7, 0.1, 0.8, 0.3, 1, 0.7, 1, 0.9, 0.8, 0.9, 0.7, 0.7, 0.5]],
            ['9'] = [[0.1, 0.9, 0.3, 1, 0.7, 1, 0.9, 0.8, 0.9, 0.2, 0.7, 0, 0.3, 0, 0.1, 0.2, 0.1, 0.4, 0.3, 0.5, 0.7, 0.5, 0.9, 0.4]],

            ['.'] = [[0.5, 1.0, 0.5, 0.8]],
            [' '] = []  // Space character has no lines
        };

        /// <summary>
        /// Draws a string using vector lines
        /// </summary>
        /// <param name="canvas">The canvas to draw on</param>
        /// <param name="text">The text to draw</param>
        /// <param name="x">X position of the text</param>
        /// <param name="y">Y position of the text</param>
        /// <param name="height">Height of the text</param>
        /// <param name="color">Color of the text</param>
        /// <param name="lineWidth">Width of the strokes</param>
        /// <param name="spacing">Spacing between characters (0-1)</param>
        public static void DrawString(Canvas canvas, string text, double x, double y, double height, Color32 color, double lineWidth = 1.0f, double spacing = 0.3f)
        {
            if (string.IsNullOrEmpty(text))
                return;

            double charWidth = height * 0.6f;
            double totalWidth = (charWidth * text.Length) + (spacing * height * (text.Length - 1));
            double currentX = x;

            canvas.SaveState();

            canvas.SetStrokeColor(color);
            canvas.SetStrokeWidth(lineWidth);
            canvas.SetStrokeJoint(JointStyle.Round);
            canvas.SetStrokeCap(EndCapStyle.Square);

            foreach (char c in text)
            {
                DrawCharacter(canvas, c, currentX, y, height);
                currentX += charWidth + (spacing * height);
            }
            canvas.RestoreState();
        }

        public static double MeasureString(string text, double height, double spacing = 0.3) => ((height * 0.6) + (spacing * height))  * (text.Length);

        /// <summary>
        /// Draws a string using vector lines with the text centered at the given position
        /// </summary>
        public static void DrawStringCentered(Canvas canvas, string text, double x, double y, double height, Color32 color, double lineWidth = 1.0f, double spacing = 0.2f)
        {
            if (string.IsNullOrEmpty(text))
                return;

            double charWidth = height * 0.6f;
            double totalWidth = (charWidth * text.Length) + (spacing * height * (text.Length - 1));
            double startX = x - (totalWidth / 2);

            DrawString(canvas, text, startX, y, height, color, lineWidth, spacing);
        }

        /// <summary>
        /// Draws a character using vector lines
        /// </summary>
        private static void DrawCharacter(Canvas canvas, char c, double x, double y, double height)
        {
            // Default to space if character not found
            if (!CharacterDefinitions.TryGetValue(c, out double[][] sections))
                return;

            // Skip if no lines (space)
            if (sections.Length == 0)
                return;

            double width = height * 0.6f;

            // Draw each line segment
            for (int i = 0; i < sections.Length; i++)
            {
                canvas.BeginPath();
                canvas.MoveTo(
                    x + (sections[i][0] * width),
                    y + (sections[i][1] * height)
                );
                for (int j = 1; j < sections[i].Length / 2; j++)
                {
                    canvas.LineTo(
                        x + (sections[i][(j * 2) + 0] * width),
                        y + (sections[i][(j * 2) + 1] * height)
                    );
                }
                canvas.Stroke();
            }
        }
    }
}
