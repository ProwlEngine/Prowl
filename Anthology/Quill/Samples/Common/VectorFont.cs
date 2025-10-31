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
        private static readonly Dictionary<char, float[][]> CharacterDefinitions = new Dictionary<char, float[][]> {
            // Upper case letters
            ['A'] = [[0, 1, 0.5f, 0, 1, 1], [0.2f, 0.6f, 0.8f, 0.6f]],
            ['B'] = [[0, 0, 0, 1, 0.7f, 1, 0.9f, 0.8f, 0.7f, 0.5f, 0, 0.5f], [0, 0, 0.7f, 0, 0.9f, 0.2f, 0.7f, 0.5f]],
            ['C'] = [[0.9f, 0.2f, 0.7f, 0, 0.3f, 0, 0.1f, 0.2f, 0.1f, 0.8f, 0.3f, 1, 0.7f, 1, 0.9f, 0.8f]],
            ['D'] = [[0, 0, 0, 1, 0.6f, 1, 0.9f, 0.7f, 0.9f, 0.3f, 0.6f, 0, 0, 0]],
            ['E'] = [[0.9f, 0, 0, 0, 0, 1, 0.9f, 1], [0, 0.5f, 0.6f, 0.5f]],
            ['F'] = [[0.9f, 0, 0, 0, 0, 1], [0, 0.5f, 0.6f, 0.5f]],
            ['G'] = [[0.9f, 0.2f, 0.7f, 0, 0.3f, 0, 0.1f, 0.2f, 0.1f, 0.8f, 0.3f, 1, 0.7f, 1, 0.9f, 0.8f, 0.9f, 0.5f, 0.5f, 0.5f]],
            ['H'] = [[0, 0, 0, 1], [1, 0, 1, 1], [0, 0.5f, 1, 0.5f]],
            ['I'] = [[0.2f, 0, 0.8f, 0], [0.5f, 0, 0.5f, 1], [0.2f, 1, 0.8f, 1]],
            ['J'] = [[0.8f, 0, 0.8f, 0.8f, 0.6f, 1, 0.3f, 1, 0.1f, 0.8f]],
            ['K'] = [[0, 0, 0, 1], [0, 0.5f, 0.8f, 0], [0, 0.5f, 0.8f, 1]],
            ['L'] = [[0, 0, 0, 1, 0.9f, 1]],
            ['M'] = [[0, 1, 0, 0, 0.5f, 0.4f, 1, 0, 1, 1]],
            ['N'] = [[0, 1, 0, 0, 1, 1, 1, 0]],
            ['O'] = [[0.3f, 0, 0.7f, 0, 1, 0.3f, 1, 0.7f, 0.7f, 1, 0.3f, 1, 0, 0.7f, 0, 0.3f, 0.3f, 0]],
            ['P'] = [[0, 1, 0, 0, 0.7f, 0, 0.9f, 0.2f, 0.7f, 0.4f, 0, 0.4f]],
            ['Q'] = [[0.3f, 0, 0.7f, 0, 1, 0.3f, 1, 0.7f, 0.7f, 1, 0.3f, 1, 0, 0.7f, 0, 0.3f, 0.3f, 0], [0.6f, 0.8f, 1, 1]],
            ['R'] = [[0, 1, 0, 0, 0.7f, 0, 0.9f, 0.2f, 0.7f, 0.4f, 0, 0.4f], [0.4f, 0.4f, 0.9f, 1]],
            ['S'] = [[0.9f, 0.2f, 0.7f, 0, 0.3f, 0, 0.1f, 0.2f, 0.3f, 0.4f, 0.7f, 0.6f, 0.9f, 0.8f, 0.7f, 1, 0.3f, 1, 0.1f, 0.8f]],
            ['T'] = [[0, 0, 1, 0], [0.5f, 0, 0.5f, 1]],
            ['U'] = [[0, 0, 0, 0.7f, 0.3f, 1, 0.7f, 1, 1, 0.7f, 1, 0]],
            ['V'] = [[0, 0, 0.5f, 1, 1, 0]],
            ['W'] = [[0, 0, 0.2f, 1, 0.5f, 0.5f, 0.8f, 1, 1, 0]],
            ['X'] = [[0, 0, 1, 1], [0, 1, 1, 0]],
            ['Y'] = [[0, 0, 0.5f, 0.5f, 1, 0], [0.5f, 0.5f, 0.5f, 1]],
            ['Z'] = [[0, 0, 1, 0, 0, 1, 1, 1]],

            //// Numbers// Numbers
            ['0'] = [[0.3f, 0, 0.7f, 0, 1, 0.3f, 1, 0.7f, 0.7f, 1, 0.3f, 1, 0, 0.7f, 0, 0.3f, 0.3f, 0], [0, 0.1f, 1, 0.9f]],
            ['1'] = [[0.3f, 0.2f, 0.5f, 0, 0.5f, 1], [0.3f, 1, 0.7f, 1]],
            ['2'] = [[0.1f, 0.2f, 0.3f, 0, 0.7f, 0, 0.9f, 0.2f, 0.9f, 0.4f, 0.1f, 1, 0.9f, 1]],
            ['3'] = [[0.1f, 0.2f, 0.3f, 0, 0.7f, 0, 0.9f, 0.2f, 0.9f, 0.4f, 0.5f, 0.5f, 0.9f, 0.6f, 0.9f, 0.8f, 0.7f, 1, 0.3f, 1, 0.1f, 0.8f]],
            ['4'] = [[0.7f, 0, 0.7f, 1], [0, 0.7f, 0.9f, 0.7f], [0.7f, 0, 0, 0.7f]],
            ['5'] = [[0.9f, 0, 0.1f, 0, 0.1f, 0.5f, 0.7f, 0.5f, 0.9f, 0.7f, 0.9f, 0.9f, 0.7f, 1, 0.3f, 1, 0.1f, 0.9f]],
            ['6'] = [[0.9f, 0.1f, 0.7f, 0, 0.3f, 0, 0.1f, 0.2f, 0.1f, 0.8f, 0.3f, 1, 0.7f, 1, 0.9f, 0.8f, 0.9f, 0.6f, 0.7f, 0.5f, 0.3f, 0.5f, 0.1f, 0.6f]],
            ['7'] = [[0.1f, 0, 0.9f, 0, 0.5f, 1], [0.3f, 0.5f, 0.7f, 0.5f]],
            ['8'] = [[0.3f, 0, 0.7f, 0, 0.9f, 0.2f, 0.9f, 0.3f, 0.7f, 0.5f, 0.3f, 0.5f, 0.1f, 0.3f, 0.1f, 0.2f, 0.3f, 0], [0.3f, 0.5f, 0.1f, 0.7f, 0.1f, 0.8f, 0.3f, 1, 0.7f, 1, 0.9f, 0.8f, 0.9f, 0.7f, 0.7f, 0.5f]],
            ['9'] = [[0.1f, 0.9f, 0.3f, 1, 0.7f, 1, 0.9f, 0.8f, 0.9f, 0.2f, 0.7f, 0, 0.3f, 0, 0.1f, 0.2f, 0.1f, 0.4f, 0.3f, 0.5f, 0.7f, 0.5f, 0.9f, 0.4f]],

            ['.'] = [[0.5f, 1.0f, 0.5f, 0.8f]],
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
        public static void DrawString(Canvas canvas, string text, float x, float y, float height, Color32 color, float lineWidth = 1.0f, float spacing = 0.3f)
        {
            if (string.IsNullOrEmpty(text))
                return;

            float charWidth = height * 0.6f;
            float totalWidth = (charWidth * text.Length) + (spacing * height * (text.Length - 1));
            float currentX = x;

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

        public static float MeasureString(string text, float height, float spacing = 0.3f) => ((height * 0.6f) + (spacing * height))  * (text.Length);

        /// <summary>
        /// Draws a string using vector lines with the text centered at the given position
        /// </summary>
        public static void DrawStringCentered(Canvas canvas, string text, float x, float y, float height, Color32 color, float lineWidth = 1.0f, float spacing = 0.2f)
        {
            if (string.IsNullOrEmpty(text))
                return;

            float charWidth = height * 0.6f;
            float totalWidth = (charWidth * text.Length) + (spacing * height * (text.Length - 1));
            float startX = x - (totalWidth / 2);

            DrawString(canvas, text, startX, y, height, color, lineWidth, spacing);
        }

        /// <summary>
        /// Draws a character using vector lines
        /// </summary>
        private static void DrawCharacter(Canvas canvas, char c, float x, float y, float height)
        {
            // Default to space if character not found
            if (!CharacterDefinitions.TryGetValue(c, out float[][] sections))
                return;

            // Skip if no lines (space)
            if (sections.Length == 0)
                return;

            float width = height * 0.6f;

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
