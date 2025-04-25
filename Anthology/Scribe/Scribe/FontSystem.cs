using StbTrueTypeSharp;
using System.Numerics;

namespace Prowl.Scribe
{

    public class FontSystem
    {
        private readonly IFontRenderer renderer;
        private readonly BinPacker binPacker;
        private readonly List<FontInfo> fonts;
        private readonly Dictionary<AtlasGlyph.CacheKey, AtlasGlyph> glyphCache;
        private readonly Dictionary<string, TextLayout> layoutCache;

        private object atlasTexture;
        private int atlasWidth;
        private int atlasHeight;

        private bool useWhiteRect;

        // Settings
        public bool AllowExpansion { get; set; } = true;
        public float ExpansionFactor { get; set; } = 2f;
        public int MaxAtlasSize { get; set; } = 4096;
        public int Padding { get; set; } = 1;
        public bool CacheLayouts { get; set; } = true;
        public int MaxLayoutCacheSize { get; set; } = 100;

        public IEnumerable<FontInfo> Fonts => fonts;
        public int Width => atlasWidth;
        public int Height => atlasHeight;
        public object Texture => atlasTexture;
        public int FontCount => fonts.Count;

        public FontSystem(IFontRenderer renderer, int initialWidth = 512, int initialHeight = 512, bool includeWhiteRect = true)
        {
            this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

            atlasWidth = initialWidth;
            atlasHeight = initialHeight;

            this.useWhiteRect = includeWhiteRect;

            atlasTexture = renderer.CreateTexture(atlasWidth, atlasHeight);
            binPacker = new BinPacker(atlasWidth, atlasHeight);
            fonts = new List<FontInfo>();
            glyphCache = new Dictionary<AtlasGlyph.CacheKey, AtlasGlyph>();
            layoutCache = new Dictionary<string, TextLayout>();

            // Add a small white rectangle for rendering
            if (useWhiteRect)
                AddWhiteRect();
        }

        public void AddWhiteRect()
        {
            if (binPacker.TryPack(4 + Padding * 2, 4 + Padding * 2, out int x, out int y))
            {
                byte[] whiteData = new byte[4 * 4];
                Array.Fill<byte>(whiteData, 255);

                renderer.UpdateTextureRegion(atlasTexture,
                    new AtlasRect(x + Padding, y + Padding, 4, 4), whiteData);
            }
        }

        public FontInfo AddFont(string fontPath)
        {
            var fontInfo = new FontInfo();
            if (fontInfo.InitFont(File.ReadAllBytes(fontPath), 0) == 0)
                throw new InvalidDataException("Failed to initialize font");

            fonts.Add(fontInfo);

            return fontInfo;
        }

        public FontInfo AddFont(byte[] fontData)
        {
            var fontInfo = new FontInfo();
            if (fontInfo.InitFont(fontData, 0) == 0)
                throw new InvalidDataException("Failed to initialize font");

            fonts.Add(fontInfo);

            return fontInfo;
        }

        public void LoadSystemFonts()
        {
            var paths = GetSystemFontPaths();
            foreach (var path in paths)
            {
                try
                {
                    AddFont(path);
                }
                catch
                {
                    // Silently skip problematic fonts
                }
            }
        }

        private IEnumerable<string> GetSystemFontPaths()
        {
            var paths = new List<string>();

            if (OperatingSystem.IsWindows())
            {
                var fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (Directory.Exists(fontDir))
                    paths.AddRange(Directory.GetFiles(fontDir, "*.ttf"));
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var fontDirs = new[]
                {
                    "/usr/share/fonts",
                    "/usr/local/share/fonts",
                    "/System/Library/Fonts",
                    "/Library/Fonts",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")
                };

                foreach (var dir in fontDirs.Where(Directory.Exists))
                {
                    paths.AddRange(Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories));
                    paths.AddRange(Directory.GetFiles(dir, "*.otf", SearchOption.AllDirectories));
                }
            }

