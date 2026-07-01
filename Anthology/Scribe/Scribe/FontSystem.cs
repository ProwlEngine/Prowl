using Prowl.Scribe.Internal;
using Prowl.Scribe.Sdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prowl.Vector;
using System.Runtime.InteropServices;
using System.Text;

namespace Prowl.Scribe
{
    /// <summary>
    /// Atlas rasterization quality. The value is the per-glyph em pixel height the distance field is
    /// generated at; the shader scales it to any display size, so higher mainly helps very small text
    /// and very sharp corners, at the cost of atlas memory and one-time generation time.
    /// </summary>
    public enum FontQuality
    {
        Low = 16,
        Normal = 32,
        High = 64,
        Ultra = 128
    }

    public class FontSystem
    {
        private readonly IFontRenderer renderer;
        private readonly BinPacker binPacker;
        private readonly List<FontFile> fallbackFonts;
        private readonly Dictionary<AtlasGlyph.CacheKey, AtlasGlyph> glyphCache;

        readonly LruCache<LayoutCacheKey, TextLayout> layoutCache;

        private object atlasTexture;
        private int atlasWidth;
        private int atlasHeight;

        // Reusable scratch buffers for DrawLayout - avoid per-frame List/array allocations.
        private readonly List<IFontRenderer.Vertex> drawVertices = new List<IFontRenderer.Vertex>(1024);
        private readonly List<int> drawIndices = new List<int>(1536);

        private bool useWhiteRect;
        private float whiteU0, whiteV0, whiteU1, whiteV1;

        // Settings
        public bool AllowExpansion { get; set; } = true;
        public float ExpansionFactor { get; set; } = 2f;
        public int MaxAtlasSize { get; set; } = 4096;
        public int Padding { get; set; } = 1;

        /// <summary>
        /// Width of the signed-distance range in atlas pixels. Must match the value the text shader
        /// uses for its screen-pixel-range calculation. Larger ranges allow softer/larger effects.
        /// </summary>
        public float DistanceRange { get; set; } = 4f;


        int _maxLayout = 256;
        public int MaxLayoutCacheSize {
            get => _maxLayout;
            set { _maxLayout = Math.Max(1, value); layoutCache.Capacity = _maxLayout; }
        }
        public bool CacheLayouts { get; set; } = false;

        public IEnumerable<FontFile> FallbackFonts => fallbackFonts;
        public int Width => atlasWidth;
        public int Height => atlasHeight;
        public object Texture => atlasTexture;
        public int FontCount => fallbackFonts.Count;

        /// <summary>
        /// Monotonically-increasing counter bumped every time the atlas is rebuilt/resized.
        /// <para>
        /// When the atlas grows, the backing texture is recreated and every cached
        /// <see cref="AtlasGlyph"/> is invalidated - their UVs and atlas positions belong to the
        /// previous texture. Any <see cref="TextLayout"/> created before the resize still holds
        /// references to those stale <see cref="AtlasGlyph"/> objects.
        /// </para>
        /// <para>
        /// Each <see cref="TextLayout"/> stamps <see cref="TextLayout.AtlasVersion"/> when it's
        /// built; consumers compare against this value (or call
        /// <see cref="TextLayout.EnsureUpToDate"/>) to detect staleness and re-layout.
        /// <see cref="DrawLayout"/> does the check automatically.
        /// </para>
        /// </summary>
        public int AtlasVersion { get; private set; }

        public FontSystem(IFontRenderer renderer, int initialWidth = 512, int initialHeight = 512, bool includeWhiteRect = true)
        {
            this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

            atlasWidth = initialWidth;
            atlasHeight = initialHeight;

            this.useWhiteRect = includeWhiteRect;

            atlasTexture = renderer.CreateTexture(atlasWidth, atlasHeight);
            binPacker = new BinPacker(atlasWidth, atlasHeight);
            fallbackFonts = new List<FontFile>();
            glyphCache = new Dictionary<AtlasGlyph.CacheKey, AtlasGlyph>();
            layoutCache = new LruCache<LayoutCacheKey, TextLayout>(_maxLayout);

            // Add a small white rectangle for rendering
            if (useWhiteRect)
                AddWhiteRect();
        }

