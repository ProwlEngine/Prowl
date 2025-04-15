using FontStashSharp;
using Prowl.PaperUI.Graphics;
using System.Drawing;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a text element with various positioning and styling options.
    /// </summary>
    public struct Text
    {
        #region Properties

        /// <summary>The text content to display.</summary>
        public string Value { get; set; }

        /// <summary>The color of the text.</summary>
        public Color Color { get; set; }

        /// <summary>The font used to render the text.</summary>
        public SpriteFontBase Font { get; set; }

        /// <summary>Horizontal offset from the aligned position.</summary>
        public float XOffset { get; set; }

        /// <summary>Vertical offset from the aligned position.</summary>
        public float YOffset { get; set; }

        /// <summary>Horizontal alignment of the text within its container.</summary>
        public TextHorizontalAlignment HorizontalAlignment { get; set; }

        /// <summary>Vertical alignment of the text within its container.</summary>
        public TextVerticalAlignment VerticalAlignment { get; set; }

        /// <summary>The depth layer for rendering order.</summary>
        public float LayerDepth { get; set; }

        /// <summary>Additional spacing between characters.</summary>
        public float CharacterSpacing { get; set; }

        /// <summary>Additional spacing between lines.</summary>
        public float LineSpacing { get; set; }

        /// <summary>Effect applied to the text (e.g., shadow, outline).</summary>
        public FontStashSharp.FontSystemEffect Effect { get; set; } = FontStashSharp.FontSystemEffect.None;

        /// <summary>Amount of effect applied to the text.</summary>
        public int EffectAmount { get; set; } = 0;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the Text struct.
        /// </summary>
        private Text(
            string value,
            SpriteFontBase font,
            Color color,
            TextHorizontalAlignment horizontalAlignment,
            TextVerticalAlignment verticalAlignment,
            float xOffset = 0,
            float yOffset = 0,
            float layerDepth = 0,
            float characterSpacing = 0,
            float lineSpacing = 0,
            FontSystemEffect effect = FontSystemEffect.None, 
            int effectAmount = 0)
        {
            Value = value;
            Font = font ?? throw new ArgumentNullException(nameof(font));
            Color = color;
            HorizontalAlignment = horizontalAlignment;
            VerticalAlignment = verticalAlignment;
            XOffset = xOffset;
            YOffset = yOffset;
            LayerDepth = layerDepth;
            CharacterSpacing = characterSpacing;
            LineSpacing = lineSpacing;
            Effect = effect;
            EffectAmount = effectAmount;
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Draws the text within the specified rectangle.
        /// </summary>
        /// <param name="context">The canvas to draw on.</param>
        /// <param name="rect">The rectangle that contains the text.</param>
        public void Draw(Canvas context, Rect rect)
        {
            if (string.IsNullOrEmpty(Value))
            {
                return;
            }

            var textSize = Font.MeasureString(Value);
            float textX = rect.X;
            float textY = rect.Y;

            // Horizontal alignment relative to the rectangle
            switch (HorizontalAlignment)
            {
                case TextHorizontalAlignment.Center:
                    // Center text horizontally within the rectangle
                    textX = rect.X + (rect.Width - textSize.X) / 2.0f;
                    break;
                case TextHorizontalAlignment.Right:
                    // Align text to the right edge of the rectangle
                    textX = rect.X + rect.Width - textSize.X;
                    break;
            }

            // Vertical alignment relative to the rectangle
            switch (VerticalAlignment)
            {
                case TextVerticalAlignment.Center:
                    // Center text vertically within the rectangle
                    textY = rect.Y + (rect.Height - Font.LineHeight) / 2.0f;
                    break;
                case TextVerticalAlignment.Bottom:
                    // Align text to the bottom edge of the rectangle
                    textY = rect.Y + rect.Height - Font.LineHeight;
                    break;
            }

            // Apply any additional offset and draw the text
            context.FillColor(Color);
            context.Text(Font, Value, textX + XOffset, textY + YOffset, LayerDepth, CharacterSpacing, LineSpacing, effect: Effect, effectAmount: EffectAmount);
        }

        #endregion

        #region Basic Factory Methods

        /// <summary>
        /// Creates a basic text element with default left-top alignment.
        /// </summary>
        public static Text Create(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Left, TextVerticalAlignment.Top, 0, 0, 0, 0, 0, effect, effectAmount);

        #endregion

        #region Alignment Factory Methods

        /// <summary>Creates left-aligned text (vertical center).</summary>
        public static Text Left(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Left, TextVerticalAlignment.Center, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates center-aligned text (horizontal and vertical center).</summary>
        public static Text Center(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Center, TextVerticalAlignment.Center, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates right-aligned text (vertical center).</summary>
        public static Text Right(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Right, TextVerticalAlignment.Center, 0, 0, 0, 0, 0, effect, effectAmount);

        #endregion

        #region Position Factory Methods

        /// <summary>Creates text aligned to the top-left corner.</summary>
        public static Text TopLeft(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Left, TextVerticalAlignment.Top, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the top-center position.</summary>
        public static Text TopCenter(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Center, TextVerticalAlignment.Top, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the top-right corner.</summary>
        public static Text TopRight(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Right, TextVerticalAlignment.Top, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the middle-left position.</summary>
        public static Text MiddleLeft(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Left, TextVerticalAlignment.Center, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the middle-center position.</summary>
        public static Text MiddleCenter(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Center, TextVerticalAlignment.Center, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the middle-right position.</summary>
        public static Text MiddleRight(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Right, TextVerticalAlignment.Center, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the bottom-left corner.</summary>
        public static Text BottomLeft(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Left, TextVerticalAlignment.Bottom, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the bottom-center position.</summary>
        public static Text BottomCenter(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Center, TextVerticalAlignment.Bottom, 0, 0, 0, 0, 0, effect, effectAmount);

        /// <summary>Creates text aligned to the bottom-right corner.</summary>
        public static Text BottomRight(string value, SpriteFontBase font, Color? color = null, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, TextHorizontalAlignment.Right, TextVerticalAlignment.Bottom, 0, 0, 0, 0, 0, effect, effectAmount);

        #endregion

        #region Custom Factory Method

        /// <summary>
        /// Creates a fully customized text element with all properties specified.
        /// </summary>
        public static Text Custom(
            string value,
            SpriteFontBase font,
            Color? color = null,
            TextHorizontalAlignment horizontalAlignment = TextHorizontalAlignment.Left,
            TextVerticalAlignment verticalAlignment = TextVerticalAlignment.Top,
            float xOffset = 0,
            float yOffset = 0,
            float layerDepth = 0,
            float characterSpacing = 0,
            float lineSpacing = 0,
            FontSystemEffect effect = FontSystemEffect.None,
            int effectAmount = 0) =>
            new Text(value, font, color ?? Color.White, horizontalAlignment, verticalAlignment,
                    xOffset, yOffset, layerDepth, characterSpacing, lineSpacing, effect, effectAmount);

        #endregion
    }
}