            return paths;
        }

        public AtlasGlyph GetOrCreateGlyph(int codepoint, float pixelSize, FontInfo preferredFont = null)
        {
            // Try preferred font first, then fallback to all fonts
            var fontsToTry = preferredFont != null
                ? new[] { preferredFont }.Concat(fonts.Where(f => f != preferredFont))
                : fonts;

            foreach (var font in fontsToTry)
            {
                // Check the cache first before checking if the font even has the glyph, since if it doesnt, it wont be in the cache,
                // and we need to check the cache either way regardless. so doing it first if it IS in the cache lets us skip the HasGlyph check
                var key = new AtlasGlyph.CacheKey(codepoint, pixelSize, font);
                if (glyphCache.TryGetValue(key, out var cachedGlyph))
                {
                    return cachedGlyph;
                }

                if (!HasGlyph(font, codepoint)) continue;

                var glyph = new AtlasGlyph(codepoint, pixelSize, font, this);

                // Try to rasterize and add to atlas
                if (TryAddGlyphToAtlas(glyph))
                {
                    glyphCache[key] = glyph;
                    return glyph;
                }

                // If we can't fit it, expand atlas and try again
                if (AllowExpansion && TryExpandAtlas(glyph))
                {
                    if (TryAddGlyphToAtlas(glyph))
                    {
                        glyphCache[key] = glyph;
                        return glyph;
                    }
                }

                // Store in cache even if not in atlas (for metrics)
                glyphCache[key] = glyph;
                return glyph;
            }

            return null; // Glyph not found in any font
        }

        private bool TryAddGlyphToAtlas(AtlasGlyph glyph)
        {
            var bitmap = RenderGlyph(glyph.Font, glyph.Codepoint, glyph.PixelSize);
            if (bitmap == null) return true; // Empty glyph, nothing to pack

            int packWidth = bitmap.Value.Width + Padding * 2;
            int packHeight = bitmap.Value.Height + Padding * 2;

            if (binPacker.TryPack(packWidth, packHeight, out int x, out int y))
            {
                glyph.AtlasX = x + Padding;
                glyph.AtlasY = y + Padding;
                glyph.AtlasWidth = bitmap.Value.Width;
                glyph.AtlasHeight = bitmap.Value.Height;

                // Calculate texture coordinates
                glyph.U0 = (float)glyph.AtlasX / atlasWidth;
                glyph.V0 = (float)glyph.AtlasY / atlasHeight;
                glyph.U1 = (float)(glyph.AtlasX + glyph.AtlasWidth) / atlasWidth;
                glyph.V1 = (float)(glyph.AtlasY + glyph.AtlasHeight) / atlasHeight;

                // Upload bitmap to atlas
                renderer.UpdateTextureRegion(atlasTexture,
                    new AtlasRect(glyph.AtlasX, glyph.AtlasY, glyph.AtlasWidth, glyph.AtlasHeight),
                    bitmap.Value.Data);

                return true;
            }

            return false;
        }

        private bool TryExpandAtlas(AtlasGlyph glyph)
        {
            var bitmap = RenderGlyph(glyph.Font, glyph.Codepoint, glyph.PixelSize);
            if (bitmap == null) return true;

            int requiredWidth = bitmap.Value.Width + Padding * 2;
            int requiredHeight = bitmap.Value.Height + Padding * 2;

            int newWidth = Math.Max(atlasWidth, (int)(atlasWidth * ExpansionFactor));
            int newHeight = Math.Max(atlasHeight, (int)(atlasHeight * ExpansionFactor));

            // Ensure we can fit the glyph
            newWidth = Math.Max(newWidth, atlasWidth + requiredWidth);
            newHeight = Math.Max(newHeight, atlasHeight + requiredHeight);

            // Respect max size
            if (newWidth > MaxAtlasSize || newHeight > MaxAtlasSize)
                return false;

            // Create new atlas
            atlasWidth = newWidth;
            atlasHeight = newHeight;
            atlasTexture = renderer.CreateTexture(atlasWidth, atlasHeight);

            // Clear bin packer and glyph cache
            binPacker.Clear(atlasWidth, atlasHeight);
            glyphCache.Clear();

            // Clear the Layout Cache
            ClearLayoutCache();

            // Re-add white rect
            if (useWhiteRect)
                AddWhiteRect();

            return true;
        }