        public void AddWhiteRect()
        {
            if (binPacker.TryPack(4 + Padding * 2, 4 + Padding * 2, out int x, out int y))
            {
                // RGBA, fully opaque white. In the SDF text shader the median of (1,1,1) reads as
                // fully inside, so this rect still renders as a solid fill.
                byte[] whiteData = new byte[4 * 4 * 4];
                Array.Fill<byte>(whiteData, 255);

                renderer.UpdateTextureRegion(atlasTexture,
                    new AtlasRect(x, y, 4, 4), whiteData);

                whiteU0 = (float)x / atlasWidth;
                whiteV0 = (float)y / atlasHeight;
                whiteU1 = (float)(x + 1) / atlasWidth;
                whiteV1 = (float)(y + 1) / atlasHeight;
            }
        }

        public void AddFallbackFont(FontFile font)
        {
            fallbackFonts.Add(font);

            // Fallback list changed -> cached glyphs may resolve to different fonts now.
            glyphCache.Clear();
            layoutCache.Clear();
            AtlasVersion++;
        }

        public IEnumerable<FontFile> EnumerateSystemFonts()
        {
            var paths = GetSystemFontPaths();
            foreach (var path in paths)
            {
                FontFile font = null;
                try
                {
                    font = new FontFile(path);
                }
                catch
                {
                    continue; // Silently skip problematic fonts
                }
                if (font != null)
                    yield return font;
            }
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

        public AtlasGlyph GetOrCreateGlyph(int codepoint, FontFile font, FontQuality quality)
        {
            if(font == null) throw new ArgumentNullException(nameof(font));

            // An unset/default FontQuality (0) is not a valid rasterization size, so callers that
            // build TextLayoutSettings without specifying Quality would otherwise get no glyphs.
            if ((int)quality <= 0) quality = FontQuality.Normal;

            int gi = font.FindGlyphIndex(codepoint);
            if (gi > 0)
                return GetOrAddGlyph(font, gi, quality);

            // Check fallback fonts (must match the requested style).
            foreach (var f in fallbackFonts)
            {
                if (f == font) continue;
                if (f.Style != font.Style) continue;
                int fgi = f.FindGlyphIndex(codepoint);
                if (fgi > 0)
                    return GetOrAddGlyph(f, fgi, quality);
            }

            return null; // Glyph not found in any font
        }

        /// <summary>
        /// Returns the atlas glyph for a specific glyph index in a specific font (no codepoint
        /// lookup, no fallback). Used by the shaper for substituted glyphs such as ligatures.
        /// </summary>
        public AtlasGlyph GetOrCreateGlyphByIndex(int glyphIndex, FontFile font, FontQuality quality)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if ((int)quality <= 0) quality = FontQuality.Normal;
            if (glyphIndex <= 0) return null;
            return GetOrAddGlyph(font, glyphIndex, quality);
        }

