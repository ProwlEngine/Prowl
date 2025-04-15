using FontStashSharp;
using FontStashSharp.Interfaces;
using System.Drawing;
using System.Numerics;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Handles text rendering for PaperUI using FontStashSharp.
    /// Implements the FontStashSharp renderer interfaces to draw text on the canvas.
    /// </summary>
    /// <remarks>
    /// Creates a new text renderer for the specified canvas.
    /// </remarks>
    /// <param name="context">Canvas to render text on</param>
    internal class TextRenderer(Canvas context) : IFontStashRenderer2, ITexture2DManager
    {
        private readonly Canvas _context = context;
        internal int _lastVertexOffset;
        internal object? _lastTextTexture = null;

        /// <summary>
        /// Gets the texture manager for font operations.
        /// </summary>
        public ITexture2DManager TextureManager => this;

        /// <summary>
        /// Creates a new texture with the specified dimensions.
        /// </summary>
        public object CreateTexture(int width, int height) => _context._renderer.CreateTexture(width, height);

        /// <summary>
        /// Gets the size of a texture.
        /// </summary>
        public Point GetTextureSize(object texture) => _context._renderer.GetTextureSize(texture);

        /// <summary>
        /// Updates texture data in the specified region.
        /// </summary>
        public void SetTextureData(object texture, Rectangle bounds, byte[] data) => _context._renderer.SetTextureData(texture, bounds, data);

        /// <summary>
        /// Draws a quad with the given texture and coordinates.
        /// Called by FontStashSharp when rendering glyphs.
        /// </summary>
        public void DrawQuad(object texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight)
        {
            // Flush if we're changing texture
            if (_lastTextTexture != null && _lastTextTexture != texture)
            {
                FlushText();
            }

            var state = _context._currentState;

            // Transform vertices through the current transform matrix
            // Top-left vertex
            state.Transform.TransformPoint(out float px, out float py, topLeft.Position.X, topLeft.Position.Y);
            px = (int)px;
            py = (int)py;
            var newTopLeft = new Vertex(px, py, topLeft.TextureCoordinate.X, topLeft.TextureCoordinate.Y);

            // Top-right vertex
            state.Transform.TransformPoint(out px, out py, topRight.Position.X, topRight.Position.Y);
            px = (int)px;
            py = (int)py;
            var newTopRight = new Vertex(px, py, topRight.TextureCoordinate.X, topRight.TextureCoordinate.Y);

            // Bottom-right vertex
            state.Transform.TransformPoint(out px, out py, bottomRight.Position.X, bottomRight.Position.Y);
            px = (int)px;
            py = (int)py;
            var newBottomRight = new Vertex(px, py, bottomRight.TextureCoordinate.X, bottomRight.TextureCoordinate.Y);

            // Bottom-left vertex
            state.Transform.TransformPoint(out px, out py, bottomLeft.Position.X, bottomLeft.Position.Y);
            px = (int)px;
            py = (int)py;
            var newBottomLeft = new Vertex(px, py, bottomLeft.TextureCoordinate.X, bottomLeft.TextureCoordinate.Y);

            // Add vertices to form two triangles (a quad)
            var renderCache = _context._renderCache;
            renderCache.AddVertex(newTopLeft);     // Triangle 1 - First vertex
            renderCache.AddVertex(newBottomRight); // Triangle 1 - Second vertex
            renderCache.AddVertex(newTopRight);    // Triangle 1 - Third vertex

            renderCache.AddVertex(newTopLeft);     // Triangle 2 - First vertex
            renderCache.AddVertex(newBottomLeft);  // Triangle 2 - Second vertex
            renderCache.AddVertex(newBottomRight); // Triangle 2 - Third vertex

            _lastTextTexture = texture;
        }

        /// <summary>
        /// Flushes pending text drawing operations to the renderer.
        /// </summary>
        private void FlushText()
        {
            var renderCache = _context._renderCache;

            // Nothing to render
            if (_lastTextTexture == null || _lastVertexOffset == renderCache.VertexCount)
            {
                return;
            }

            var state = _context._currentState;
            var paint = state.Fill;

            // Set up paint with the text texture
            paint.Image = _lastTextTexture;

            // Apply current alpha to paint colors
            Canvas.MultiplyAlpha(ref paint.InnerColor, state.Alpha);
            Canvas.MultiplyAlpha(ref paint.OuterColor, state.Alpha);

            // Create a render call for the text triangles
            renderCache.RenderTriangles(
                ref paint,
                ref state.Scissor,
                _context._fringeWidth,
                _lastVertexOffset,
                renderCache.VertexCount - _lastVertexOffset
            );

            // Reset for next batch
            _lastVertexOffset = renderCache.VertexCount;
            _lastTextTexture = null;
        }

        /// <summary>
        /// Renders text at the specified position with the given parameters.
        /// </summary>
        public void Text(SpriteFontBase font, string text, float x, float y, float layerDepth, float characterSpacing, float lineSpacing, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0)
        {
            if (string.IsNullOrWhiteSpace(text) || font == null)
                return;

            _lastVertexOffset = _context._renderCache.VertexCount;

            font.DrawText(this, text, new Vector2(x, y), FSColor.White, layerDepth: layerDepth, characterSpacing: characterSpacing, lineSpacing: lineSpacing, effect: effect, effectAmount: effectAmount);

            // Ensure all text is rendered
            FlushText();
        }
    }
}