        #region Metrics and Getters

        public GlyphMetrics? GetGlyphMetrics(FontInfo fontInfo, int codepoint, float pixelSize)
        {
            int glyphIndex = fontInfo.FindGlyphIndex(codepoint);
            if (glyphIndex == 0) return null;

            float scale = fontInfo.ScaleForPixelHeight(pixelSize);

            // Get advance and bearing
            int advance = 0, leftSideBearing = 0;
            fontInfo.GetGlyphHorizontalMetrics(glyphIndex, ref advance, ref leftSideBearing);

            // Get bounding box
            int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            fontInfo.GetGlyphBitmapBoundingBox(glyphIndex, scale, scale, ref x0, ref y0, ref x1, ref y1);

            return new GlyphMetrics {
                AdvanceWidth = advance * scale,
                LeftSideBearing = leftSideBearing * scale,
                Width = x1 - x0,
                Height = y1 - y0,
                OffsetX = x0,
                OffsetY = y0
            };
        }

        public void GetScaledVMetrics(FontInfo font, float pixelSize, out float ascent, out float descent, out float lineGap)
        {
            font.GetFontVMetrics(out int a, out int d, out int g);
            float s = font.ScaleForPixelHeight(pixelSize);
            ascent = a * s;
            descent = d * s; // stb returns negative descent; caller may convert to positive if desired
            lineGap = g * s;
        }

        public GlyphBitmap? RenderGlyph(FontInfo fontInfo, int codepoint, float pixelSize)
        {
            int glyphIndex = fontInfo.FindGlyphIndex(codepoint);
            if (glyphIndex == 0) return null;

            float scale = fontInfo.ScaleForPixelHeight(pixelSize);

            int width = 0, height = 0, xoff = 0, yoff = 0;
            var bitmap = fontInfo.GetGlyphBitmap(scale, scale, glyphIndex, ref width, ref height, ref xoff, ref yoff);

            if (bitmap.IsNull || width == 0 || height == 0)
                return null;

            // Convert to byte array
            byte[] data = new byte[width * height];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = bitmap[i];
            }

