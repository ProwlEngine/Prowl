using System;
using System.Collections.Generic;
using SharpFont;
using Silk.NET.SDL;

namespace Prowl.Runtime.GUI.Graphics
{
    public class ImFontAtlas
    {
        static char[] GetGlyphRangesDefault()
        {
            return new char[]
            {
                '\x0020', '\x00FF', // Basic Latin + Latin Supplement
                '\x0000',
            };
        }

        static uint Decode85Byte(char c) { return c >= '\\' ? c - 36u : c - 35u; }
        static unsafe void Decode85(string src, byte[] dst)
        {
            var srcidx = 0;
            var dstidx = 0;
            while (srcidx < src.Length)
            {
                uint tmp = Decode85Byte(src[srcidx + 0]) + 85u * (Decode85Byte(src[srcidx + 1]) + 85u * (Decode85Byte(src[srcidx + 2]) + 85u * (Decode85Byte(src[srcidx + 3]) + 85u * Decode85Byte(src[srcidx + 4]))));
                dst[dstidx + 0] = (byte)(tmp >> 0 & 0xFF);
                dst[dstidx + 1] = (byte)(tmp >> 8 & 0xFF);
                dst[dstidx + 2] = (byte)(tmp >> 16 & 0xFF);
                dst[dstidx + 3] = (byte)(tmp >> 24 & 0xFF);   // We can't assume little-endianess.
                srcidx += 5;
                dstidx += 4;
            }
        }

        // Members
        // (Access texture data via GetTexData*() calls which will setup a default font for you.)
        public object TexID;              // User data to refer to the texture once it has been uploaded to user's graphic systems. It ia passed back to you during rendering.
        byte[] TexPixelsAlpha8;    // 1 component per pixel, each component is unsigned 8-bit. Total size = TexWidth * TexHeight
        byte[] TexPixelsRGBA32;    // 4 component per pixel, each component is unsigned 8-bit. Total size = TexWidth * TexHeight * 4
        internal int TexWidth;           // Texture width calculated during Build().
        internal int TexHeight;          // Texture height calculated during Build().
        internal int TexDesiredWidth;    // Texture width desired by user before Build(). Must be a power-of-two. If have many glyphs your graphics API have texture size restrictions you may want to increase texture width to decrease height.
        internal Vector2 TexUvWhitePixel;    // Texture coordinates to a white pixel
        public List<ImFont> Fonts;              // Hold all the fonts returned by AddFont*. Fonts[0] is the default font upon calling ImGui::NewFrame(), use ImGui::PushFont()/PopFont() to change the current font.

        // Private
        internal List<ImFontConfig> ConfigData;         // Internal data

        //// Build pixels data. This is automatically for you by the GetTexData*** functions.
        //struct ImFontTempBuildData
        //{
        //    internal stbtt_fontinfo FontInfo;
        //    internal stbrp_rect Rects;
        //    internal stbtt_pack_range Ranges;
        //    internal int RangesCount;
        //};

        struct ImFontPackingRect
        {
            internal int id;

            //input
            internal int w, h;

            //output
            internal int x, y;
            internal bool was_packed;

            internal bool pack(MaxRectsBinPack spc)
            {
                was_packed = false;
                if (w == 0 || h == 0)
                    return false;

                var r = spc.Insert(w, h, MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestAreaFit);
                if (r.width == 0 || r.height == 0)
                    return false;

                x = (int)r.x;
                y = (int)r.y;

                was_packed = true;
                return true;
            }

        }

        internal bool Build()
        {
            System.Diagnostics.Debug.Assert(ConfigData.Count > 0);

            TexID = null;
            TexWidth = TexHeight = 0;
            TexUvWhitePixel = new Vector2(0, 0);
            ClearTexData();

            int total_glyph_count = 0;
            int total_glyph_range_count = 0;
            for (int input_i = 0; input_i < ConfigData.Count; input_i++)
            {
                ImFontConfig cfg = ConfigData[input_i];
                System.Diagnostics.Debug.Assert(cfg.DstFont != null && (!cfg.DstFont.IsLoaded() || cfg.DstFont.ContainerAtlas == this));

                System.Diagnostics.Debug.Assert(cfg.FontData != null);

                // Count glyphs
                if (cfg.GlyphRanges == null)
                    cfg.GlyphRanges = GetGlyphRangesDefault();

                for (int in_range = 0; cfg.GlyphRanges[in_range] > 0 && cfg.GlyphRanges[in_range + 1] > 0; in_range += 2)
                {
                    total_glyph_count += cfg.GlyphRanges[in_range + 1] - cfg.GlyphRanges[in_range] + 1;
                    total_glyph_range_count++;
                }
            }

            // Start packing. We need a known width for the skyline algorithm. Using a cheap heuristic here to decide of width. User can override TexDesiredWidth if they wish.
            // After packing is done, width shouldn't matter much, but some API/GPU have texture size limitations and increasing width can decrease height.
            TexWidth = TexDesiredWidth > 0 ? TexDesiredWidth : total_glyph_count > 4000 ? 4096 : total_glyph_count > 2000 ? 2048 : total_glyph_count > 1000 ? 1024 : 512;
            TexHeight = 0;
            int max_tex_height = 1024 * 32;
            var spc = new MaxRectsBinPack(TexWidth, max_tex_height, false);

            List<ImFontPackingRect> rects = new List<ImFontPackingRect>();
            RenderCustomTexData(spc, 0, rects);

            // First font pass: pack all glyphs (no rendering at this point, we are working with rectangles in an infinitely tall texture at this point)
            for (int input_i = 0; input_i < ConfigData.Count; input_i++)
            {
                ImFontConfig cfg = ConfigData[input_i];
                cfg.Face.SetPixelSizes((uint)(cfg.SizePixels * cfg.OversampleH), (uint)(cfg.SizePixels * cfg.OversampleV));
                for (int in_range = 0; cfg.GlyphRanges[in_range] > 0 && cfg.GlyphRanges[in_range + 1] > 0; in_range += 2)
                {
                    var glyphs = new List<Rect>(cfg.GlyphRanges[in_range + 1] - cfg.GlyphRanges[in_range] + 1);
                    var packedGlyphs = new List<Rect>(cfg.GlyphRanges[in_range + 1] - cfg.GlyphRanges[in_range] + 1);
                    for (var range = cfg.GlyphRanges[in_range]; range <= cfg.GlyphRanges[in_range + 1]; range++)
                    {
                        char c = range;

                        uint glyphIndex = cfg.Face.GetCharIndex(c);
                        cfg.Face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);

                        //added padding to keep from bleeding
                        glyphs.Add(Rect.CreateFromMinMax(new(0, 0), new(cfg.Face.Glyph.Metrics.Width + 2, cfg.Face.Glyph.Metrics.Height + 2)));
                        spc.Insert((int)cfg.Face.Glyph.Metrics.Width + 2, (int)cfg.Face.Glyph.Metrics.Height + 2, MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestAreaFit);
                    }
                    spc.Insert(glyphs, packedGlyphs, MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestAreaFit);
                    System.Diagnostics.Debug.Assert(glyphs.Count == packedGlyphs.Count);

                    for (var i = 0; i < glyphs.Count; i++)
                    {
                        var c = cfg.GlyphRanges[in_range] + i;
                        var g = glyphs[i];
                        var pg = packedGlyphs[i];

                        var was_packed = pg.width > 0 && pg.height > 0;
                        var r = new ImFontPackingRect()
                        {
                            id = c,
                            x = (int)pg.x + 1,
                            y = (int)pg.y + 1,
                            w = (int)pg.width,
                            h = (int)pg.height,
                            was_packed = was_packed
                        };

                        if (was_packed)
                            TexHeight = Mathf.Max(TexHeight, r.y + r.h);

                        rects.Add(r);
                    }
                }
            }

            // Create texture
            int v = TexHeight;
            v--; v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16; v++;
            TexHeight = v;
            TexPixelsAlpha8 = new byte[TexWidth * TexHeight];

            for (int input_i = 0; input_i < ConfigData.Count; input_i++)
            {
                ImFontConfig cfg = ConfigData[input_i];
                ImFont dst_font = cfg.DstFont;

                int unscaled_ascent = cfg.Face.Ascender,
                    unscaled_descent = cfg.Face.Descender;

                float font_scale = cfg.SizePixels / (unscaled_ascent - unscaled_descent); //taken from stbtt_ScaleForPixelHeight
                var max_height = cfg.Face.Height * font_scale;

                float ascent = unscaled_ascent * font_scale;
                float descent = unscaled_descent * font_scale;
                if (!cfg.MergeMode)
                {
                    dst_font.ContainerAtlas = this;
                    dst_font.ConfigData = cfg;
                    dst_font.ConfigDataCount = 0;
                    dst_font.FontSize = cfg.SizePixels;
                    dst_font.Ascent = ascent;
                    dst_font.Descent = descent;
                    dst_font.Glyphs.resize(0);
                }

                dst_font.ConfigDataCount++;
                float off_y = cfg.MergeMode && cfg.MergeGlyphCenterV ? (ascent - dst_font.Ascent) * 0.5f : 0.0f;

                //render
                for (var i = 0; i < rects.Count; i++)
                {
                    var rect = rects[i];
                    //if (rect.id > 0 /*&& rect.was_packed*/)
                    {
                        var codepoint = (ushort)rect.id;
                        if (cfg.MergeMode && dst_font.HasGlyph((char)codepoint))
                            continue;

                        uint glyphIndex = cfg.Face.GetCharIndex(codepoint);
                        cfg.Face.LoadGlyph(glyphIndex, LoadFlags.Render, LoadTarget.Normal);
                        cfg.Face.Glyph.RenderGlyph(RenderMode.Normal);

                        var bmp = cfg.Face.Glyph.Bitmap;
                        for (var x = 0; x < bmp.Width; x++)
                            for (var y = 0; y < bmp.Rows; y++)
                                TexPixelsAlpha8[rect.x + x + (rect.y + y) * TexWidth] = bmp.BufferData[x + y * bmp.Pitch];
                    }
                }

                cfg.Face.SetPixelSizes((uint)cfg.SizePixels, (uint)cfg.SizePixels);

                //need to calculate origin/baseline
                var top = 0f;
                var bot = 0f;

                for (var i = 0; i < rects.Count; i++)
                {
                    var rect = rects[i];
                    //if (rect.id > 0 /*&& rect.was_packed*/)
                    {
                        var codepoint = (ushort)rect.id;
                        if (cfg.MergeMode && dst_font.HasGlyph((char)codepoint))
                            continue;

                        uint glyphIndex = cfg.Face.GetCharIndex(codepoint);
                        cfg.Face.LoadGlyph(glyphIndex, LoadFlags.ComputeMetrics, LoadTarget.Normal);
                        var glyphTop = (float)cfg.Face.Glyph.Metrics.HorizontalBearingY;
                        var glyphBot = (float)(cfg.Face.Glyph.Metrics.Height - cfg.Face.Glyph.Metrics.HorizontalBearingY);
                        if (glyphTop > top) top = glyphTop;
                        if (glyphBot > bot) bot = glyphBot;
                    }
                }

                //dst_font.FallbackGlyph = null; // Always clear fallback so FindGlyph can return NULL. It will be set again in BuildLookupTable()
                for (var i = 0; i < rects.Count; i++)
                {
                    var rect = rects[i];
                    //if (rect.id > 0 /*&& rect.was_packed*/)
                    {
                        var codepoint = (ushort)rect.id;
                        if (cfg.MergeMode && dst_font.HasGlyph((char)codepoint))
                            continue;

                        uint glyphIndex = cfg.Face.GetCharIndex(codepoint);
                        cfg.Face.LoadGlyph(glyphIndex, LoadFlags.ComputeMetrics, LoadTarget.Normal);

                        dst_font.Glyphs.resize(dst_font.Glyphs.Count + 1);
                        var glyph = dst_font.Glyphs[dst_font.Glyphs.Count - 1];
                        glyph.Codepoint = codepoint;

                        glyph.X0 = (int)cfg.Face.Glyph.Metrics.HorizontalBearingX + (float)cfg.Face.Glyph.BitmapLeft;
                        glyph.X1 = (int)cfg.Face.Glyph.Metrics.Width + glyph.X0;
                        glyph.Y0 = top - (float)cfg.Face.Glyph.Metrics.HorizontalBearingY;
                        glyph.Y1 = glyph.Y0 + (float)cfg.Face.Glyph.Metrics.Height;

                        glyph.U0 = rect.x / (float)TexWidth;
                        glyph.V0 = rect.y / (float)TexHeight;
                        glyph.U1 = (rect.x + rect.w) / (float)TexWidth;
                        glyph.V1 = (rect.y + rect.h) / (float)TexHeight;

                        glyph.XAdvance = (int)cfg.Face.Glyph.Metrics.HorizontalAdvance + (float)cfg.GlyphExtraSpacing.x;  // Bake spacing into XAdvance
                        if (cfg.PixelSnapH)
                            glyph.XAdvance = (int)(glyph.XAdvance + 0.5f);
                        dst_font.Glyphs[dst_font.Glyphs.Count - 1] = glyph;
                    }
                }

                cfg.DstFont.BuildLookupTable();
            }

