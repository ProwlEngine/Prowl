using Prowl.Scribe;
using Prowl.Vector;
using System;

namespace Prowl.Quill
{
    public class FontAtlasSettings 
    {
        public bool AllowExpansion = true;
        public float ExpansionFactor = 2f;
        public int AtlasSize = 1024;
        public int MaxAtlasSize = 4096;
        public bool UseLayoutCache = true;
        public int MaxLayoutCacheSize = 256;
        public int AtlasPadding = 1;
    }

    public class TextRenderer : IFontRenderer
    {
        private readonly Canvas _canvas;

        private FontSystem _fontSystem;

        public FontSystem FontEngine => _fontSystem;

        internal TextRenderer(Canvas canvas, FontAtlasSettings settings)
        {
            _canvas = canvas;

            _fontSystem = new FontSystem(this, settings.AtlasSize, settings.AtlasSize, true);

            _fontSystem.AllowExpansion = settings.AllowExpansion;
            _fontSystem.ExpansionFactor = settings.ExpansionFactor;
            _fontSystem.MaxAtlasSize = settings.MaxAtlasSize;
            _fontSystem.CacheLayouts = settings.UseLayoutCache;
            _fontSystem.MaxLayoutCacheSize = settings.MaxLayoutCacheSize;
            _fontSystem.Padding = settings.AtlasPadding;
        }

        /// <summary>
        /// Creates a new texture with the specified dimensions.
        /// </summary>
        public object CreateTexture(int width, int height) => _canvas._renderer.CreateTexture((uint)width, (uint)height);

        /// <summary>
        /// Updates texture data in the specified region.
        /// </summary>
        public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data)
        {
            // The data is single channel, we need to convert it to RGBA
            byte[] rgbaData = new byte[bounds.Width * bounds.Height * 4];
            for (int i = 0; i < bounds.Width * bounds.Height; i++)
            {
                byte value = data[i];
                rgbaData[i * 4 + 0] = value; // R
                rgbaData[i * 4 + 1] = value; // G
                rgbaData[i * 4 + 2] = value; // B
                rgbaData[i * 4 + 3] = value; // A
            }

            _canvas._renderer.SetTextureData(texture, new IntRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height), rgbaData);
        }

        /// <summary>
        /// Draws a quad with the given texture and coordinates.
        /// Called by Quill when rendering glyphs.
        /// </summary>
        public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
        {
            _canvas.SetFontTexture(texture);

            // UV offset of 2.0 signals text mode to shader (UV >= 2 means text)
            var uvOffset = new Float2(2.0f, 2.0f);

            // Positions from Scribe are in pixel space (scaled). Convert back to logical units
            // before TransformPoint, which will apply the canvas transform and then scale back.
            float invScale = 1.0f / _canvas.Scale;

            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = vertices[indices[i + 0]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];

                // Convert from pixel space to logical units, then transform
                // TransformPoint applies the canvas transform and scales back to pixels
                uint index = (uint)_canvas.Vertices.Count;
                var vertA = new Vertex(_canvas.TransformPoint(new Float2(a.Position.X * invScale, a.Position.Y * invScale)), a.TextureCoordinate + uvOffset, ToColor(a.Color));
                var vertB = new Vertex(_canvas.TransformPoint(new Float2(b.Position.X * invScale, b.Position.Y * invScale)), b.TextureCoordinate + uvOffset, ToColor(b.Color));
                var vertC = new Vertex(_canvas.TransformPoint(new Float2(c.Position.X * invScale, c.Position.Y * invScale)), c.TextureCoordinate + uvOffset, ToColor(c.Color));

                _canvas.AddVertex(vertA);
                _canvas.AddVertex(vertC);
                _canvas.AddVertex(vertB);

                _canvas.AddTriangle(index, index + 1, index + 2);
            }

            _canvas.SetFontTexture(null);
        }

        private static FontColor ToFSColor(Prowl.Vector.Color32 color)
        {
            return new FontColor(color.R, color.G, color.B, color.A);
        }

        private static Color32 ToColor(FontColor color)
        {
            return Color32.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}
