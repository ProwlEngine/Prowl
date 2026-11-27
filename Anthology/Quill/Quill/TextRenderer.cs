using System;

using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Quill;

/// <summary>
/// Configuration settings for the font atlas used in text rendering.
/// </summary>
public class FontAtlasSettings
{
    /// <summary>
    /// Whether to allow the atlas to expand when it runs out of space. Default is true.
    /// </summary>
    public bool AllowExpansion = true;

    /// <summary>
    /// The factor by which to expand the atlas when more space is needed. Default is 2.
    /// </summary>
    public float ExpansionFactor = 2f;

    /// <summary>
    /// The initial size of the font atlas in pixels. Default is 1024.
    /// </summary>
    public int AtlasSize = 1024;

    /// <summary>
    /// The maximum size the atlas can expand to. Default is 4096.
    /// </summary>
    public int MaxAtlasSize = 4096;

    /// <summary>
    /// Whether to cache text layouts for improved performance. Default is true.
    /// </summary>
    public bool UseLayoutCache = true;

    /// <summary>
    /// The maximum number of layouts to cache. Default is 256.
    /// </summary>
    public int MaxLayoutCacheSize = 256;

    /// <summary>
    /// The padding between glyphs in the atlas. Default is 1.
    /// </summary>
    public int AtlasPadding = 1;
}

/// <summary>
/// Handles text rendering by integrating with the Scribe font system.
/// </summary>
public class TextRenderer : IFontRenderer
{
    private readonly Canvas _canvas;

    private FontSystem _fontSystem;

    /// <summary>
    /// Gets the underlying font system for advanced text operations.
    /// </summary>
    public FontSystem FontEngine => _fontSystem;

    internal TextRenderer(Canvas canvas, FontAtlasSettings settings)
    {
        _canvas = canvas;

        _fontSystem = new FontSystem(this, settings.AtlasSize, settings.AtlasSize, true)
        {
            AllowExpansion = settings.AllowExpansion,
            ExpansionFactor = settings.ExpansionFactor,
            MaxAtlasSize = settings.MaxAtlasSize,
            CacheLayouts = settings.UseLayoutCache,
            MaxLayoutCacheSize = settings.MaxLayoutCacheSize,
            Padding = settings.AtlasPadding
        };
    }

    /// <summary>
    /// Creates a new texture with the specified dimensions.
    /// </summary>
    public object CreateTexture(int width, int height) => _canvas._renderer.CreateTexture((uint)width, (uint)height);

    /// <summary>
    /// Updates texture data in the specified region. Scribe now produces multi-channel signed
    /// distance fields, so the data is already RGBA (4 bytes per pixel) and is uploaded directly.
    /// </summary>
    public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data)
    {
        _canvas._renderer.SetTextureData(texture, new IntRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height), data);
    }

    /// <summary>
    /// Draws a quad with the given texture and coordinates.
    /// Called by Quill when rendering glyphs.
    /// </summary>
    public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
    {
        // Bind the font atlas as dedicated canvas state (a separate sampler unit) rather than the
        // brush texture, so this text batches into the same draw call as surrounding shapes.
        _canvas.SetFontAtlas(texture);
        _canvas._textDraws++;

        Transform2D transform = _canvas.GetTransform();
        float fbScale = _canvas.FramebufferScale;

        // Scribe hands us an indexed mesh (4 unique vertices + 6 indices per glyph). Each vertex
        // is transformed once and written straight into the canvas buffers.
        GeometryBuffer<Vertex> vbuf = _canvas._vertices;
        vbuf.Reserve(vertices.Length);
        Vertex[] dst = vbuf.Array;
        int baseVertex = vbuf.Count;
        uint baseIndex = (uint)baseVertex;

        // A run is nearly always one colour, so it is converted and premultiplied once per change.
        float globalAlpha = _canvas.GlobalAlpha;
        FontColor lastColor = default;
        byte r = 0, g = 0, b = 0, a = 0;
        bool haveColor = false;

        bool translationOnly = transform.IsIdentityOrTranslation;
        // Scribe positions are already in pixel space. With no rotation, scale or skew the
        // divide to logical and multiply back by framebuffer scale cancel, leaving an offset.
        float ox = transform.E * fbScale;
        float oy = transform.F * fbScale;
        float invScale = 1.0f / fbScale;

        for (int i = 0; i < vertices.Length; i++)
        {
            ref readonly IFontRenderer.Vertex v = ref vertices[i];
            FontColor c = v.Color;
            if (!haveColor || c.R != lastColor.R || c.G != lastColor.G || c.B != lastColor.B || c.A != lastColor.A)
            {
                lastColor = c;
                haveColor = true;
                Premultiply(c, globalAlpha, out r, out g, out b, out a);
            }

            ref Vertex d = ref dst[baseVertex + i];
            if (translationOnly)
            {
                d.x = v.Position.X + ox;
                d.y = v.Position.Y + oy;
            }
            else
            {
                Float2 p = _canvas.TransformPoint(new Float2(v.Position.X * invScale, v.Position.Y * invScale));
                d.x = p.X;
                d.y = p.Y;
            }

            // UV offset of 2.0 signals text mode to shader (UV >= 2 means text)
            d.u = v.TextureCoordinate.X + 2.0f;
            d.v = v.TextureCoordinate.Y + 2.0f;
            d.r = r;
            d.g = g;
            d.b = b;
            d.a = a;
        }
        vbuf.Count = baseVertex + vertices.Length;

        // Each source triangle (i0,i1,i2) is emitted as (i0,i2,i1), the winding the canvas expects.
        int indexCount = indices.Length / 3 * 3;
        GeometryBuffer<uint> ibuf = _canvas._indices;
        ibuf.Reserve(indexCount);
        uint[] idst = ibuf.Array;
        int io = ibuf.Count;
        for (int i = 0; i < indexCount; i += 3)
        {
            idst[io + i] = baseIndex + (uint)indices[i];
            idst[io + i + 1] = baseIndex + (uint)indices[i + 2];
            idst[io + i + 2] = baseIndex + (uint)indices[i + 1];
        }
        ibuf.Count = io + indexCount;

        if (indexCount > 0)
            _canvas.AddTriangleCount(indexCount / 3);

        // The atlas stays bound as canvas state (persistent, part of the batch hash) so shapes
        // drawn after this text keep batching with it.
    }

    // Global alpha and premultiplied alpha, the same as Canvas.AddVertices applies per vertex.
    private static void Premultiply(FontColor c, float globalAlpha, out byte r, out byte g, out byte b, out byte a)
    {
        r = c.R; g = c.G; b = c.B; a = c.A;
        if (globalAlpha != 1.0f)
            a = (byte)(a * globalAlpha);

        if (a != 255)
        {
            float alpha = a / 255f;
            r = (byte)(r * alpha);
            g = (byte)(g * alpha);
            b = (byte)(b * alpha);
        }
    }

    private static FontColor ToFSColor(Prowl.Vector.Color32 color)
    {
        return new FontColor(color.R, color.G, color.B, color.A);
    }
}
