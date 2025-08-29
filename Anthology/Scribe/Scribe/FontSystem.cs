using Prowl.Scribe.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Prowl.Scribe
{
    public class FontSystem
    {
        private readonly IFontRenderer renderer;
        private readonly BinPacker binPacker;
        private readonly List<FontInfo> fonts;
        private readonly Dictionary<AtlasGlyph.CacheKey, AtlasGlyph> glyphCache;

        readonly LruCache<LayoutCacheKey, TextLayout> layoutCache;
        readonly Dictionary<FontInfo, int> fontIds = new Dictionary<FontInfo, int>();

        private readonly Dictionary<(FontInfo, int, int), float> kerningMapCache;

        private readonly Dictionary<(string, FontStyle), FontInfo> fontLookup;

        private object atlasTexture;
        private int atlasWidth;
        private int atlasHeight;

        private bool useWhiteRect;
        private float whiteU0, whiteV0, whiteU1, whiteV1;

        // Settings
        public bool AllowExpansion { get; set; } = true;
        public float ExpansionFactor { get; set; } = 2f;
        public int MaxAtlasSize { get; set; } = 4096;
        public int Padding { get; set; } = 1;
        int _maxLayout = 256;
        public int MaxLayoutCacheSize {
            get => _maxLayout;
            set { _maxLayout = Math.Max(1, value); layoutCache.Capacity = _maxLayout; }
        }
        public bool CacheLayouts { get; set; } = false;

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
            layoutCache = new LruCache<LayoutCacheKey, TextLayout>(_maxLayout);
            kerningMapCache = new Dictionary<(FontInfo, int, int), float>();

            fontLookup = new Dictionary<(string, FontStyle), FontInfo>();

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
                    new AtlasRect(x, y, 4, 4), whiteData);

                whiteU0 = (float)x / atlasWidth;
                whiteV0 = (float)y / atlasHeight;
                whiteU1 = (float)(x + 1) / atlasWidth;
                whiteV1 = (float)(y + 1) / atlasHeight;
            }
        }

        public FontInfo AddFont(string fontPath)
        {
            var fontInfo = new FontInfo();
            if (fontInfo.InitFont(File.ReadAllBytes(fontPath), 0) == 0)
                throw new InvalidDataException("Failed to initialize font");

            RegisterFont(fontInfo);

            return fontInfo;
        }

        public FontInfo AddFont(byte[] fontData)
        {
            var fontInfo = new FontInfo();
            if (fontInfo.InitFont(fontData, 0) == 0)
                throw new InvalidDataException("Failed to initialize font");

            RegisterFont(fontInfo);
            return fontInfo;
        }

        public FontInfo GetFont(string familyName, FontStyle style = FontStyle.Regular)
        {
            if (string.IsNullOrEmpty(familyName))
                return null;
            var key = (familyName.ToLowerInvariant(), style);
            fontLookup.TryGetValue(key, out var font);
            return font;
        }

        void RegisterFont(FontInfo fontInfo)
        {
            ExtractFontMetadata(fontInfo, out var family, out var style);

            fontInfo.FamilyName = family;
            fontInfo.Style = style;

            fonts.Add(fontInfo);
            fontIds[fontInfo] = fonts.Count - 1;

            var key = (family.ToLowerInvariant(), style);
            fontLookup[key] = fontInfo;

            glyphCache.Clear();
        }

        static void ExtractFontMetadata(FontInfo fontInfo, out string family, out FontStyle style)
        {
            string fam = GetNameString(fontInfo, 1);
            string sub = GetNameString(fontInfo, 2);

            family = fam;
            style = ParseStyle(sub);
        }

        static string GetNameString(FontInfo font, int nameId)
        {
            int len = 0;
            var ptr = font.GetFontNameString(font, ref len, 3, 1, 0x409, nameId);
            if (ptr.IsNull || len == 0)
                return string.Empty;
            var buffer = new byte[len];
            for (int i = 0; i < len; i++) buffer[i] = ptr[i];
            return Encoding.BigEndianUnicode.GetString(buffer);
        }

        static FontStyle ParseStyle(string styleName)
        {
            var s = styleName?.ToLowerInvariant() ?? string.Empty;
            bool bold = s.Contains("bold");
            bool italic = s.Contains("italic") || s.Contains("oblique");
            if (bold && italic) return FontStyle.BoldItalic;
            if (bold) return FontStyle.Bold;
            if (italic) return FontStyle.Italic;
            return FontStyle.Regular;
        }

        public void LoadSystemFonts(params string[] priorityFamilies)
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

            ApplyFontPriorities(priorityFamilies);
        }

        void ApplyFontPriorities(string[] priorityFamilies)
        {
            if (priorityFamilies == null || priorityFamilies.Length == 0)
                return;

            var prioritized = new List<FontInfo>();
            var seen = new HashSet<FontInfo>();

            foreach (var fam in priorityFamilies)
            {
                if (string.IsNullOrEmpty(fam))
                    continue;

                foreach (var fi in fonts.Where(f => string.Equals(f.FamilyName, fam, StringComparison.OrdinalIgnoreCase)))
                {
                    prioritized.Add(fi);
                    seen.Add(fi);
                }
            }

            if (prioritized.Count == 0)
                return;

            var others = fonts.Where(f => !seen.Contains(f)).ToList();

            fonts.Clear();
            fonts.AddRange(prioritized);
            fonts.AddRange(others);

            fontIds.Clear();
            for (int i = 0; i < fonts.Count; i++)
                fontIds[fonts[i]] = i;

            layoutCache.Clear();
        }

        private IEnumerable<string> GetSystemFontPaths()
        {
            // De-dupe final results
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Safe enumerator that handles permissions and missing dirs
            IEnumerable<string> EnumerateFontsUnder(string root)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    yield break;

                var stack = new Stack<string>();
                stack.Push(root);

                while (stack.Count > 0)
                {
                    string dir = stack.Pop();

                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(dir); }
                    catch { files = Array.Empty<string>(); }

                    foreach (var f in files)
                    {
                        string ext;
                        try { ext = Path.GetExtension(f); }
                        catch { continue; }

                        if (string.Equals(ext, ".ttf", StringComparison.OrdinalIgnoreCase) && yielded.Add(f))
                            yield return f;
                    }

                    IEnumerable<string> subdirs;
                    try { subdirs = Directory.EnumerateDirectories(dir); }
                    catch { subdirs = Array.Empty<string>(); }

                    foreach (var d in subdirs)
                        stack.Push(d);
                }
            }

            // Build OS-specific search roots
            var roots = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // System fonts
                roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));

                // Per-user fonts (Windows 10+)
                var userFonts = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");
                roots.Add(userFonts);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // System & local fonts
                roots.Add("/usr/share/fonts");
                roots.Add("/usr/local/share/fonts");

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                roots.Add(Path.Combine(home, ".fonts"));                  // legacy
                roots.Add(Path.Combine(home, ".local", "share", "fonts"));// modern
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // System & local fonts
                roots.Add("/System/Library/Fonts");
                roots.Add("/System/Library/Fonts/Supplemental");
                roots.Add("/Library/Fonts");

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                roots.Add(Path.Combine(home, "Library", "Fonts"));
            }

            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var f in EnumerateFontsUnder(r))
                    yield return f;
        }

        public AtlasGlyph GetOrCreateGlyph(int codepoint, float pixelSize, FontInfo preferredFont = null)
        {
            // Try the preferred font first
            if (preferredFont != null)
            {
                var glyph = TryGetGlyphFromFont(preferredFont);
                if (glyph != null)
                    return glyph;
            }

            // not in either, Look in all loaded fonts for this glyph
            foreach (var font in fonts)
            {
                if (font == preferredFont) continue;

                var glyph = TryGetGlyphFromFont(font);
                if (glyph != null)
                    return glyph;
            }

            return null; // Glyph not found in any font

            AtlasGlyph TryGetGlyphFromFont(FontInfo font)
            {
                if (!HasGlyph(font, codepoint))
                    return null;

                var key = new AtlasGlyph.CacheKey(codepoint, pixelSize, font);
                if (glyphCache.TryGetValue(key, out var cachedGlyph))
                    return cachedGlyph;

                var glyph = new AtlasGlyph(codepoint, pixelSize, font, this);

                if (TryAddGlyphToAtlas(glyph))
                {
                    glyphCache[key] = glyph;
                    return glyph;
                }

                if (AllowExpansion && TryExpandAtlas(glyph) && TryAddGlyphToAtlas(glyph))
                {
                    glyphCache[key] = glyph;
                    return glyph;
                }

                glyphCache[key] = glyph;
                return glyph;
            }
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
            layoutCache.Clear();
            kerningMapCache.Clear();

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
            float s = font.ScaleForPixelHeight(pixelSize);
            ascent = font.Ascent * s;
            descent = font.Descent * s; // stb returns negative descent; caller may convert to positive if desired
            lineGap = font.Linegap * s;
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
            var key = (fontInfo, leftCodepoint, rightCodepoint);
            if (kerningMapCache.TryGetValue(key, out var kern))
                return kern;

            int leftGlyph = fontInfo.FindGlyphIndex(leftCodepoint);
            int rightGlyph = fontInfo.FindGlyphIndex(rightCodepoint);

            if (leftGlyph == 0 || rightGlyph == 0)
            {
                kerningMapCache[key] = 0;
                return 0;
            }

            float scale = fontInfo.ScaleForPixelHeight(pixelSize);
            int kernAdvance = fontInfo.GetGlyphKerningAdvance(leftGlyph, rightGlyph);

            float result = kernAdvance * scale;
            kerningMapCache[key] = result;
            return result;
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
                var empty = new TextLayout();
                empty.UpdateLayout(text, settings, this);
                return empty;
            }

            if (!CacheLayouts)
            {
                var direct = new TextLayout();
                direct.UpdateLayout(text, settings, this);
                return direct;
            }

            var key = GenerateLayoutCacheKey(text, settings);

            if (layoutCache.TryGetValue(key, out var cached))
                return cached;

            var layout = new TextLayout();
            layout.UpdateLayout(text, settings, this);

            layoutCache.Add(key, layout);
            return layout;
        }

        int GetFontId(FontInfo fi) => (fi != null && fontIds.TryGetValue(fi, out var id)) ? id : -1;
        LayoutCacheKey GenerateLayoutCacheKey(string text, TextLayoutSettings s)
            => new LayoutCacheKey(text, s.PixelSize, s.LetterSpacing, s.WordSpacing, s.LineHeight,
                   s.TabSize, s.WrapMode, s.Alignment, s.MaxWidth, GetFontId(s.PreferredFont));

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