            // Cleanup temporaries
            //ImGui::MemFree(buf_packedchars);
            //ImGui::MemFree(buf_ranges);
            //ImGui::MemFree(tmp_array);

            // Render into our custom data block
            RenderCustomTexData(spc, 1, rects);

            return true;
        }

        void RenderCustomTexData(MaxRectsBinPack spc, int pass, List<ImFontPackingRect> rects)
        {
            // A work of art lies ahead! (. = white layer, X = black layer, others are blank)

            // The white texels on the top left are the ones we'll use everywhere in ImGui to render filled shapes.
            const int TEX_DATA_W = 90;
            const int TEX_DATA_H = 27;
            const string texture_data =
                "..-         -XXXXXXX-    X    -           X           -XXXXXXX          -          XXXXXXX" +
                "..-         -X.....X-   X.X   -          X.X          -X.....X          -          X.....X" +
                "---         -XXX.XXX-  X...X  -         X...X         -X....X           -           X....X" +
                "X           -  X.X  - X.....X -        X.....X        -X...X            -            X...X" +
                "XX          -  X.X  -X.......X-       X.......X       -X..X.X           -           X.X..X" +
                "X.X         -  X.X  -XXXX.XXXX-       XXXX.XXXX       -X.X X.X          -          X.X X.X" +
                "X..X        -  X.X  -   X.X   -          X.X          -XX   X.X         -         X.X   XX" +
                "X...X       -  X.X  -   X.X   -    XX    X.X    XX    -      X.X        -        X.X      " +
                "X....X      -  X.X  -   X.X   -   X.X    X.X    X.X   -       X.X       -       X.X       " +
                "X.....X     -  X.X  -   X.X   -  X..X    X.X    X..X  -        X.X      -      X.X        " +
                "X......X    -  X.X  -   X.X   - X...XXXXXX.XXXXXX...X -         X.X   XX-XX   X.X         " +
                "X.......X   -  X.X  -   X.X   -X.....................X-          X.X X.X-X.X X.X          " +
                "X........X  -  X.X  -   X.X   - X...XXXXXX.XXXXXX...X -           X.X..X-X..X.X           " +
                "X.........X -XXX.XXX-   X.X   -  X..X    X.X    X..X  -            X...X-X...X            " +
                "X..........X-X.....X-   X.X   -   X.X    X.X    X.X   -           X....X-X....X           " +
                "X......XXXXX-XXXXXXX-   X.X   -    XX    X.X    XX    -          X.....X-X.....X          " +
                "X...X..X    ---------   X.X   -          X.X          -          XXXXXXX-XXXXXXX          " +
                "X..X X..X   -       -XXXX.XXXX-       XXXX.XXXX       ------------------------------------" +
                "X.X  X..X   -       -X.......X-       X.......X       -    XX           XX    -           " +
                "XX    X..X  -       - X.....X -        X.....X        -   X.X           X.X   -           " +
                "      X..X          -  X...X  -         X...X         -  X..X           X..X  -           " +
                "       XX           -   X.X   -          X.X          - X...XXXXXXXXXXXXX...X -           " +
                "------------        -    X    -           X           -X.....................X-           " +
                "                    ----------------------------------- X...XXXXXXXXXXXXX...X -           " +
                "                                                      -  X..X           X..X  -           " +
                "                                                      -   X.X           X.X   -           " +
                "                                                      -    XX           XX    -           ";

            //List < stbrp_rect > &rects = *(List<stbrp_rect>*)p_rects;
            if (pass == 0)
            {
                // Request rectangles
                var custom = new ImFontPackingRect();
                custom.w = TEX_DATA_W * 2 + 1;
                custom.h = TEX_DATA_H + 1;
                rects.Add(custom);
                custom.pack(spc);
            }
            else if (pass == 1)
            {
                // Render/copy pixels
                //the first rect in rects will always be custom font data
                var r = rects[0];
                for (int y = 0, n = 0; y < TEX_DATA_H; y++)
                    for (int x = 0; x < TEX_DATA_W; x++, n++)
                    {
                        int offset0 = r.x + x + (r.y + y) * TexWidth;
                        int offset1 = offset0 + 1 + TEX_DATA_W;
                        TexPixelsAlpha8[offset0] = (byte)(texture_data[n] == '.' ? 0xFF : 0x00);
                        TexPixelsAlpha8[offset1] = (byte)(texture_data[n] == 'X' ? 0xFF : 0x00);
                    }

                Vector2 tex_uv_scale = new Vector2(1.0f / TexWidth, 1.0f / TexHeight);
                TexUvWhitePixel = new Vector2((r.x + 0.5f) * tex_uv_scale.x, (r.y + 0.5f) * tex_uv_scale.y);

                //TODO: Finish render custom text
                //// Setup mouse cursors
                //var cursor_datas = new Vector2[,]
                //{
                //    // Pos ........ Size ......... Offset ......
                //    { new Vector2(0, 3),  new Vector2(12, 19), new Vector2(0, 0) }, // ImGuiMouseCursor_Arrow
                //    { new Vector2(13, 0), new Vector2(7, 16),  new Vector2(4, 8) }, // ImGuiMouseCursor_TextInput
                //    { new Vector2(31, 0), new Vector2(23, 23), new Vector2(11, 11) }, // ImGuiMouseCursor_Move
                //    { new Vector2(21, 0), new Vector2(9, 23),  new Vector2(5, 11) }, // ImGuiMouseCursor_ResizeNS
                //    { new Vector2(55, 18),new Vector2(23, 9),  new Vector2(11, 5) }, // ImGuiMouseCursor_ResizeEW
                //    { new Vector2(73, 0), new Vector2(17, 17), new Vector2(9, 9) }, // ImGuiMouseCursor_ResizeNESW
                //    { new Vector2(55, 0), new Vector2(17, 17), new Vector2(9, 9) }, // ImGuiMouseCursor_ResizeNWSE
                //};

                //for (int type = 0; type < 7; type++)
                //{
                //    ImGuiMouseCursorData cursor_data = ImGui.Instance.State.MouseCursorData[type];
                //    Vector2 pos = cursor_datas[type, 0] + new Vector2((float)r.x, (float)r.y);
                //    Vector2 size = cursor_datas[type, 1];
                //    cursor_data.Type = (ImGuiMouseCursor)type;
                //    cursor_data.Count = size;
                //    cursor_data.HotOffset = cursor_datas[type, 2];
                //    cursor_data.TexUvMin[0] = (pos) * tex_uv_scale;
                //    cursor_data.TexUvMax[0] = (pos + size) * tex_uv_scale;
                //    pos.x += TEX_DATA_W + 1;
                //    cursor_data.TexUvMin[1] = (pos) * tex_uv_scale;
                //    cursor_data.TexUvMax[1] = (pos + size) * tex_uv_scale;
                //}
            }
        }


        public ImFontAtlas()
        {
            ConfigData = new List<ImFontConfig>();
            Fonts = new List<ImFont>();
        }
        //~ImFontAtlas();