            return new GlyphBitmap {
                Width = width,
                Height = height,
                OffsetX = xoff,
                OffsetY = yoff,
                Data = data
            };
        }

        public float GetKerning(FontInfo fontInfo, int leftCodepoint, int rightCodepoint, float pixelSize)
        {
            int leftGlyph = fontInfo.FindGlyphIndex(leftCodepoint);
            int rightGlyph = fontInfo.FindGlyphIndex(rightCodepoint);

            if (leftGlyph == 0 || rightGlyph == 0) return 0;

            float scale = fontInfo.ScaleForPixelHeight(pixelSize);
            int kernAdvance = fontInfo.GetGlyphKerningAdvance(leftGlyph, rightGlyph);

            return kernAdvance * scale;
        }

        public bool HasGlyph(FontInfo fontInfo, int codepoint)
        {
            return fontInfo.FindGlyphIndex(codepoint) != 0;
        }

        #endregion

        #region Layout Methods

        public TextLayout CreateLayout(string text, TextLayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text))
            {
                var emptyLayout = new TextLayout();
                emptyLayout.UpdateLayout(text, settings, this);
                return emptyLayout;
            }

            string cacheKey = CacheLayouts ? GenerateLayoutCacheKey(text, settings) : null;

            if (cacheKey != null && layoutCache.TryGetValue(cacheKey, out var cachedLayout))
            {
                return cachedLayout;
            }

            var layout = new TextLayout();
            layout.UpdateLayout(text, settings, this);

            if (cacheKey != null)
            {
                // Manage cache size
                if (layoutCache.Count >= MaxLayoutCacheSize)
                {
                    var firstKey = layoutCache.Keys.First();
                    layoutCache.Remove(firstKey);
                }
                layoutCache[cacheKey] = layout;
            }

            return layout;
        }

        private string GenerateLayoutCacheKey(string text, TextLayoutSettings settings)
        {
            return $"{text}|{settings.PixelSize}|{settings.LetterSpacing}|{settings.WordSpacing}|" +
                   $"{settings.LineHeight}|{settings.TabSize}|{settings.WrapMode}|{settings.Alignment}|" +
                   $"{settings.MaxWidth}|{settings.PreferredFont?.GetHashCode() ?? 0}";
        }

        public void ClearLayoutCache()
        {
            layoutCache.Clear();
        }

        #endregion

        #region Updated API Methods

        public Vector2 MeasureText(string text, float pixelSize, FontInfo preferredFont = null, float letterSpacing = 0)
        {
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = pixelSize;
            settings.PreferredFont = preferredFont;
            settings.LetterSpacing = letterSpacing;

            var layout = CreateLayout(text, settings);
            return layout.Size;
        }

        public Vector2 MeasureText(string text, TextLayoutSettings settings)
        {
            var layout = CreateLayout(text, settings);
            return layout.Size;
        }

        public void DrawText(string text, Vector2 position, FontColor color, float pixelSize,
            FontInfo preferredFont = null, float letterSpacing = 0)
        {
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = pixelSize;
            settings.PreferredFont = preferredFont;
            settings.LetterSpacing = letterSpacing;

            DrawText(text, position, color, settings);
        }

        public void DrawText(string text, Vector2 position, FontColor color, TextLayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text)) return;

            var layout = CreateLayout(text, settings);
            DrawLayout(layout, position, color);
        }

        public void DrawLayout(TextLayout layout, Vector2 position, FontColor color)
        {
            if (layout.Lines.Count == 0) return;

            var vertices = new List<IFontRenderer.Vertex>();
            var indices = new List<int>();
            int vertexCount = 0;

            foreach (var line in layout.Lines)
            {
                foreach (var glyphInstance in line.Glyphs)
                {
                    var glyph = glyphInstance.Glyph;

                    // Only render if glyph is in atlas
                    if (!glyph.IsInAtlas || glyph.AtlasWidth <= 0 || glyph.AtlasHeight <= 0)
                        continue;

                    float glyphX = position.X + line.Position.X + glyphInstance.Position.X;
                    float glyphY = position.Y + line.Position.Y + glyphInstance.Position.Y;
                    float glyphW = glyph.Metrics.Width;
                    float glyphH = glyph.Metrics.Height;

                    // Create quad vertices
                    vertices.Add(new IFontRenderer.Vertex(new Vector3(glyphX, glyphY, 0), color, new Vector2(glyph.U0, glyph.V0)));
                    vertices.Add(new IFontRenderer.Vertex(new Vector3(glyphX + glyphW, glyphY, 0), color, new Vector2(glyph.U1, glyph.V0)));
                    vertices.Add(new IFontRenderer.Vertex(new Vector3(glyphX, glyphY + glyphH, 0), color, new Vector2(glyph.U0, glyph.V1)));
                    vertices.Add(new IFontRenderer.Vertex(new Vector3(glyphX + glyphW, glyphY + glyphH, 0), color, new Vector2(glyph.U1, glyph.V1)));

                    // Create quad indices
                    indices.AddRange(new[] { vertexCount, vertexCount + 1, vertexCount + 2, vertexCount + 1, vertexCount + 3, vertexCount + 2 });
                    vertexCount += 4;
                }
            }

            if (vertices.Count > 0)
            {
                renderer.DrawQuads(atlasTexture, vertices.ToArray(), indices.ToArray());
            }
        }

        #endregion
    }
}