        private AtlasGlyph GetOrAddGlyph(FontFile font, int glyphIndex, FontQuality quality)
        {
            var key = new AtlasGlyph.CacheKey(glyphIndex, quality, font);
            if (glyphCache.TryGetValue(key, out var cachedGlyph))
                return cachedGlyph;

            var glyph = AtlasGlyph.FromGlyphIndex(font, glyphIndex, quality);

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

        // Rasterizes a glyph's distance field into the atlas once per quality. The field is
        // resolution independent, so the single entry serves every requested display size.
        private bool TryAddGlyphToAtlas(AtlasGlyph glyph)
        {
            if (!SdfScanlineGenerator.TryGenerate(glyph.Font, glyph.GlyphIndex, (int)glyph.Quality, DistanceRange, out var result))
                return true; // Empty glyph (e.g. space), nothing to pack

            int packWidth = result.Width + Padding * 2;
            int packHeight = result.Height + Padding * 2;

            if (binPacker.TryPack(packWidth, packHeight, out int x, out int y))
            {
                glyph.AtlasX = x + Padding;
                glyph.AtlasY = y + Padding;
                glyph.AtlasWidth = result.Width;
                glyph.AtlasHeight = result.Height;

                // Calculate texture coordinates
                glyph.U0 = (float)glyph.AtlasX / atlasWidth;
                glyph.V0 = (float)glyph.AtlasY / atlasHeight;
                glyph.U1 = (float)(glyph.AtlasX + glyph.AtlasWidth) / atlasWidth;
                glyph.V1 = (float)(glyph.AtlasY + glyph.AtlasHeight) / atlasHeight;

                // Padded glyph region in font units (Y up) - scaled per display size at draw time.
                glyph.RegionX0 = result.Rx0;
                glyph.RegionY0 = result.Ry0;
                glyph.RegionX1 = result.Rx1;
                glyph.RegionY1 = result.Ry1;

                // Upload distance field to atlas
                renderer.UpdateTextureRegion(atlasTexture,
                    new AtlasRect(glyph.AtlasX, glyph.AtlasY, glyph.AtlasWidth, glyph.AtlasHeight),
                    result.Rgba);

                return true;
            }

            return false;
        }

        private bool TryExpandAtlas(AtlasGlyph glyph)
        {
            if (!SdfScanlineGenerator.TryGenerate(glyph.Font, glyph.GlyphIndex, (int)glyph.Quality, DistanceRange, out var result))
                return true;

            int requiredWidth = result.Width + Padding * 2;
            int requiredHeight = result.Height + Padding * 2;

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

            // Bump version so any externally-held TextLayout knows it's stale.
            AtlasVersion++;

            // Re-add white rect
            if (useWhiteRect)
                AddWhiteRect();

            return true;
        }

        #region Metrics and Getters

        public GlyphMetrics? GetGlyphMetrics(FontFile fontInfo, int codepoint, float pixelSize)
        {
            int glyphIndex = fontInfo.FindGlyphIndex(codepoint);
            if (glyphIndex == 0) return null;
            return GetGlyphMetricsByIndex(fontInfo, glyphIndex, pixelSize);
        }

        /// <summary>Per-glyph horizontal metrics by glyph index (used by the shaper / substituted glyphs).</summary>
        public GlyphMetrics? GetGlyphMetricsByIndex(FontFile fontInfo, int glyphIndex, float pixelSize)
        {
            if (glyphIndex <= 0) return null;

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

        public void GetScaledVMetrics(FontFile font, float pixelSize, out float ascent, out float descent, out float lineGap)
        {
            float s = font.ScaleForPixelHeight(pixelSize);
            ascent = font.Ascent * s;
            descent = font.Descent * s; // stb returns negative descent; caller may convert to positive if desired
            lineGap = font.Linegap * s;
        }

        public float GetKerning(FontFile fontInfo, int leftCodepoint, int rightCodepoint, float pixelSize)
        {
            int leftGlyph = fontInfo.FindGlyphIndex(leftCodepoint);
            int rightGlyph = fontInfo.FindGlyphIndex(rightCodepoint);

            return GetKerningByGlyph(fontInfo, leftGlyph, rightGlyph, pixelSize);
        }

        public float GetKerningByGlyph(FontFile fontInfo, int leftGlyph, int rightGlyph, float pixelSize)
        {
            float scale = fontInfo.ScaleForPixelHeight(pixelSize);
            int kernAdvance = fontInfo.GetGlyphKerningAdvance(leftGlyph, rightGlyph);

            return kernAdvance * scale;
        }

        // Reusable GSUB shaping buffer (single-threaded, like the other caches).
        private List<GsubGlyph> _shapeBuf;

        /// <summary>
        /// Shapes a character range [<paramref name="start"/>, <paramref name="end"/>) into a
        /// positioned glyph run: codepoint mapping (surrogate-aware), GSUB substitution (ccmp/liga/
        /// rlig) and GPOS pair kerning folded into each glyph's advance. The run is split into
        /// maximal same-font segments (per fallback/selector resolution); shaping and kerning apply
        /// within a segment. Results are appended to <paramref name="output"/>.
        /// </summary>
        internal void ShapeRun(string text, int start, int end, FontFile requestedFont,
            Func<int, FontFile> selector, float pixelSize, FontQuality quality, List<ShapedGlyph> output)
        {
            output.Clear();
            var buf = _shapeBuf ??= new List<GsubGlyph>();
            buf.Clear();
            FontFile runFont = null;

            int i = start;
            while (i < end)
            {
                char c = text[i];
                int codepoint, charCount;
                if (char.IsHighSurrogate(c) && i + 1 < end && char.IsLowSurrogate(text[i + 1]))
                {
                    codepoint = char.ConvertToUtf32(c, text[i + 1]);
                    charCount = 2;
                }
                else
                {
                    codepoint = c;
                    charCount = 1;
                }

                FontFile reqFont = selector != null ? (selector(i) ?? requestedFont) : requestedFont;
                var ag = reqFont != null ? GetOrCreateGlyph(codepoint, reqFont, quality) : null;

                if (ag == null)
                {
                    // Missing in every font: flush so shaping/kerning doesn't cross the gap, then skip.
                    FlushSubRun(runFont, buf, pixelSize, quality, output);
                    buf.Clear();
                    runFont = null;
                    i += charCount;
                    continue;
                }

                if (runFont != null && !ReferenceEquals(ag.Font, runFont))
                {
                    FlushSubRun(runFont, buf, pixelSize, quality, output);
                    buf.Clear();
                }

                runFont = ag.Font;
                buf.Add(new GsubGlyph(ag.GlyphIndex, i, charCount));
                i += charCount;
            }

            FlushSubRun(runFont, buf, pixelSize, quality, output);
            buf.Clear();
        }

        private void FlushSubRun(FontFile font, List<GsubGlyph> buf, float pixelSize, FontQuality quality, List<ShapedGlyph> output)
        {
            if (font == null || buf.Count == 0)
                return;

            font.ApplyGsub(buf);
            float scale = font.ScaleForPixelHeight(pixelSize);

            for (int k = 0; k < buf.Count; k++)
            {
                var gg = buf[k];
                var atlas = GetOrCreateGlyphByIndex(gg.Glyph, font, quality);

                int adv = 0, lsb = 0;
                font.GetGlyphHorizontalMetrics(gg.Glyph, ref adv, ref lsb);
                float advance = adv * scale;
                if (k + 1 < buf.Count)
                    advance += font.GetGlyphKerningAdvance(gg.Glyph, buf[k + 1].Glyph) * scale;

                output.Add(new ShapedGlyph {
                    Glyph = atlas,
                    Advance = advance,
                    Cluster = gg.Cluster,
                    CharCount = gg.CharCount
                });
            }
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

        LayoutCacheKey GenerateLayoutCacheKey(string text, TextLayoutSettings s)
            => new LayoutCacheKey(text, s.PixelSize, s.LetterSpacing, s.WordSpacing, s.LineHeight,
                   s.TabSize, s.WrapMode, s.Alignment, s.MaxWidth, s.Font.GetHashCode());

        #endregion

        #region Updated API Methods

        public Float2 MeasureText(string text, float pixelSize, FontFile font, float letterSpacing = 0)
        {
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = pixelSize;
            settings.Font = font;
            settings.LetterSpacing = letterSpacing;

            var layout = CreateLayout(text, settings);
            return layout.Size;
        }

        public Float2 MeasureText(string text, TextLayoutSettings settings)
        {
            var layout = CreateLayout(text, settings);
            return layout.Size;
        }

        public void DrawText(string text, Float2 position, FontColor color, float pixelSize, FontFile font, float letterSpacing = 0)
        {
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = pixelSize;
            settings.Font = font;
            settings.LetterSpacing = letterSpacing;

            DrawText(text, position, color, settings);
        }


        public void DrawText(string text, Float2 position, FontColor color, TextLayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text)) return;

            var layout = CreateLayout(text, settings);
            DrawLayout(layout, position, color);
        }

        public void DrawLayout(TextLayout layout, Float2 position, FontColor color)
        {
            if (layout.Lines.Count == 0) return;

            // Atlas may have grown/rebuilt since the layout was created - UVs and glyph refs
            // would be stale. Re-layout in place so glyphs repopulate against the current atlas.
            layout.EnsureUpToDate(this);

            var vertices = drawVertices;
            var indices = drawIndices;
            vertices.Clear();
            indices.Clear();
            int vertexCount = 0;

            foreach (var line in layout.Lines)
            {
                foreach (var glyphInstance in line.Glyphs)
                {
                    var glyph = glyphInstance.Glyph;

                    // Only render if glyph is in atlas
                    if (!glyph.IsInAtlas || glyph.AtlasWidth <= 0 || glyph.AtlasHeight <= 0)
                        continue;

                    // Recover the pen origin (x) and baseline (y) from the glyph instance, then place
                    // the padded distance-field quad relative to them. The quad includes the
                    // distance-field margin, so it is larger than the glyph's ink bounds. The region
                    // is in font units; scale it to this instance's pixel size at draw time.
                    float ps = glyphInstance.PixelSize;
                    var gm = GetGlyphMetricsByIndex(glyph.Font, glyph.GlyphIndex, ps) ?? default;
                    float sc = glyph.Font.ScaleForPixelHeight(ps);

                    float penX = position.X + line.Position.X + glyphInstance.Position.X - gm.OffsetX;
                    float baselineY = position.Y + line.Position.Y + glyphInstance.Position.Y - gm.OffsetY;

                    float glyphX = penX + (float)(glyph.RegionX0 * sc);
                    float glyphY = baselineY + (float)(-glyph.RegionY1 * sc);
                    float glyphX1 = penX + (float)(glyph.RegionX1 * sc);
                    float glyphY1 = baselineY + (float)(-glyph.RegionY0 * sc);

                    // Create quad vertices
                    vertices.Add(new IFontRenderer.Vertex(new Float3(glyphX, glyphY, 0), color, new Float2(glyph.U0, glyph.V0)));
                    vertices.Add(new IFontRenderer.Vertex(new Float3(glyphX1, glyphY, 0), color, new Float2(glyph.U1, glyph.V0)));
                    vertices.Add(new IFontRenderer.Vertex(new Float3(glyphX, glyphY1, 0), color, new Float2(glyph.U0, glyph.V1)));
                    vertices.Add(new IFontRenderer.Vertex(new Float3(glyphX1, glyphY1, 0), color, new Float2(glyph.U1, glyph.V1)));

                    // Create quad indices (six Add calls - no per-quad array allocation)
                    indices.Add(vertexCount);
                    indices.Add(vertexCount + 1);
                    indices.Add(vertexCount + 2);
                    indices.Add(vertexCount + 1);
                    indices.Add(vertexCount + 3);
                    indices.Add(vertexCount + 2);
                    vertexCount += 4;
                }
            }

            if (vertices.Count > 0)
            {
#if NET5_0_OR_GREATER
                renderer.DrawQuads(atlasTexture,
                    CollectionsMarshal.AsSpan(vertices),
                    CollectionsMarshal.AsSpan(indices));
#else
                renderer.DrawQuads(atlasTexture, vertices.ToArray(), indices.ToArray());
#endif
            }
        }

        #endregion
    }
}