        ImFont AddFont(ImFontConfig font_cfg)
        {
            System.Diagnostics.Debug.Assert(font_cfg.FontData != null && font_cfg.FontDataSize > 0);
            System.Diagnostics.Debug.Assert(font_cfg.SizePixels > 0.0f);

            // Create new font
            if (!font_cfg.MergeMode)
            {
                ImFont font = new ImFont();
                //IM_PLACEMENT_NEW(font) ImFont();
                Fonts.Add(font);
            }

            ConfigData.Add(font_cfg);
            ImFontConfig new_font_cfg = ConfigData[ConfigData.Count - 1];
            new_font_cfg.DstFont = Fonts[Fonts.Count - 1];
            if (!new_font_cfg.FontDataOwnedByAtlas)
            {
                //new_font_cfg.FontData = ImGui::MemAlloc(new_font_cfg.FontDataSize);
                new_font_cfg.FontDataOwnedByAtlas = true;
                var fontData = new byte[font_cfg.FontData.Length];
                Array.Copy(font_cfg.FontData, fontData, fontData.Length);
                new_font_cfg.FontData = fontData;

                //memcpy(new_font_cfg.FontData, font_cfg->FontData, (size_t)new_font_cfg.FontDataSize);
            }

            var library = new Library();
            new_font_cfg.Face = new Face(library, new_font_cfg.FontData, 0);
            //new_font_cfg.Face.SetCharSize(0, new_font_cfg.SizePixels, 0, 96);
            new_font_cfg.Face.SetPixelSizes(0, (uint)new_font_cfg.SizePixels);

            // Invalidate texture
            ClearTexData();
            return Fonts[Fonts.Count - 1];
        }
        internal ImFont AddFontDefault(float fontSize = 13f, ImFontConfig font_cfg_template = null)
        {
            ImFontConfig font_cfg = font_cfg_template ?? new ImFontConfig();// font_cfg_template != null ? *font_cfg_template : ImFontConfig();
            if (font_cfg_template == null)
            {
                font_cfg.OversampleH = font_cfg.OversampleV = 2;
                font_cfg.PixelSnapH = true;
            }

            var ttf_compressed_base85 = STB.GetDefaultCompressedFontDataTTFBase85();
            ImFont font = AddFontFromMemoryCompressedBase85TTF(ttf_compressed_base85, fontSize, font_cfg, GetGlyphRangesDefault());
            return font;
        }
        //TODO: AddFontFromFileTTF
        //ImFont AddFontFromFileTTF(string filename, float size_pixels, ImFontConfig font_cfg_template = null, uint[] glyph_ranges = null)
        //{
        //    int data_size = 0;
        //    byte[] data = ImLoadFileToMemory(filename, "rb", &data_size, 0);
        //    if (data != null)
        //    {
        //        System.Diagnostics.Debug.Assert(false);
        //        return null;
        //    }
        //    ImFontConfig font_cfg = font_cfg_template ?? new ImFontConfig();
        //    if (font_cfg.Name == null)
        //    {
        //        // Store a short copy of filename into into the font name for convenience
        //        font_cfg.Name = System.IO.Path.GetFileName(filename);
        //    }
        //    return AddFontFromMemoryTTF(data, data_size, size_pixels, font_cfg, glyph_ranges);
        //}
        // Transfer ownership of 'ttf_data' to ImFontAtlas, will be deleted after Build()
        ImFont AddFontFromMemoryTTF(byte[] ttf_data, int ttf_size, float size_pixels, ImFontConfig font_cfg_template = null, char[] glyph_ranges = null)
        {
            ImFontConfig font_cfg = font_cfg_template ?? new ImFontConfig();
            System.Diagnostics.Debug.Assert(font_cfg.FontData == null);
            font_cfg.FontData = ttf_data;
            font_cfg.FontDataSize = ttf_size;
            font_cfg.SizePixels = size_pixels;
            if (glyph_ranges != null)
                font_cfg.GlyphRanges = glyph_ranges;
            return AddFont(font_cfg);
        }
        // 'compressed_ttf_data' still owned by caller. Compress with binary_to_compressed_c.cpp
        unsafe ImFont AddFontFromMemoryCompressedTTF(byte* compressed_ttf_data, uint compressed_ttf_size, float size_pixels, ImFontConfig font_cfg_template = null, char[] glyph_ranges = null)
        {
            uint buf_decompressed_size = STB.stb_decompress_length(compressed_ttf_data);
            //unsigned char* buf_decompressed_data = (unsigned char*)ImGui::MemAlloc(buf_decompressed_size);
            byte[] _buf_decompressed_data = new byte[buf_decompressed_size];

            fixed (byte* buf_decompressed_data = _buf_decompressed_data)
            {
                STB.stb_decompress(buf_decompressed_data, compressed_ttf_data, compressed_ttf_size);

                ImFontConfig font_cfg = font_cfg_template ?? new ImFontConfig();

                System.Diagnostics.Debug.Assert(font_cfg.FontData == null);
                font_cfg.FontDataOwnedByAtlas = true;
                return AddFontFromMemoryTTF(_buf_decompressed_data, (int)buf_decompressed_size, size_pixels, font_cfg_template, glyph_ranges);
            }
        }
        // 'compressed_ttf_data_base85' still owned by caller. Compress with binary_to_compressed_c.cpp with -base85 paramaeter
        unsafe ImFont AddFontFromMemoryCompressedBase85TTF(string compressed_ttf_data_base85, float size_pixels, ImFontConfig font_cfg = null, char[] glyph_ranges = null)
        {
            uint compressed_ttf_size = (uint)((compressed_ttf_data_base85.Length + 4) / 5 * 4);
            byte[] _compressed_ttf = new byte[compressed_ttf_size];
            //void* compressed_ttf = ImGui::MemAlloc((size_t)compressed_ttf_size);
            fixed (byte* compressed_ttf = _compressed_ttf)
            {
                Decode85(compressed_ttf_data_base85, _compressed_ttf);
                ImFont font = AddFontFromMemoryCompressedTTF(compressed_ttf, compressed_ttf_size, size_pixels, font_cfg, glyph_ranges);
                //ImGui::MemFree(compressed_ttf);
                return font;
            }
        }

        // Clear the CPU-side texture data. Saves RAM once the texture has been copied to graphics memory.
        void ClearTexData()
        {
            TexPixelsAlpha8 = null;
            TexPixelsRGBA32 = null;
        }
        // Clear the input TTF data (inc sizes, glyph ranges)
        void ClearInputData()
        {
            for (int i = 0; i < ConfigData.Count; i++)
                if (ConfigData[i].FontData != null && ConfigData[i].FontDataOwnedByAtlas)
                {
                    //ImGui::MemFree(ConfigData[i].FontData);
                    ConfigData[i].FontData = null;
                }

            // When clearing this we lose access to the font name and other information used to build the font.
            for (int i = 0; i < Fonts.Count; i++)
                if (Fonts[i].ConfigData.FontData == null)
                {
                    Fonts[i].ConfigData = null;
                    Fonts[i].ConfigDataCount = 0;
                }
            ConfigData.Clear();
        }

        // Clear the ImGui-side font data (glyphs storage, UV coordinates)
        void ClearFonts()
        {
            //for (int i = 0; i < Fonts.Count; i++)
            //{
            //    Fonts[i]->~ImFont();
            //    ImGui::MemFree(Fonts[i]);
            //}
            Fonts.Clear();
        }

        // Clear all
        internal void Clear()
        {
            ClearInputData();
            ClearTexData();
            ClearFonts();
        }


        // Retrieve texture data
        // User is in charge of copying the pixels into graphics memory, then call SetTextureUserID()
        // After loading the texture into your graphic system, store your texture handle in 'TexID' (ignore if you aren't using multiple fonts nor images)
        // RGBA32 format is provided for convenience and high compatibility, but note that all RGB pixels are white, so 75% of the memory is wasted.
        // Pitch = Width * BytesPerPixels
        // 1 byte per-pixel
        public byte[] GetTexDataAsAlpha8(out int out_width, out int out_height)
        {
            // Build atlas on demand
            if (TexPixelsAlpha8 == null)
            {
                if (ConfigData.Count == 0)
                    AddFontDefault(20f);
                Build();
            }

            out_width = TexWidth;
            out_height = TexHeight;

            //if (out_bytes_per_pixel) *out_bytes_per_pixel = 1;
            return TexPixelsAlpha8;
        }

        // 4 bytes-per-pixel
        public byte[] GetTexDataAsRGBA32(out int out_width, out int out_height)
        {
            out_width = TexWidth;
            out_height = TexHeight;

            if (TexPixelsRGBA32 == null)
            {
                var pixels = GetTexDataAsAlpha8(out out_width, out out_height);
                TexPixelsRGBA32 = new byte[TexWidth * TexHeight * 4];

                int src = 0;
                int dst = 0;
                for (int n = 0; n < TexWidth * TexHeight; n++)
                {
                    TexPixelsRGBA32[dst++] = 0xff;
                    TexPixelsRGBA32[dst++] = 0xff;
                    TexPixelsRGBA32[dst++] = 0xff;
                    TexPixelsRGBA32[dst++] = pixels[src++];
                }
            }
            return TexPixelsRGBA32;
        }

        // 4 bytes-per-pixel
        public byte[] GetTexDataAsARGB32(out int out_width, out int out_height)
        {
            out_width = TexWidth;
            out_height = TexHeight;

            // Convert to RGBA32 format on demand
            // Although it is likely to be the most commonly used format, our font rendering is 1 channel / 8 bpp
            if (TexPixelsRGBA32 == null)
            {
                //unsigned char* pixels;
                var pixels = GetTexDataAsAlpha8(out out_width, out out_height);
                TexPixelsRGBA32 = new byte[TexWidth * TexHeight * 4];

                //const unsigned char* src = pixels;
                //unsigned int* dst = TexPixelsRGBA32;
                var dst = 0;
                for (var n = 0; n < TexWidth * TexHeight; n++)
                {
                    TexPixelsRGBA32[dst++] = 0xff;
                    TexPixelsRGBA32[dst++] = 0xff;
                    TexPixelsRGBA32[dst++] = 0xff;
                    TexPixelsRGBA32[dst++] = pixels[n];
                    //*dst++ = ((unsigned int)(*src++) << 24) | 0x00FFFFFF;

                }
            }

            return TexPixelsRGBA32;
            //*out_pixels = (unsigned char*)TexPixelsRGBA32;
            //if (out_width) *out_width = TexWidth;
            //if (out_height) *out_height = TexHeight;
            //if (out_bytes_per_pixel) *out_bytes_per_pixel = 4;
        }

        //public void SetTexID(void* id) { TexID = id; }

