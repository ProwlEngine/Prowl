using FontStashSharp;
using FontStashSharp.Interfaces;
using Prowl.Vector;
using System.Drawing;

namespace Prowl.Quill
{
    internal class TextRenderer : IFontStashRenderer2, ITexture2DManager
    {
        private readonly Canvas _canvas;

        public TextRenderer(Canvas canvas) => _canvas = canvas;

        /// <summary> Lets the texture manager for font operations. </summary>
        public ITexture2DManager TextureManager => this;

        /// <summary>
        /// Creates a new texture with the specified dimensions.
        /// </summary>
        public object CreateTexture(int width, int height) => _canvas._renderer.CreateTexture((uint)width, (uint)height);

        /// <summary>
        /// Gets the size of a texture.
        /// </summary>
        public Point GetTextureSize(object texture) 
        {
            Vector2Int size = _canvas._renderer.GetTextureSize(texture);
            return new Point(size.x, size.y);
        }

        /// <summary>
        /// Updates texture data in the specified region.
        /// </summary>
        public void SetTextureData(object texture, Rectangle bounds, byte[] data) => _canvas._renderer.SetTextureData(texture, new(bounds.X, bounds.Y, bounds.Width, bounds.Height), data);

        /// <summary>
        /// Draws a quad with the given texture and coordinates.
        /// Called by FontStashSharp when rendering glyphs.
        /// </summary>
        public void DrawQuad(object texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight)
        {
            // Transform vertices through the current transform matrix
            Vector2 pos;

            // Top-left vertex
            pos = _canvas.TransformPoint(new(topLeft.Position.X, topLeft.Position.Y));
            var newTopLeft = new Vertex(new(Math.Round(pos.x), Math.Round(pos.y)), new(topLeft.TextureCoordinate.X, topLeft.TextureCoordinate.Y), ToColor(topLeft.Color));

            // Top-right vertex
            pos = _canvas.TransformPoint(new(topRight.Position.X, topRight.Position.Y));
            var newTopRight = new Vertex(new(Math.Round(pos.x), Math.Round(pos.y)), new(topRight.TextureCoordinate.X, topRight.TextureCoordinate.Y), ToColor(topRight.Color));

            // Bottom-right vertex
            pos = _canvas.TransformPoint(new(bottomRight.Position.X, bottomRight.Position.Y));
            var newBottomRight = new Vertex(new(Math.Round(pos.x), Math.Round(pos.y)), new(bottomRight.TextureCoordinate.X, bottomRight.TextureCoordinate.Y), ToColor(bottomRight.Color));

            // Bottom-left vertex
            pos = _canvas.TransformPoint(new(bottomLeft.Position.X, bottomLeft.Position.Y));
            var newBottomLeft = new Vertex(new(Math.Round(pos.x), Math.Round(pos.y)), new(bottomLeft.TextureCoordinate.X, bottomLeft.TextureCoordinate.Y), ToColor(bottomLeft.Color));

            _canvas.SetTexture(texture);

            // Add vertices to form two triangles (a quad)
            _canvas.AddVertex(newTopLeft);
            _canvas.AddVertex(newBottomRight);
            _canvas.AddVertex(newTopRight);
            _canvas.AddTriangle();

            _canvas.AddVertex(newTopLeft);
            _canvas.AddVertex(newBottomLeft);
            _canvas.AddVertex(newBottomRight);
            _canvas.AddTriangle();

            _canvas.SetTexture(null);
        }

        /// <summary>
        /// Renders text at the specified position with the given parameters.
        /// </summary>
        public void Text(SpriteFontBase font, string text, Vector2 position, Color color, double rotation = 0f, Vector2 origin = default(Vector2), Vector2? scale = null, double layerDepth = 0f, double characterSpacing = 0f, double lineSpacing = 0f, TextStyle textStyle = TextStyle.None)
        {
            if (string.IsNullOrWhiteSpace(text) || font == null)
                return;

            font.DrawText(this, text, position, ToFSColor(color), (float)rotation, origin, scale, (float)layerDepth, (float)characterSpacing, (float)lineSpacing, textStyle);
        }

        /// <summary>
        /// Renders text at the specified position with the given parameters.
        /// </summary>
        public void Text(SpriteFontBase font, string text, Vector2 position, Color[] colors, double rotation = 0f, Vector2 origin = default(Vector2), Vector2? scale = null, double layerDepth = 0f, double characterSpacing = 0f, double lineSpacing = 0f, TextStyle textStyle = TextStyle.None)
        {
            if (string.IsNullOrWhiteSpace(text) || font == null)
                return;

            var fsColors = new FSColor[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                fsColors[i] = ToFSColor(colors[i]);

            font.DrawText(this, text, position, fsColors, (float)rotation, origin, scale, (float)layerDepth, (float)characterSpacing, (float)lineSpacing, textStyle);
        }

        private static FSColor ToFSColor(Color color)
        {
            return new FSColor(color.R, color.G, color.B, color.A);
        }

        private static Color ToColor(FSColor color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}
