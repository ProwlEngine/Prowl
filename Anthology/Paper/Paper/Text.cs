using System.Drawing;

using FontStashSharp;

using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a text element with various positioning and styling options.
    /// </summary>
    public struct Text
    {
        #region Properties
        public string Value { get; set; }
        public Color Color { get; set; }
        public SpriteFontBase? Font { get; set; }
        public double AlignX { get; set; }
        public double AlignY { get; set; }
        public double XOffset { get; set; }
        public double YOffset { get; set; }
        public double LayerDepth { get; set; }
        public double CharacterSpacing { get; set; }
        public double LineSpacing { get; set; }
        #endregion

        public static readonly Text Empty = new Text
        {
            Value = string.Empty,
            Color = Color.White,
            Font = null,
            AlignX = 0,
            AlignY = 0,
            XOffset = 0,
            YOffset = 0,
            LayerDepth = 0,
            CharacterSpacing = 0,
            LineSpacing = 0
        };

        #region Factory Methods
        public static Text Create(
            string value,
            SpriteFontBase font,
            Color? color = null,
            double alignX = 0,
            double alignY = 0,
            double xOffset = 0,
            double yOffset = 0,
            double layerDepth = 0,
            double characterSpacing = 0,
            double lineSpacing = 0)
        {
            return new Text
            {
                Value = value,
                Font = font,
                Color = color ?? Color.White,
                AlignX = alignX,
                AlignY = alignY,
                XOffset = xOffset,
                YOffset = yOffset,
                LayerDepth = layerDepth,
                CharacterSpacing = characterSpacing,
                LineSpacing = lineSpacing
            };
        }
        public static Text Create(
            string value,
            SpriteFontBase font,
            double alignX = 0,
            double alignY = 0,
            double xOffset = 0,
            double yOffset = 0,
            double layerDepth = 0,
            double characterSpacing = 0,
            double lineSpacing = 0) => Create(value, font, Color.White, alignX, alignY, xOffset, yOffset, layerDepth, characterSpacing, lineSpacing);

        public static Text Left(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.0, 0.5);
        public static Text Center(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.5, 0.5);
        public static Text Right(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 1.0, 0.5);

        public static Text TopLeft(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.0, 0.0);
        public static Text TopCenter(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.5, 0.0);
        public static Text TopRight(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 1.0, 0.0);
        public static Text MiddleLeft(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.0, 0.5);
        public static Text MiddleCenter(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.5, 0.5);
        public static Text MiddleRight(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 1.0, 0.5);
        public static Text BottomLeft(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.0, 1.0);
        public static Text BottomCenter(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 0.5, 1.0);
        public static Text BottomRight(string value, SpriteFontBase font, Color? color = null) =>
            Create(value, font, color, 1.0, 1.0);
        #endregion

        #region Rendering
        public void Draw(Canvas context, Rect rect)
        {
            if (string.IsNullOrEmpty(Value) || Font == null)
                return;

            var textSize = Font.MeasureString(Value);
            double textX = rect.x + (rect.width - textSize.X) * AlignX;
            double textY = rect.y + (rect.height - Font.LineHeight) * AlignY;

            int xPos = (int)(textX + XOffset);
            int yPos = (int)(textY + YOffset);
            context.DrawText(Font, Value, xPos, yPos, Color, 0,
                layerDepth: LayerDepth,
                characterSpacing: CharacterSpacing,
                lineSpacing: LineSpacing);
        }
        #endregion

        #region Interpolation
        public static Text Lerp(Text a, Text b, double t)
        {
            return new Text
            {
                Value = t > 0.5 ? b.Value : a.Value,
                Font = t > 0.5 ? b.Font : a.Font,
                Color = LerpColor(a.Color, b.Color, t),
                AlignX = a.AlignX + (b.AlignX - a.AlignX) * t,
                AlignY = a.AlignY + (b.AlignY - a.AlignY) * t,
                XOffset = a.XOffset + (b.XOffset - a.XOffset) * t,
                YOffset = a.YOffset + (b.YOffset - a.YOffset) * t,
                LayerDepth = a.LayerDepth + (b.LayerDepth - a.LayerDepth) * t,
                CharacterSpacing = a.CharacterSpacing + (b.CharacterSpacing - a.CharacterSpacing) * t,
                LineSpacing = a.LineSpacing + (b.LineSpacing - a.LineSpacing) * t
            };
        }

        private static Color LerpColor(Color start, Color end, double t)
        {
            int r = (int)(start.R + (end.R - start.R) * t);
            int g = (int)(start.G + (end.G - start.G) * t);
            int b = (int)(start.B + (end.B - start.B) * t);
            int a = (int)(start.A + (end.A - start.A) * t);
            return Color.FromArgb(a, r, g, b);
        }
        #endregion
    }
}