        // Helpers to retrieve list of common Unicode ranges (2 value per range, values are inclusive, zero-terminated list)
        // (Those functions could be static but aren't so most users don't have to refer to the ImFontAtlas:: name ever if in their code; just using io.Fonts->)
        //public  ImWchar* GetGlyphRangesDefault();    // Basic Latin, Extended Latin
        //public  ImWchar* GetGlyphRangesKorean();     // Default + Korean characters
        //public  ImWchar* GetGlyphRangesJapanese();   // Default + Hiragana, Katakana, Half-Width, Selection of 1946 Ideographs
        //public  ImWchar* GetGlyphRangesChinese();    // Japanese + full set of about 21000 CJK Unified Ideographs
        //public  ImWchar* GetGlyphRangesCyrillic();   // Default + about 400 Cyrillic characters

    }


    public unsafe static class STB
    {
        public static uint stb_decompress_length(byte* input)
        {
            return (uint)((input[8] << 24) + (input[9] << 16) + (input[10] << 8) + input[11]);
        }


        private static byte* stb__barrier;
        private static byte* stb__barrier2;
        private static byte* stb__barrier3;
        private static byte* stb__barrier4;
        private static byte* stb__dout;

        static void stb__match(byte* data, uint length)
        {
            // INVERSE of memmove... write each byte before copying the next...
            System.Diagnostics.Debug.Assert(stb__dout + length <= stb__barrier);
            if (stb__dout + length > stb__barrier) { stb__dout += length; return; }
            if (data < stb__barrier4) { stb__dout = stb__barrier + 1; return; }
            while (length-- > 0) *stb__dout++ = *data++;
        }

        static void stb__lit(byte* data, uint length)
        {
            System.Diagnostics.Debug.Assert(stb__dout + length <= stb__barrier);
            if (stb__dout + length > stb__barrier) { stb__dout += length; return; }
            if (data < stb__barrier2) { stb__dout = stb__barrier + 1; return; }
            memcpy(stb__dout, data, length);
            stb__dout += length;
        }
        public static void memcpy(byte* destination, byte* source, uint length)
        {
            var index = 0;
            while (index < length)
            {
                destination[index] = source[index];
                index++;
            }
        }

        private static uint stb__in2(byte* i, int x) { return (uint)((i[x] << 8) + i[x + 1]); }
        private static uint stb__in3(byte* i, int x) { return (uint)((i[x] << 16) + stb__in2(i, x + 1)); }
        private static uint stb__in4(byte* i, int x) { return (uint)((i[x] << 24) + stb__in3(i, x + 1)); }

        static byte* stb_decompress_token(byte* i)
        {
            if (*i >= 0x20)
            { // use fewer if's for cases that expand small
                if (*i >= 0x80) { stb__match(stb__dout - i[1] - 1, i[0] - 0x80u + 1u); i += 2; }
                else if (*i >= 0x40) { stb__match(stb__dout - (stb__in2(i, 0) - 0x4000u + 1u), i[2] + 1u); i += 3; }
                else /* *i >= 0x20 */ { stb__lit(i + 1, i[0] - 0x20u + 1u); i += 1 + i[0] - 0x20 + 1; }
            }
            else
            { // more ifs for cases that expand large, since overhead is amortized
                if (*i >= 0x18) { stb__match(stb__dout - (stb__in3(i, 0) - 0x180000u + 1u), i[3] + 1u); i += 4; }
                else if (*i >= 0x10) { stb__match(stb__dout - (stb__in3(i, 0) - 0x100000u + 1u), stb__in2(i, 3) + 1u); i += 5; }
                else if (*i >= 0x08) { stb__lit(i + 2, stb__in2(i, 0) - 0x0800u + 1u); i += 2 + (stb__in2(i, 0) - 0x0800u + 1u); }
                else if (*i == 0x07) { stb__lit(i + 3, stb__in2(i, 1) + 1u); i += 3 + (stb__in2(i, 1) + 1u); }
                else if (*i == 0x06) { stb__match(stb__dout - (stb__in3(i, 1) + 1u), i[4] + 1u); i += 5; }
                else if (*i == 0x04) { stb__match(stb__dout - (stb__in3(i, 1) + 1u), stb__in2(i, 4) + 1u); i += 6; }
            }
            return i;
        }


        static uint stb_adler32(uint adler32, byte* buffer, uint buflen)
        {
            ulong ADLER_MOD = 65521;
            ulong s1 = adler32 & 0xffff, s2 = adler32 >> 16;
            ulong i;
            uint blocklen;

            blocklen = buflen % 5552u;
            while (buflen > 0u)
            {
                for (i = 0; i + 7 < blocklen; i += 8)
                {
                    s1 += buffer[0]; s2 += s1;
                    s1 += buffer[1]; s2 += s1;
                    s1 += buffer[2]; s2 += s1;
                    s1 += buffer[3]; s2 += s1;
                    s1 += buffer[4]; s2 += s1;
                    s1 += buffer[5]; s2 += s1;
                    s1 += buffer[6]; s2 += s1;
                    s1 += buffer[7]; s2 += s1;

                    buffer += 8;
                }

                for (; i < blocklen; ++i)
                {
                    s1 += *buffer++;
                    s2 += s1;
                }

                s1 %= ADLER_MOD; s2 %= ADLER_MOD;
                buflen -= blocklen;
                blocklen = 5552u;
            }
            return (uint)(s2 << 16) + (uint)s1;
        }

        public static uint stb_decompress(byte* output, byte* i, uint length)
        {
            uint olen;
            if (stb__in4(i, 0) != 0x57bC0000) return 0;
            if (stb__in4(i, 4) != 0) return 0; // error! stream is > 4GB
            olen = stb_decompress_length(i);
            stb__barrier2 = i;
            stb__barrier3 = i + length;
            stb__barrier = output + olen;
            stb__barrier4 = output;
            i += 16;

            stb__dout = output;
            for (; ; )
            {
                byte* old_i = i;
                i = stb_decompress_token(i);
                if (i == old_i)
                {
                    if (*i == 0x05 && i[1] == 0xfa)
                    {
                        System.Diagnostics.Debug.Assert(stb__dout == output + olen);
                        if (stb__dout != output + olen) return 0;
                        if (stb_adler32(1, output, olen) != stb__in4(i, 2))
                            return 0;
                        return olen;
                    }
                    else
                    {

                        System.Diagnostics.Debug.Assert(false); /* NOTREACHED */
                        return 0;
                    }
                }

                System.Diagnostics.Debug.Assert(stb__dout <= output + olen);
                if (stb__dout > output + olen)
                    return 0;
            }
        }

        //-----------------------------------------------------------------------------
        // ProggyClean.ttf
        // Copyright (c) 2004, 2005 Tristan Grimmer
        // MIT license (see License.txt in http://www.upperbounds.net/download/ProggyClean.ttf.zip)
        // Download and more information at http://upperbounds.net
        //-----------------------------------------------------------------------------
        // File: 'ProggyClean.ttf' (41208 bytes)
        // Exported using binary_to_compressed_c.cpp
        //-----------------------------------------------------------------------------
        const string proggy_clean_ttf_compressed_data_base85 = //[11980 + 1] =
        "7])#######hV0qs'/###[),##/l:$#Q6>##5[n42>c-TH`->>#/e>11NNV=Bv(*:.F?uu#(gRU.o0XGH`$vhLG1hxt9?W`#,5LsCp#-i>.r$<$6pD>Lb';9Crc6tgXmKVeU2cD4Eo3R/" +
"2*>]b(MC;$jPfY.;h^`IWM9<Lh2TlS+f-s$o6Q<BWH`YiU.xfLq$N;$0iR/GX:U(jcW2p/W*q?-qmnUCI;jHSAiFWM.R*kU@C=GH?a9wp8f$e.-4^Qg1)Q-GL(lf(r/7GrRgwV%MS=C#" +
"`8ND>Qo#t'X#(v#Y9w0#1D$CIf;W'#pWUPXOuxXuU(H9M(1<q-UE31#^-V'8IRUo7Qf./L>=Ke$$'5F%)]0^#0X@U.a<r:QLtFsLcL6##lOj)#.Y5<-R&KgLwqJfLgN&;Q?gI^#DY2uL" +
"i@^rMl9t=cWq6##weg>$FBjVQTSDgEKnIS7EM9>ZY9w0#L;>>#Mx&4Mvt//L[MkA#W@lK.N'[0#7RL_&#w+F%HtG9M#XL`N&.,GM4Pg;-<nLENhvx>-VsM.M0rJfLH2eTM`*oJMHRC`N" +
"kfimM2J,W-jXS:)r0wK#@Fge$U>`w'N7G#$#fB#$E^$#:9:hk+eOe--6x)F7*E%?76%^GMHePW-Z5l'&GiF#$956:rS?dA#fiK:)Yr+`&#0j@'DbG&#^$PG.Ll+DNa<XCMKEV*N)LN/N" +
"*b=%Q6pia-Xg8I$<MR&,VdJe$<(7G;Ckl'&hF;;$<_=X(b.RS%%)###MPBuuE1V:v&cX&#2m#(&cV]`k9OhLMbn%s$G2,B$BfD3X*sp5#l,$R#]x_X1xKX%b5U*[r5iMfUo9U`N99hG)" +
"tm+/Us9pG)XPu`<0s-)WTt(gCRxIg(%6sfh=ktMKn3j)<6<b5Sk_/0(^]AaN#(p/L>&VZ>1i%h1S9u5o@YaaW$e+b<TWFn/Z:Oh(Cx2$lNEoN^e)#CFY@@I;BOQ*sRwZtZxRcU7uW6CX" +
"ow0i(?$Q[cjOd[P4d)]>ROPOpxTO7Stwi1::iB1q)C_=dV26J;2,]7op$]uQr@_V7$q^%lQwtuHY]=DX,n3L#0PHDO4f9>dC@O>HBuKPpP*E,N+b3L#lpR/MrTEH.IAQk.a>D[.e;mc." +
"x]Ip.PH^'/aqUO/$1WxLoW0[iLA<QT;5HKD+@qQ'NQ(3_PLhE48R.qAPSwQ0/WK?Z,[x?-J;jQTWA0X@KJ(_Y8N-:/M74:/-ZpKrUss?d#dZq]DAbkU*JqkL+nwX@@47`5>w=4h(9.`G" +
"CRUxHPeR`5Mjol(dUWxZa(>STrPkrJiWx`5U7F#.g*jrohGg`cg:lSTvEY/EV_7H4Q9[Z%cnv;JQYZ5q.l7Zeas:HOIZOB?G<Nald$qs]@]L<J7bR*>gv:[7MI2k).'2($5FNP&EQ(,)" +
"U]W]+fh18.vsai00);D3@4ku5P?DP8aJt+;qUM]=+b'8@;mViBKx0DE[-auGl8:PJ&Dj+M6OC]O^((##]`0i)drT;-7X`=-H3[igUnPG-NZlo.#k@h#=Ork$m>a>$-?Tm$UV(?#P6YY#" +
"'/###xe7q.73rI3*pP/$1>s9)W,JrM7SN]'/4C#v$U`0#V.[0>xQsH$fEmPMgY2u7Kh(G%siIfLSoS+MK2eTM$=5,M8p`A.;_R%#u[K#$x4AG8.kK/HSB==-'Ie/QTtG?-.*^N-4B/ZM" +
"_3YlQC7(p7q)&](`6_c)$/*JL(L-^(]$wIM`dPtOdGA,U3:w2M-0<q-]L_?^)1vw'.,MRsqVr.L;aN&#/EgJ)PBc[-f>+WomX2u7lqM2iEumMTcsF?-aT=Z-97UEnXglEn1K-bnEO`gu" +
"Ft(c%=;Am_Qs@jLooI&NX;]0#j4#F14;gl8-GQpgwhrq8'=l_f-b49'UOqkLu7-##oDY2L(te+Mch&gLYtJ,MEtJfLh'x'M=$CS-ZZ%P]8bZ>#S?YY#%Q&q'3^Fw&?D)UDNrocM3A76/" +
"/oL?#h7gl85[qW/NDOk%16ij;+:1a'iNIdb-ou8.P*w,v5#EI$TWS>Pot-R*H'-SEpA:g)f+O$%%`kA#G=8RMmG1&O`>to8bC]T&$,n.LoO>29sp3dt-52U%VM#q7'DHpg+#Z9%H[K<L" +
"%a2E-grWVM3@2=-k22tL]4$##6We'8UJCKE[d_=%wI;'6X-GsLX4j^SgJ$##R*w,vP3wK#iiW&#*h^D&R?jp7+/u&#(AP##XU8c$fSYW-J95_-Dp[g9wcO&#M-h1OcJlc-*vpw0xUX&#" +
"OQFKNX@QI'IoPp7nb,QU//MQ&ZDkKP)X<WSVL(68uVl&#c'[0#(s1X&xm$Y%B7*K:eDA323j998GXbA#pwMs-jgD$9QISB-A_(aN4xoFM^@C58D0+Q+q3n0#3U1InDjF682-SjMXJK)(" +
"h$hxua_K]ul92%'BOU&#BRRh-slg8KDlr:%L71Ka:.A;%YULjDPmL<LYs8i#XwJOYaKPKc1h:'9Ke,g)b),78=I39B;xiY$bgGw-&.Zi9InXDuYa%G*f2Bq7mn9^#p1vv%#(Wi-;/Z5h" +
"o;#2:;%d&#x9v68C5g?ntX0X)pT`;%pB3q7mgGN)3%(P8nTd5L7GeA-GL@+%J3u2:(Yf>et`e;)f#Km8&+DC$I46>#Kr]]u-[=99tts1.qb#q72g1WJO81q+eN'03'eM>&1XxY-caEnO" +
"j%2n8)),?ILR5^.Ibn<-X-Mq7[a82Lq:F&#ce+S9wsCK*x`569E8ew'He]h:sI[2LM$[guka3ZRd6:t%IG:;$%YiJ:Nq=?eAw;/:nnDq0(CYcMpG)qLN4$##&J<j$UpK<Q4a1]MupW^-" +
"sj_$%[HK%'F####QRZJ::Y3EGl4'@%FkiAOg#p[##O`gukTfBHagL<LHw%q&OV0##F=6/:chIm0@eCP8X]:kFI%hl8hgO@RcBhS-@Qb$%+m=hPDLg*%K8ln(wcf3/'DW-$.lR?n[nCH-" +
"eXOONTJlh:.RYF%3'p6sq:UIMA945&^HFS87@$EP2iG<-lCO$%c`uKGD3rC$x0BL8aFn--`ke%#HMP'vh1/R&O_J9'um,.<tx[@%wsJk&bUT2`0uMv7gg#qp/ij.L56'hl;.s5CUrxjO" +
"M7-##.l+Au'A&O:-T72L]P`&=;ctp'XScX*rU.>-XTt,%OVU4)S1+R-#dg0/Nn?Ku1^0f$B*P:Rowwm-`0PKjYDDM'3]d39VZHEl4,.j']Pk-M.h^&:0FACm$maq-&sgw0t7/6(^xtk%" +
"LuH88Fj-ekm>GA#_>568x6(OFRl-IZp`&b,_P'$M<Jnq79VsJW/mWS*PUiq76;]/NM_>hLbxfc$mj`,O;&%W2m`Zh:/)Uetw:aJ%]K9h:TcF]u_-Sj9,VK3M.*'&0D[Ca]J9gp8,kAW]" +
"%(?A%R$f<->Zts'^kn=-^@c4%-pY6qI%J%1IGxfLU9CP8cbPlXv);C=b),<2mOvP8up,UVf3839acAWAW-W?#ao/^#%KYo8fRULNd2.>%m]UK:n%r$'sw]J;5pAoO_#2mO3n,'=H5(et" +
"Hg*`+RLgv>=4U8guD$I%D:W>-r5V*%j*W:Kvej.Lp$<M-SGZ':+Q_k+uvOSLiEo(<aD/K<CCc`'Lx>'?;++O'>()jLR-^u68PHm8ZFWe+ej8h:9r6L*0//c&iH&R8pRbA#Kjm%upV1g:" +
"a_#Ur7FuA#(tRh#.Y5K+@?3<-8m0$PEn;J:rh6?I6uG<-`wMU'ircp0LaE_OtlMb&1#6T.#FDKu#1Lw%u%+GM+X'e?YLfjM[VO0MbuFp7;>Q&#WIo)0@F%q7c#4XAXN-U&VB<HFF*qL(" +
"$/V,;(kXZejWO`<[5??ewY(*9=%wDc;,u<'9t3W-(H1th3+G]ucQ]kLs7df($/*JL]@*t7Bu_G3_7mp7<iaQjO@.kLg;x3B0lqp7Hf,^Ze7-##@/c58Mo(3;knp0%)A7?-W+eI'o8)b<" +
"nKnw'Ho8C=Y>pqB>0ie&jhZ[?iLR@@_AvA-iQC(=ksRZRVp7`.=+NpBC%rh&3]R:8XDmE5^V8O(x<<aG/1N$#FX$0V5Y6x'aErI3I$7x%E`v<-BY,)%-?Psf*l?%C3.mM(=/M0:JxG'?" +
"7WhH%o'a<-80g0NBxoO(GH<dM]n.+%q@jH?f.UsJ2Ggs&4<-e47&Kl+f//9@`b+?.TeN_&B8Ss?v;^Trk;f#YvJkl&w$]>-+k?'(<S:68tq*WoDfZu';mM?8X[ma8W%*`-=;D.(nc7/;" +
")g:T1=^J$&BRV(-lTmNB6xqB[@0*o.erM*<SWF]u2=st-*(6v>^](H.aREZSi,#1:[IXaZFOm<-ui#qUq2$##Ri;u75OK#(RtaW-K-F`S+cF]uN`-KMQ%rP/Xri.LRcB##=YL3BgM/3M" +
"D?@f&1'BW-)Ju<L25gl8uhVm1hL$##*8###'A3/LkKW+(^rWX?5W_8g)a(m&K8P>#bmmWCMkk&#TR`C,5d>g)F;t,4:@_l8G/5h4vUd%&%950:VXD'QdWoY-F$BtUwmfe$YqL'8(PWX(" +
"P?^@Po3$##`MSs?DWBZ/S>+4%>fX,VWv/w'KD`LP5IbH;rTV>n3cEK8U#bX]l-/V+^lj3;vlMb&[5YQ8#pekX9JP3XUC72L,,?+Ni&co7ApnO*5NK,((W-i:$,kp'UDAO(G0Sq7MVjJs" +
"bIu)'Z,*[>br5fX^:FPAWr-m2KgL<LUN098kTF&#lvo58=/vjDo;.;)Ka*hLR#/k=rKbxuV`>Q_nN6'8uTG&#1T5g)uLv:873UpTLgH+#FgpH'_o1780Ph8KmxQJ8#H72L4@768@Tm&Q" +
"h4CB/5OvmA&,Q&QbUoi$a_%3M01H)4x7I^&KQVgtFnV+;[Pc>[m4k//,]1?#`VY[Jr*3&&slRfLiVZJ:]?=K3Sw=[$=uRB?3xk48@aeg<Z'<$#4H)6,>e0jT6'N#(q%.O=?2S]u*(m<-" +
"V8J'(1)G][68hW$5'q[GC&5j`TE?m'esFGNRM)j,ffZ?-qx8;->g4t*:CIP/[Qap7/9'#(1sao7w-.qNUdkJ)tCF&#B^;xGvn2r9FEPFFFcL@.iFNkTve$m%#QvQS8U@)2Z+3K:AKM5i" +
"sZ88+dKQ)W6>J%CL<KE>`.d*(B`-n8D9oK<Up]c$X$(,)M8Zt7/[rdkqTgl-0cuGMv'?>-XV1q['-5k'cAZ69e;D_?$ZPP&s^+7])$*$#@QYi9,5P&#9r+$%CE=68>K8r0=dSC%%(@p7" +
".m7jilQ02'0-VWAg<a/''3u.=4L$Y)6k/K:_[3=&jvL<L0C/2'v:^;-DIBW,B4E68:kZ;%?8(Q8BH=kO65BW?xSG&#@uU,DS*,?.+(o(#1vCS8#CHF>TlGW'b)Tq7VT9q^*^$$.:&N@@" +
"$&)WHtPm*5_rO0&e%K&#-30j(E4#'Zb.o/(Tpm$>K'f@[PvFl,hfINTNU6u'0pao7%XUp9]5.>%h`8_=VYbxuel.NTSsJfLacFu3B'lQSu/m6-Oqem8T+oE--$0a/k]uj9EwsG>%veR*" +
"hv^BFpQj:K'#SJ,sB-'#](j.Lg92rTw-*n%@/;39rrJF,l#qV%OrtBeC6/,;qB3ebNW[?,Hqj2L.1NP&GjUR=1D8QaS3Up&@*9wP?+lo7b?@%'k4`p0Z$22%K3+iCZj?XJN4Nm&+YF]u" +
"@-W$U%VEQ/,,>>#)D<h#`)h0:<Q6909ua+&VU%n2:cG3FJ-%@Bj-DgLr`Hw&HAKjKjseK</xKT*)B,N9X3]krc12t'pgTV(Lv-tL[xg_%=M_q7a^x?7Ubd>#%8cY#YZ?=,`Wdxu/ae&#" +
"w6)R89tI#6@s'(6Bf7a&?S=^ZI_kS&ai`&=tE72L_D,;^R)7[$s<Eh#c&)q.MXI%#v9ROa5FZO%sF7q7Nwb&#ptUJ:aqJe$Sl68%.D###EC><?-aF&#RNQv>o8lKN%5/$(vdfq7+ebA#" +
"u1p]ovUKW&Y%q]'>$1@-[xfn$7ZTp7mM,G,Ko7a&Gu%G[RMxJs[0MM%wci.LFDK)(<c`Q8N)jEIF*+?P2a8g%)$q]o2aH8C&<SibC/q,(e:v;-b#6[$NtDZ84Je2KNvB#$P5?tQ3nt(0" +
"d=j.LQf./Ll33+(;q3L-w=8dX$#WF&uIJ@-bfI>%:_i2B5CsR8&9Z&#=mPEnm0f`<&c)QL5uJ#%u%lJj+D-r;BoF&#4DoS97h5g)E#o:&S4weDF,9^Hoe`h*L+_a*NrLW-1pG_&2UdB8" +
"6e%B/:=>)N4xeW.*wft-;$'58-ESqr<b?UI(_%@[P46>#U`'6AQ]m&6/`Z>#S?YY#Vc;r7U2&326d=w&H####?TZ`*4?&.MK?LP8Vxg>$[QXc%QJv92.(Db*B)gb*BM9dM*hJMAo*c&#" +
"b0v=Pjer]$gG&JXDf->'StvU7505l9$AFvgYRI^&<^b68?j#q9QX4SM'RO#&sL1IM.rJfLUAj221]d##DW=m83u5;'bYx,*Sl0hL(W;;$doB&O/TQ:(Z^xBdLjL<Lni;''X.`$#8+1GD" +
":k$YUWsbn8ogh6rxZ2Z9]%nd+>V#*8U_72Lh+2Q8Cj0i:6hp&$C/:p(HK>T8Y[gHQ4`4)'$Ab(Nof%V'8hL&#<NEdtg(n'=S1A(Q1/I&4([%dM`,Iu'1:_hL>SfD07&6D<fp8dHM7/g+" +
"tlPN9J*rKaPct&?'uBCem^jn%9_K)<,C5K3s=5g&GmJb*[SYq7K;TRLGCsM-$$;S%:Y@r7AK0pprpL<Lrh,q7e/%KWK:50I^+m'vi`3?%Zp+<-d+$L-Sv:@.o19n$s0&39;kn;S%BSq*" +
"$3WoJSCLweV[aZ'MQIjO<7;X-X;&+dMLvu#^UsGEC9WEc[X(wI7#2.(F0jV*eZf<-Qv3J-c+J5AlrB#$p(H68LvEA'q3n0#m,[`*8Ft)FcYgEud]CWfm68,(aLA$@EFTgLXoBq/UPlp7" +
":d[/;r_ix=:TF`S5H-b<LI&HY(K=h#)]Lk$K14lVfm:x$H<3^Ql<M`$OhapBnkup'D#L$Pb_`N*g]2e;X/Dtg,bsj&K#2[-:iYr'_wgH)NUIR8a1n#S?Yej'h8^58UbZd+^FKD*T@;6A" +
"7aQC[K8d-(v6GI$x:T<&'Gp5Uf>@M.*J:;$-rv29'M]8qMv-tLp,'886iaC=Hb*YJoKJ,(j%K=H`K.v9HggqBIiZu'QvBT.#=)0ukruV&.)3=(^1`o*Pj4<-<aN((^7('#Z0wK#5GX@7" +
"u][`*S^43933A4rl][`*O4CgLEl]v$1Q3AeF37dbXk,.)vj#x'd`;qgbQR%FW,2(?LO=s%Sc68%NP'##Aotl8x=BE#j1UD([3$M(]UI2LX3RpKN@;/#f'f/&_mt&F)XdF<9t4)Qa.*kT" +
"LwQ'(TTB9.xH'>#MJ+gLq9-##@HuZPN0]u:h7.T..G:;$/Usj(T7`Q8tT72LnYl<-qx8;-HV7Q-&Xdx%1a,hC=0u+HlsV>nuIQL-5<N?)NBS)QN*_I,?&)2'IM%L3I)X((e/dl2&8'<M" +
":^#M*Q+[T.Xri.LYS3v%fF`68h;b-X[/En'CR.q7E)p'/kle2HM,u;^%OKC-N+Ll%F9CF<Nf'^#t2L,;27W:0O@6##U6W7:$rJfLWHj$#)woqBefIZ.PK<b*t7ed;p*_m;4ExK#h@&]>" +
"_>@kXQtMacfD.m-VAb8;IReM3$wf0''hra*so568'Ip&vRs849'MRYSp%:t:h5qSgwpEr$B>Q,;s(C#$)`svQuF$##-D,##,g68@2[T;.XSdN9Qe)rpt._K-#5wF)sP'##p#C0c%-Gb%" +
"hd+<-j'Ai*x&&HMkT]C'OSl##5RG[JXaHN;d'uA#x._U;.`PU@(Z3dt4r152@:v,'R.Sj'w#0<-;kPI)FfJ&#AYJ&#//)>-k=m=*XnK$>=)72L]0I%>.G690a:$##<,);?;72#?x9+d;" +
"^V'9;jY@;)br#q^YQpx:X#Te$Z^'=-=bGhLf:D6&bNwZ9-ZD#n^9HhLMr5G;']d&6'wYmTFmL<LD)F^%[tC'8;+9E#C$g%#5Y>q9wI>P(9mI[>kC-ekLC/R&CH+s'B;K-M6$EB%is00:" +
"+A4[7xks.LrNk0&E)wILYF@2L'0Nb$+pv<(2.768/FrY&h$^3i&@+G%JT'<-,v`3;_)I9M^AE]CN?Cl2AZg+%4iTpT3<n-&%H%b<FDj2M<hH=&Eh<2Len$b*aTX=-8QxN)k11IM1c^j%" +
"9s<L<NFSo)B?+<-(GxsF,^-Eh@$4dXhN$+#rxK8'je'D7k`e;)2pYwPA'_p9&@^18ml1^[@g4t*[JOa*[=Qp7(qJ_oOL^('7fB&Hq-:sf,sNj8xq^>$U4O]GKx'm9)b@p7YsvK3w^YR-" +
"CdQ*:Ir<($u&)#(&?L9Rg3H)4fiEp^iI9O8KnTj,]H?D*r7'M;PwZ9K0E^k&-cpI;.p/6_vwoFMV<->#%Xi.LxVnrU(4&8/P+:hLSKj$#U%]49t'I:rgMi'FL@a:0Y-uA[39',(vbma*" +
"hU%<-SRF`Tt:542R_VV$p@[p8DV[A,?1839FWdF<TddF<9Ah-6&9tWoDlh]&1SpGMq>Ti1O*H&#(AL8[_P%.M>v^-))qOT*F5Cq0`Ye%+$B6i:7@0IX<N+T+0MlMBPQ*Vj>SsD<U4JHY" +
"8kD2)2fU/M#$e.)T4,_=8hLim[&);?UkK'-x?'(:siIfL<$pFM`i<?%W(mGDHM%>iWP,##P`%/L<eXi:@Z9C.7o=@(pXdAO/NLQ8lPl+HPOQa8wD8=^GlPa8TKI1CjhsCTSLJM'/Wl>-" +
"S(qw%sf/@%#B6;/U7K]uZbi^Oc^2n<bhPmUkMw>%t<)'mEVE''n`WnJra$^TKvX5B>;_aSEK',(hwa0:i4G?.Bci.(X[?b*($,=-n<.Q%`(X=?+@Am*Js0&=3bh8K]mL<LoNs'6,'85`" +
"0?t/'_U59@]ddF<#LdF<eWdF<OuN/45rY<-L@&#+fm>69=Lb,OcZV/);TTm8VI;?%OtJ<(b4mq7M6:u?KRdF<gR@2L=FNU-<b[(9c/ML3m;Z[$oF3g)GAWqpARc=<ROu7cL5l;-[A]%/" +
"+fsd;l#SafT/f*W]0=O'$(Tb<[)*@e775R-:Yob%g*>l*:xP?Yb.5)%w_I?7uk5JC+FS(m#i'k.'a0i)9<7b'fs'59hq$*5Uhv##pi^8+hIEBF`nvo`;'l0.^S1<-wUK2/Coh58KKhLj" +
"M=SO*rfO`+qC`W-On.=AJ56>>i2@2LH6A:&5q`?9I3@@'04&p2/LVa*T-4<-i3;M9UvZd+N7>b*eIwg:CC)c<>nO&#<IGe;__.thjZl<%w(Wk2xmp4Q@I#I9,DF]u7-P=.-_:YJ]aS@V" +
"?6*C()dOp7:WL,b&3Rg/.cmM9&r^>$(>.Z-I&J(Q0Hd5Q%7Co-b`-c<N(6r@ip+AurK<m86QIth*#v;-OBqi+L7wDE-Ir8K['m+DDSLwK&/.?-V%U_%3:qKNu$_b*B-kp7NaD'QdWQPK" +
"Yq[@>P)hI;*_F]u`Rb[.j8_Q/<&>uu+VsH$sM9TA%?)(vmJ80),P7E>)tjD%2L=-t#fK[%`v=Q8<FfNkgg^oIbah*#8/Qt$F&:K*-(N/'+1vMB,u()-a.VUU*#[e%gAAO(S>WlA2);Sa" +
">gXm8YB`1d@K#n]76-a$U,mF<fX]idqd)<3,]J7JmW4`6]uks=4-72L(jEk+:bJ0M^q-8Dm_Z?0olP1C9Sa&H[d&c$ooQUj]Exd*3ZM@-WGW2%s',B-_M%>%Ul:#/'xoFM9QX-$.QN'>" +
"[%$Z$uF6pA6Ki2O5:8w*vP1<-1`[G,)-m#>0`P&#eb#.3i)rtB61(o'$?X3B</R90;eZ]%Ncq;-Tl]#F>2Qft^ae_5tKL9MUe9b*sLEQ95C&`=G?@Mj=wh*'3E>=-<)Gt*Iw)'QG:`@I" +
"wOf7&]1i'S01B+Ev/Nac#9S;=;YQpg_6U`*kVY39xK,[/6Aj7:'1Bm-_1EYfa1+o&o4hp7KN_Q(OlIo@S%;jVdn0'1<Vc52=u`3^o-n1'g4v58Hj&6_t7$##?M)c<$bgQ_'SY((-xkA#" +
"Y(,p'H9rIVY-b,'%bCPF7.J<Up^,(dU1VY*5#WkTU>h19w,WQhLI)3S#f$2(eb,jr*b;3Vw]*7NH%$c4Vs,eD9>XW8?N]o+(*pgC%/72LV-u<Hp,3@e^9UB1J+ak9-TN/mhKPg+AJYd$" +
"MlvAF_jCK*.O-^(63adMT->W%iewS8W6m2rtCpo'RS1R84=@paTKt)>=%&1[)*vp'u+x,VrwN;&]kuO9JDbg=pO$J*.jVe;u'm0dr9l,<*wMK*Oe=g8lV_KEBFkO'oU]^=[-792#ok,)" +
"i]lR8qQ2oA8wcRCZ^7w/Njh;?.stX?Q1>S1q4Bn$)K1<-rGdO'$Wr.Lc.CG)$/*JL4tNR/,SVO3,aUw'DJN:)Ss;wGn9A32ijw%FL+Z0Fn.U9;reSq)bmI32U==5ALuG&#Vf1398/pVo" +
"1*c-(aY168o<`JsSbk-,1N;$>0:OUas(3:8Z972LSfF8eb=c-;>SPw7.6hn3m`9^Xkn(r.qS[0;T%&Qc=+STRxX'q1BNk3&*eu2;&8q$&x>Q#Q7^Tf+6<(d%ZVmj2bDi%.3L2n+4W'$P" +
"iDDG)g,r%+?,$@?uou5tSe2aN_AQU*<h`e-GI7)?OK2A.d7_c)?wQ5AS@DL3r#7fSkgl6-++D:'A,uq7SvlB$pcpH'q3n0#_%dY#xCpr-l<F0NR@-##FEV6NTF6##$l84N1w?AO>'IAO" +
"URQ##V^Fv-XFbGM7Fl(N<3DhLGF%q.1rC$#:T__&Pi68%0xi_&[qFJ(77j_&JWoF.V735&T,[R*:xFR*K5>>#`bW-?4Ne_&6Ne_&6Ne_&n`kr-#GJcM6X;uM6X;uM(.a..^2TkL%oR(#" +
";u.T%fAr%4tJ8&><1=GHZ_+m9/#H1F^R#SC#*N=BA9(D?v[UiFY>>^8p,KKF.W]L29uLkLlu/+4T<XoIB&hx=T1PcDaB&;HH+-AFr?(m9HZV)FKS8JCw;SD=6[^/DZUL`EUDf]GGlG&>" +
"w$)F./^n3+rlo+DB;5sIYGNk+i1t-69Jg--0pao7Sm#K)pdHW&;LuDNH@H>#/X-TI(;P>#,Gc>#0Su>#4`1?#8lC?#<xU?#@.i?#D:%@#HF7@#LRI@#P_[@#Tkn@#Xw*A#]-=A#a9OA#" +
"d<F&#*;G##.GY##2Sl##6`($#:l:$#>xL$#B.`$#F:r$#JF.%#NR@%#R_R%#Vke%#Zww%#_-4&#3^Rh%Sflr-k'MS.o?.5/sWel/wpEM0%3'/1)K^f1-d>G21&v(35>V`39V7A4=onx4" +
"A1OY5EI0;6Ibgr6M$HS7Q<)58C5w,;WoA*#[%T*#`1g*#d=#+#hI5+#lUG+#pbY+#tnl+#x$),#&1;,#*=M,#.I`,#2Ur,#6b.-#;w[H#iQtA#m^0B#qjBB#uvTB##-hB#'9$C#+E6C#" +
"/QHC#3^ZC#7jmC#;v)D#?,<D#C8ND#GDaD#KPsD#O]/E#g1A5#KA*1#gC17#MGd;#8(02#L-d3#rWM4#Hga1#,<w0#T.j<#O#'2#CYN1#qa^:#_4m3#o@/=#eG8=#t8J5#`+78#4uI-#" +
"m3B2#SB[8#Q0@8#i[*9#iOn8#1Nm;#^sN9#qh<9#:=x-#P;K2#$%X9#bC+.#Rg;<#mN=.#MTF.#RZO.#2?)4#Y#(/#[)1/#b;L/#dAU/#0Sv;#lY$0#n`-0#sf60#(F24#wrH0#%/e0#" +
"TmD<#%JSMFove:CTBEXI:<eh2g)B,3h2^G3i;#d3jD>)4kMYD4lVu`4m`:&5niUA5@(A5BA1]PBB:xlBCC=2CDLXMCEUtiCf&0g2'tN?PGT4CPGT4CPGT4CPGT4CPGT4CPGT4CPGT4CP" +
"GT4CPGT4CPGT4CPGT4CPGT4CPGT4CP-qekC`.9kEg^+F$kwViFJTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5KTB&5o,^<-28ZI'O?;xp" +
"O?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xpO?;xp;7q-#lLYI:xvD=#";

        public static string GetDefaultCompressedFontDataTTFBase85()
        {
            return proggy_clean_ttf_compressed_data_base85;
        }
    }

    internal class MaxRectsBinPack
    {

        public int binWidth = 0;
        public int binHeight = 0;
        public bool allowRotations;

        public List<Rect> usedRectangles = new List<Rect>();
        public List<Rect> freeRectangles = new List<Rect>();

        public enum FreeRectChoiceHeuristic
        {
            RectBestShortSideFit, //< -BSSF: Positions the rectangle against the short side of a free rectangle into which it fits the best.
            RectBestLongSideFit, //< -BLSF: Positions the rectangle against the long side of a free rectangle into which it fits the best.
            RectBestAreaFit, //< -BAF: Positions the rectangle into the smallest free rect into which it fits.
            RectBottomLeftRule, //< -BL: Does the Tetris placement.
            RectContactPointRule //< -CP: Choosest the placement where the rectangle touches other rects as much as possible.
        };

        public MaxRectsBinPack(int width, int height, bool rotations = true)
        {
            Init(width, height, rotations);
        }

        public void Init(int width, int height, bool rotations = true)
        {
            binWidth = width;
            binHeight = height;
            allowRotations = rotations;

            Rect n = new Rect();
            n.x = 0;
            n.y = 0;
            n.width = width;
            n.height = height;

            usedRectangles.Clear();

            freeRectangles.Clear();
            freeRectangles.Add(n);
        }

        public Rect Insert(int width, int height, FreeRectChoiceHeuristic method)
        {
            Rect newNode = new Rect();
            int score1 = 0; // Unused in this function. We don't need to know the score after finding the position.
            int score2 = 0;
            switch (method)
            {
                case FreeRectChoiceHeuristic.RectBestShortSideFit: newNode = FindPositionForNewNodeBestShortSideFit(width, height, ref score1, ref score2); break;
                case FreeRectChoiceHeuristic.RectBottomLeftRule: newNode = FindPositionForNewNodeBottomLeft(width, height, ref score1, ref score2); break;
                case FreeRectChoiceHeuristic.RectContactPointRule: newNode = FindPositionForNewNodeContactPoint(width, height, ref score1); break;
                case FreeRectChoiceHeuristic.RectBestLongSideFit: newNode = FindPositionForNewNodeBestLongSideFit(width, height, ref score2, ref score1); break;
                case FreeRectChoiceHeuristic.RectBestAreaFit: newNode = FindPositionForNewNodeBestAreaFit(width, height, ref score1, ref score2); break;
            }

            if (newNode.height == 0)
                return newNode;

            int numRectanglesToProcess = freeRectangles.Count;
            for (int i = 0; i < numRectanglesToProcess; ++i)
            {
                if (SplitFreeNode(freeRectangles[i], ref newNode))
                {
                    freeRectangles.RemoveAt(i);
                    --i;
                    --numRectanglesToProcess;
                }
            }

            PruneFreeList();

            usedRectangles.Add(newNode);
            return newNode;
        }

        public void Insert(List<Rect> rects, List<Rect> dst, FreeRectChoiceHeuristic method)
        {
            dst.Clear();
            dst.AddRange(new Rect[rects.Count]);

            var remaining = rects.Count;
            var completed = new bool[rects.Count];
            while (remaining > 0)
            {
                int bestScore1 = int.MaxValue;
                int bestScore2 = int.MaxValue;
                int bestRectIndex = -1;
                Rect bestNode = new Rect();

                for (int i = 0; i < rects.Count; ++i)
                {
                    if (!completed[i])
                    {
                        int score1 = 0;
                        int score2 = 0;
                        Rect newNode = ScoreRect((int)rects[i].width, (int)rects[i].height, method, ref score1, ref score2);

                        if (score1 < bestScore1 || score1 == bestScore1 && score2 < bestScore2)
                        {
                            bestScore1 = score1;
                            bestScore2 = score2;
                            bestNode = newNode;
                            bestRectIndex = i;
                        }
                    }
                }

                if (bestRectIndex == -1)
                    return;

                PlaceRect(bestNode);
                completed[bestRectIndex] = true;
                dst[bestRectIndex] = bestNode;
                remaining--;
                //rects.RemoveAt(bestRectIndex);
            }
        }

        void PlaceRect(Rect node)
        {
            int numRectanglesToProcess = freeRectangles.Count;
            for (int i = 0; i < numRectanglesToProcess; ++i)
            {
                if (SplitFreeNode(freeRectangles[i], ref node))
                {
                    freeRectangles.RemoveAt(i);
                    --i;
                    --numRectanglesToProcess;
                }
            }

            PruneFreeList();

            usedRectangles.Add(node);
        }

        Rect ScoreRect(int width, int height, FreeRectChoiceHeuristic method, ref int score1, ref int score2)
        {
            Rect newNode = new Rect();
            score1 = int.MaxValue;
            score2 = int.MaxValue;
            switch (method)
            {
                case FreeRectChoiceHeuristic.RectBestShortSideFit: newNode = FindPositionForNewNodeBestShortSideFit(width, height, ref score1, ref score2); break;
                case FreeRectChoiceHeuristic.RectBottomLeftRule: newNode = FindPositionForNewNodeBottomLeft(width, height, ref score1, ref score2); break;
                case FreeRectChoiceHeuristic.RectContactPointRule:
                    newNode = FindPositionForNewNodeContactPoint(width, height, ref score1);
                    score1 = -score1; // Reverse since we are minimizing, but for contact point score bigger is better.
                    break;
                case FreeRectChoiceHeuristic.RectBestLongSideFit: newNode = FindPositionForNewNodeBestLongSideFit(width, height, ref score2, ref score1); break;
                case FreeRectChoiceHeuristic.RectBestAreaFit: newNode = FindPositionForNewNodeBestAreaFit(width, height, ref score1, ref score2); break;
            }

            // Cannot fit the current rectangle.
            if (newNode.height == 0)
            {
                score1 = int.MaxValue;
                score2 = int.MaxValue;
            }

            return newNode;
        }

        /// Computes the ratio of used surface area.
        public float Occupancy()
        {
            ulong usedSurfaceArea = 0;
            for (int i = 0; i < usedRectangles.Count; ++i)
                usedSurfaceArea += (uint)usedRectangles[i].width * (uint)usedRectangles[i].height;

            return (float)usedSurfaceArea / (binWidth * binHeight);
        }

        Rect FindPositionForNewNodeBottomLeft(int width, int height, ref int bestY, ref int bestX)
        {
            Rect bestNode = new Rect();
            //memset(bestNode, 0, sizeof(Rect));

            bestY = int.MaxValue;

            for (int i = 0; i < freeRectangles.Count; ++i)
            {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
                {
                    int topSideY = (int)freeRectangles[i].y + height;
                    if (topSideY < bestY || topSideY == bestY && freeRectangles[i].x < bestX)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = width;
                        bestNode.height = height;
                        bestY = topSideY;
                        bestX = (int)freeRectangles[i].x;
                    }
                }
                if (allowRotations && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
                {
                    int topSideY = (int)freeRectangles[i].y + width;
                    if (topSideY < bestY || topSideY == bestY && freeRectangles[i].x < bestX)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = height;
                        bestNode.height = width;
                        bestY = topSideY;
                        bestX = (int)freeRectangles[i].x;
                    }
                }
            }
            return bestNode;
        }

        Rect FindPositionForNewNodeBestShortSideFit(int width, int height, ref int bestShortSideFit, ref int bestLongSideFit)
        {
            Rect bestNode = new Rect();
            //memset(&bestNode, 0, sizeof(Rect));

            bestShortSideFit = int.MaxValue;

            for (int i = 0; i < freeRectangles.Count; ++i)
            {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
                {
                    int leftoverHoriz = Math.Abs((int)freeRectangles[i].width - width);
                    int leftoverVert = Math.Abs((int)freeRectangles[i].height - height);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                    int longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                    if (shortSideFit < bestShortSideFit || shortSideFit == bestShortSideFit && longSideFit < bestLongSideFit)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = width;
                        bestNode.height = height;
                        bestShortSideFit = shortSideFit;
                        bestLongSideFit = longSideFit;
                    }
                }

                if (allowRotations && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
                {
                    int flippedLeftoverHoriz = Math.Abs((int)freeRectangles[i].width - height);
                    int flippedLeftoverVert = Math.Abs((int)freeRectangles[i].height - width);
                    int flippedShortSideFit = Math.Min(flippedLeftoverHoriz, flippedLeftoverVert);
                    int flippedLongSideFit = Math.Max(flippedLeftoverHoriz, flippedLeftoverVert);

                    if (flippedShortSideFit < bestShortSideFit || flippedShortSideFit == bestShortSideFit && flippedLongSideFit < bestLongSideFit)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = height;
                        bestNode.height = width;
                        bestShortSideFit = flippedShortSideFit;
                        bestLongSideFit = flippedLongSideFit;
                    }
                }
            }
            return bestNode;
        }

        Rect FindPositionForNewNodeBestLongSideFit(int width, int height, ref int bestShortSideFit, ref int bestLongSideFit)
        {
            Rect bestNode = new Rect();
            //memset(&bestNode, 0, sizeof(Rect));

            bestLongSideFit = int.MaxValue;

            for (int i = 0; i < freeRectangles.Count; ++i)
            {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
                {
                    int leftoverHoriz = Math.Abs((int)freeRectangles[i].width - width);
                    int leftoverVert = Math.Abs((int)freeRectangles[i].height - height);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                    int longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                    if (longSideFit < bestLongSideFit || longSideFit == bestLongSideFit && shortSideFit < bestShortSideFit)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = width;
                        bestNode.height = height;
                        bestShortSideFit = shortSideFit;
                        bestLongSideFit = longSideFit;
                    }
                }

                if (allowRotations && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
                {
                    int leftoverHoriz = Math.Abs((int)freeRectangles[i].width - height);
                    int leftoverVert = Math.Abs((int)freeRectangles[i].height - width);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                    int longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                    if (longSideFit < bestLongSideFit || longSideFit == bestLongSideFit && shortSideFit < bestShortSideFit)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = height;
                        bestNode.height = width;
                        bestShortSideFit = shortSideFit;
                        bestLongSideFit = longSideFit;
                    }
                }
            }
            return bestNode;
        }

        Rect FindPositionForNewNodeBestAreaFit(int width, int height, ref int bestAreaFit, ref int bestShortSideFit)
        {
            Rect bestNode = new Rect();
            //memset(&bestNode, 0, sizeof(Rect));

            bestAreaFit = int.MaxValue;

            for (int i = 0; i < freeRectangles.Count; ++i)
            {
                int areaFit = (int)freeRectangles[i].width * (int)freeRectangles[i].height - width * height;

                // Try to place the rectangle in upright (non-flipped) orientation.
                if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
                {
                    int leftoverHoriz = Math.Abs((int)freeRectangles[i].width - width);
                    int leftoverVert = Math.Abs((int)freeRectangles[i].height - height);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);

                    if (areaFit < bestAreaFit || areaFit == bestAreaFit && shortSideFit < bestShortSideFit)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = width;
                        bestNode.height = height;
                        bestShortSideFit = shortSideFit;
                        bestAreaFit = areaFit;
                    }
                }

                if (allowRotations && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
                {
                    int leftoverHoriz = Math.Abs((int)freeRectangles[i].width - height);
                    int leftoverVert = Math.Abs((int)freeRectangles[i].height - width);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);

                    if (areaFit < bestAreaFit || areaFit == bestAreaFit && shortSideFit < bestShortSideFit)
                    {
                        bestNode.x = freeRectangles[i].x;
                        bestNode.y = freeRectangles[i].y;
                        bestNode.width = height;
                        bestNode.height = width;
                        bestShortSideFit = shortSideFit;
                        bestAreaFit = areaFit;
                    }
                }
            }
            return bestNode;
        }

        /// Returns 0 if the two intervals i1 and i2 are disjoint, or the length of their overlap otherwise.
        int CommonIntervalLength(int i1start, int i1end, int i2start, int i2end)
        {
            if (i1end < i2start || i2end < i1start)
                return 0;
            return Math.Min(i1end, i2end) - Math.Max(i1start, i2start);
        }

        int ContactPointScoreNode(int x, int y, int width, int height)
        {
            int score = 0;

            if (x == 0 || x + width == binWidth)
                score += height;
            if (y == 0 || y + height == binHeight)
                score += width;

            for (int i = 0; i < usedRectangles.Count; ++i)
            {
                if (usedRectangles[i].x == x + width || usedRectangles[i].x + usedRectangles[i].width == x)
                    score += CommonIntervalLength((int)usedRectangles[i].y, (int)usedRectangles[i].y + (int)usedRectangles[i].height, y, y + height);
                if (usedRectangles[i].y == y + height || usedRectangles[i].y + usedRectangles[i].height == y)
                    score += CommonIntervalLength((int)usedRectangles[i].x, (int)usedRectangles[i].x + (int)usedRectangles[i].width, x, x + width);
            }
            return score;
        }

        Rect FindPositionForNewNodeContactPoint(int width, int height, ref int bestContactScore)
        {
            Rect bestNode = new Rect();
            //memset(&bestNode, 0, sizeof(Rect));

            bestContactScore = -1;

            for (int i = 0; i < freeRectangles.Count; ++i)
            {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
                {
                    int score = ContactPointScoreNode((int)freeRectangles[i].x, (int)freeRectangles[i].y, width, height);
                    if (score > bestContactScore)
                    {
                        bestNode.x = (int)freeRectangles[i].x;
                        bestNode.y = (int)freeRectangles[i].y;
                        bestNode.width = width;
                        bestNode.height = height;
                        bestContactScore = score;
                    }
                }
                if (allowRotations && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
                {
                    int score = ContactPointScoreNode((int)freeRectangles[i].x, (int)freeRectangles[i].y, height, width);
                    if (score > bestContactScore)
                    {
                        bestNode.x = (int)freeRectangles[i].x;
                        bestNode.y = (int)freeRectangles[i].y;
                        bestNode.width = height;
                        bestNode.height = width;
                        bestContactScore = score;
                    }
                }
            }
            return bestNode;
        }

        bool SplitFreeNode(Rect freeNode, ref Rect usedNode)
        {
            // Test with SAT if the rectangles even intersect.
            if (usedNode.x >= freeNode.x + freeNode.width || usedNode.x + usedNode.width <= freeNode.x ||
                usedNode.y >= freeNode.y + freeNode.height || usedNode.y + usedNode.height <= freeNode.y)
                return false;

            if (usedNode.x < freeNode.x + freeNode.width && usedNode.x + usedNode.width > freeNode.x)
            {
                // New node at the top side of the used node.
                if (usedNode.y > freeNode.y && usedNode.y < freeNode.y + freeNode.height)
                {
                    Rect newNode = freeNode;
                    newNode.height = usedNode.y - newNode.y;
                    freeRectangles.Add(newNode);
                }

                // New node at the bottom side of the used node.
                if (usedNode.y + usedNode.height < freeNode.y + freeNode.height)
                {
                    Rect newNode = freeNode;
                    newNode.y = usedNode.y + usedNode.height;
                    newNode.height = freeNode.y + freeNode.height - (usedNode.y + usedNode.height);
                    freeRectangles.Add(newNode);
                }
            }

            if (usedNode.y < freeNode.y + freeNode.height && usedNode.y + usedNode.height > freeNode.y)
            {
                // New node at the left side of the used node.
                if (usedNode.x > freeNode.x && usedNode.x < freeNode.x + freeNode.width)
                {
                    Rect newNode = freeNode;
                    newNode.width = usedNode.x - newNode.x;
                    freeRectangles.Add(newNode);
                }

                // New node at the right side of the used node.
                if (usedNode.x + usedNode.width < freeNode.x + freeNode.width)
                {
                    Rect newNode = freeNode;
                    newNode.x = usedNode.x + usedNode.width;
                    newNode.width = freeNode.x + freeNode.width - (usedNode.x + usedNode.width);
                    freeRectangles.Add(newNode);
                }
            }

            return true;
        }

        void PruneFreeList()
        {
            for (int i = 0; i < freeRectangles.Count; ++i)
                for (int j = i + 1; j < freeRectangles.Count; ++j)
                {
                    if (IsContainedIn(freeRectangles[i], freeRectangles[j]))
                    {
                        freeRectangles.RemoveAt(i);
                        --i;
                        break;
                    }
                    if (IsContainedIn(freeRectangles[j], freeRectangles[i]))
                    {
                        freeRectangles.RemoveAt(j);
                        --j;
                    }
                }
        }

        bool IsContainedIn(Rect a, Rect b)
        {
            return a.x >= b.x && a.y >= b.y
                && a.x + a.width <= b.x + b.width
                && a.y + a.height <= b.y + b.height;
        }

    }
}
