using System;
using System.Collections.Generic;
using static Prowl.Scribe.Internal.Common;

namespace Prowl.Scribe.Internal
{
    public class FontInfo
    {
        private Buf cff = null;
        private Buf charstrings = null;
        private FakePtr<byte> data;
        private Buf fdselect = null;
        private Buf fontdicts = null;
        private int fontstart;
        private int glyf;
        private int gpos;
        private Buf gsubrs = null;
        private int head;
        private int hhea;
        private int hmtx;
        private int index_map;
        private int indexToLocFormat;
        private int kern;
        private int loca;
        private int numGlyphs;
        private Buf subrs = null;

        private readonly Dictionary<int, int> unicodeMapCache = new Dictionary<int, int>();

        public string FamilyName { get; internal set; } = string.Empty;
        public FontStyle Style { get; internal set; } = FontStyle.Regular;

        public int Ascent { get; private set; } = 0;
        public int Descent { get; private set; } = 0;
        public int Linegap { get; private set; } = 0;

        internal int InitFont(byte[] data, int fontstart)
		{
			uint cmap = 0;
			uint t = 0;
			var i = 0;
			var numTables = 0;
			var ptr = new FakePtr<byte>(data);
			this.data = ptr;
			this.fontstart = fontstart;
			this.cff = new Buf(FakePtr<byte>.Null, 0);
			cmap = stbtt__find_table(ptr, (uint)fontstart, "cmap");
			this.loca = (int)stbtt__find_table(ptr, (uint)fontstart, "loca");
			this.head = (int)stbtt__find_table(ptr, (uint)fontstart, "head");
			this.glyf = (int)stbtt__find_table(ptr, (uint)fontstart, "glyf");
			this.hhea = (int)stbtt__find_table(ptr, (uint)fontstart, "hhea");
			this.hmtx = (int)stbtt__find_table(ptr, (uint)fontstart, "hmtx");
			this.kern = (int)stbtt__find_table(ptr, (uint)fontstart, "kern");
			this.gpos = (int)stbtt__find_table(ptr, (uint)fontstart, "GPOS");
			if (cmap == 0 || this.head == 0 || this.hhea == 0 || this.hmtx == 0)
				return 0;
			if (this.glyf != 0)
			{
				if (this.loca == 0)
					return 0;
			}
			else
			{
				Buf b = null;
				Buf topdict = null;
				Buf topdictidx = null;
				var cstype = (uint)2;
				var charstrings = (uint)0;
				var fdarrayoff = (uint)0;
				var fdselectoff = (uint)0;
				uint cff = 0;
				cff = stbtt__find_table(ptr, (uint)fontstart, "CFF ");
				if (cff == 0)
					return 0;
				this.fontdicts = new Buf(FakePtr<byte>.Null, 0);
				this.fdselect = new Buf(FakePtr<byte>.Null, 0);
				this.cff = new Buf(new FakePtr<byte>(ptr, (int)cff), 512 * 1024 * 1024);
				b = this.cff;
				b.stbtt__buf_skip(2);
				b.stbtt__buf_seek(b.stbtt__buf_get8());
				b.stbtt__cff_get_index();
				topdictidx = b.stbtt__cff_get_index();
				topdict = topdictidx.stbtt__cff_index_get(0);
				b.stbtt__cff_get_index();
				this.gsubrs = b.stbtt__cff_get_index();
				topdict.stbtt__dict_get_ints(17, out charstrings);
				topdict.stbtt__dict_get_ints(0x100 | 6, out cstype);
				topdict.stbtt__dict_get_ints(0x100 | 36, out fdarrayoff);
				topdict.stbtt__dict_get_ints(0x100 | 37, out fdselectoff);
				this.subrs = Buf.stbtt__get_subrs(b, topdict);

				if (cstype != 2)
					return 0;
				if (charstrings == 0)
					return 0;
				if (fdarrayoff != 0)
				{
					if (fdselectoff == 0)
						return 0;
					b.stbtt__buf_seek((int)fdarrayoff);
					this.fontdicts = b.stbtt__cff_get_index();
					this.fdselect = b.stbtt__buf_range((int)fdselectoff, (int)(b.size - fdselectoff));
				}

				b.stbtt__buf_seek((int)charstrings);
				this.charstrings = b.stbtt__cff_get_index();
			}

			t = stbtt__find_table(ptr, (uint)fontstart, "maxp");
			if (t != 0)
				this.numGlyphs = ttUSHORT(ptr + t + 4);
			else
				this.numGlyphs = 0xffff;
			numTables = ttUSHORT(ptr + cmap + 2);
			this.index_map = 0;
			for (i = 0; i < numTables; ++i)
			{
				var encoding_record = (uint)(cmap + 4 + 8 * i);
				switch (ttUSHORT(ptr + encoding_record))
				{
					case STBTT_PLATFORM_ID_MICROSOFT:
						switch (ttUSHORT(ptr + encoding_record + 2))
						{
							case STBTT_MS_EID_UNICODE_BMP:
							case STBTT_MS_EID_UNICODE_FULL:
								this.index_map = (int)(cmap + ttULONG(ptr + encoding_record + 4));
								break;
						}

						break;
					case STBTT_PLATFORM_ID_UNICODE:
						this.index_map = (int)(cmap + ttULONG(ptr + encoding_record + 4));
						break;
				}
			}

			if (this.index_map == 0)
				return 0;
			this.indexToLocFormat = ttUSHORT(ptr + this.head + 50);

            GetFontVerticalMetrics(out int a, out int d, out int l);
            Ascent = a;
            Descent = d;
            Linegap = l;

            return 1;
		}

		public int FindGlyphIndex(int codepoint)
		{
			if (unicodeMapCache.TryGetValue(codepoint, out var cached))
				return cached;

			// 0 by default
            unicodeMapCache[codepoint] = 0;

            int glyphIndex = 0;

            var data = this.data;
			var index_map = (uint)this.index_map;
			var format = ttUSHORT(data + index_map + 0);
			if (format == 0)
			{
				var bytes = (int)ttUSHORT(data + index_map + 2);
				if (codepoint < bytes - 6)
                {
					glyphIndex = data[index_map + 6 + codepoint];
                    unicodeMapCache[codepoint] = glyphIndex;
                    return glyphIndex;
				}
                return 0;
			}

			if (format == 6)
			{
				var first = (uint)ttUSHORT(data + index_map + 6);
				var count = (uint)ttUSHORT(data + index_map + 8);
				if ((uint)codepoint >= first && (uint)codepoint < first + count)
				{
                    glyphIndex = ttUSHORT(data + index_map + 10 + (codepoint - first) * 2);
                    unicodeMapCache[codepoint] = glyphIndex;
                    return glyphIndex;
                }
                return 0;
			}

			if (format == 2)
                return 0;

			if (format == 4)
			{
				var segcount = (ushort)(ttUSHORT(data + index_map + 6) >> 1);
				var searchRange = (ushort)(ttUSHORT(data + index_map + 8) >> 1);
				var entrySelector = ttUSHORT(data + index_map + 10);
				var rangeShift = (ushort)(ttUSHORT(data + index_map + 12) >> 1);
				var endCount = index_map + 14;
				var search = endCount;
				if (codepoint > 0xffff)
                    return 0;
				if (codepoint >= ttUSHORT(data + search + rangeShift * 2))
					search += (uint)(rangeShift * 2);
				search -= 2;
				while (entrySelector != 0)
				{
					ushort end = 0;
					searchRange >>= 1;
					end = ttUSHORT(data + search + searchRange * 2);
					if (codepoint > end)
						search += (uint)(searchRange * 2);
					--entrySelector;
				}

				search += 2;
				{
					ushort offset = 0;
					ushort start = 0;
					var item = (ushort)((search - endCount) >> 1);
					start = ttUSHORT(data + index_map + 14 + segcount * 2 + 2 + 2 * item);
					if (codepoint < start)
                        return 0;
					offset = ttUSHORT(data + index_map + 14 + segcount * 6 + 2 + 2 * item);
					if (offset == 0)
					{
                        glyphIndex = (ushort)(codepoint + ttSHORT(data + index_map + 14 + segcount * 4 + 2 + 2 * item));
                        unicodeMapCache[codepoint] = glyphIndex;
                        return glyphIndex;
                    }
                    glyphIndex = ttUSHORT(data + offset + (codepoint - start) * 2 + index_map + 14 + segcount * 6 + 2 + 2 * item);
                    unicodeMapCache[codepoint] = glyphIndex;
                    return glyphIndex;
                }
			}

			if (format == 12 || format == 13)
			{
				var ngroups = ttULONG(data + index_map + 12);
				var low = 0;
				var high = 0;
				low = 0;
				high = (int)ngroups;
				while (low < high)
				{
					var mid = low + ((high - low) >> 1);
					var start_char = ttULONG(data + index_map + 16 + mid * 12);
					var end_char = ttULONG(data + index_map + 16 + mid * 12 + 4);
					if ((uint)codepoint < start_char)
					{
						high = mid;
					}
					else if ((uint)codepoint > end_char)
					{
						low = mid + 1;
					}
					else
					{
						var start_glyph = ttULONG(data + index_map + 16 + mid * 12 + 8);
						if (format == 12)
						{
                            glyphIndex = (int)(start_glyph + codepoint - start_char);
                            unicodeMapCache[codepoint] = glyphIndex;
                            return glyphIndex;
                        }
                        glyphIndex = (int)start_glyph;
                        unicodeMapCache[codepoint] = glyphIndex;
                        return glyphIndex;
                    }
				}

                return 0;
			}

            return 0;
		}

		public int GetGlyphBox(int glyph_index, ref int x0, ref int y0, ref int x1, ref int y1)
		{
			if (this.cff.size != 0)
			{
				GetGlyphInfoT2(glyph_index, ref x0, ref y0, ref x1, ref y1);
			}
			else
			{
				var g = GetGlyfOffset(glyph_index);
				if (g < 0)
					return 0;
				x0 = ttSHORT(this.data + g + 2);
				y0 = ttSHORT(this.data + g + 4);
				x1 = ttSHORT(this.data + g + 6);
				y1 = ttSHORT(this.data + g + 8);
			}

			return 1;
		}

		public int IsGlyphEmpty(int glyph_index)
		{
			short numberOfContours = 0;
			var g = 0;

			int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
			if (this.cff.size != 0)
				return GetGlyphInfoT2(glyph_index, ref x0, ref y0, ref x1, ref y1) == 0 ? 1 : 0;
			g = GetGlyfOffset(glyph_index);
			if (g < 0)
				return 1;
			numberOfContours = ttSHORT(this.data + g);
			return numberOfContours == 0 ? 1 : 0;
		}

		public int GetGlyphShape(int glyph_index, out GlyphVertex[] pvertices)
		{
			if (this.cff.size == 0)
				return GetGlyphShapeTT(glyph_index, out pvertices);
			return GetGlyphShapeT2(glyph_index, out pvertices);
		}

		public int GetGlyphKerningAdvance(int g1, int g2)
		{
            //if (this.gpos != 0)
            //{
            //    int xAdvance = stbtt__GetGlyphGPOSInfoAdvance(g1, g2);
            //    if (xAdvance != 0) return xAdvance;
            //}
            //
            //if (this.kern != 0)
            //    return stbtt__GetGlyphKernInfoAdvance(g1, g2);
            //
            //return 0;

            var xAdvance = 0;
			if (this.gpos != 0)
				xAdvance += GetGlyphGPOSInfoAdvance(g1, g2);
			else if (this.kern != 0)
				xAdvance += GetGlyphKernInfoAdvance(g1, g2);
			return xAdvance;
		}

        public void GetGlyphHorizontalMetrics(int glyph_index, ref int advanceWidth, ref int leftSideBearing)
        {
            var numOfLongHorMetrics = ttUSHORT(this.data + this.hhea + 34);
            if (glyph_index < numOfLongHorMetrics)
            {
                advanceWidth = ttSHORT(this.data + this.hmtx + 4 * glyph_index);
                leftSideBearing = ttSHORT(this.data + this.hmtx + 4 * glyph_index + 2);
            }
            else
            {
                advanceWidth = ttSHORT(this.data + this.hmtx + 4 * (numOfLongHorMetrics - 1));
                leftSideBearing = ttSHORT(this.data + this.hmtx + 4 * numOfLongHorMetrics +
                                          2 * (glyph_index - numOfLongHorMetrics));
            }
        }

		public void GetFontBoundingBox(ref int x0, ref int y0, ref int x1, ref int y1)
		{
			x0 = ttSHORT(this.data + this.head + 36);
			y0 = ttSHORT(this.data + this.head + 38);
			x1 = ttSHORT(this.data + this.head + 40);
			y1 = ttSHORT(this.data + this.head + 42);
		}

		public float ScaleForPixelHeight(float height)
		{
			var fheight = ttSHORT(this.data + this.hhea + 4) - ttSHORT(this.data + this.hhea + 6);
			return height / fheight;
		}

		public float ScaleForMappingEmToPixels(float pixels)
		{
			var unitsPerEm = (int)ttUSHORT(this.data + this.head + 18);
			return pixels / unitsPerEm;
		}

        public void GetGlyphBitmapBoundingBox(int glyph, float scale_x, float scale_y, ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			var x0 = 0; var y0 = 0; var x1 = 0; var y1 = 0;
			if (GetGlyphBox(glyph, ref x0, ref y0, ref x1, ref y1) == 0)
			{
				ix0 = iy0 = ix1 = iy1 = 0;
			}
			else
			{
				ix0 = (int)Math.Floor(x0 * scale_x);
				iy0 = (int)Math.Floor(-y1 * scale_y);
				ix1 = (int)Math.Ceiling(x1 * scale_x);
				iy1 = (int)Math.Ceiling(-y0 * scale_y);
			}
        }

        public FakePtr<byte> GetGlyphBitmap(float scale_x, float scale_y, int glyph, ref int width, ref int height, ref int xoff, ref int yoff)
		{
			var ix0 = 0;
			var iy0 = 0;
			var ix1 = 0;
			var iy1 = 0;
			var gbm = new Bitmap();
			GlyphVertex[] vertices;
			var num_verts = GetGlyphShape(glyph, out vertices);
			if (scale_x == 0)
				scale_x = scale_y;
			if (scale_y == 0)
			{
				if (scale_x == 0)
					return FakePtr<byte>.Null;
				scale_y = scale_x;
			}

			GetGlyphBitmapBoundingBox(glyph, scale_x, scale_y, ref ix0, ref iy0, ref ix1, ref iy1);
			gbm.w = ix1 - ix0;
			gbm.h = iy1 - iy0;
			width = gbm.w;
			height = gbm.h;
			xoff = ix0;
			yoff = iy0;
			if (gbm.w != 0 && gbm.h != 0)
			{
				gbm.pixels = FakePtr<byte>.CreateWithSize(gbm.w * gbm.h);
				gbm.stride = gbm.w;
				gbm.Rasterize(0.35f, vertices, num_verts, scale_x, scale_y, ix0, iy0, 1);
			}

			return gbm.pixels;
		}

        public void MakeGlyphBitmap(FakePtr<byte> output, int out_w, int out_h, int out_stride, float scale_x, float scale_y, int glyph)
		{
			var ix0 = 0;
			var iy0 = 0;
			var ix1 = 0;
			var iy1 = 0;
			var num_verts = GetGlyphShape(glyph, out GlyphVertex[] vertices);
			var gbm = new Bitmap();
			GetGlyphBitmapBoundingBox(glyph, scale_x, scale_y, ref ix0, ref iy0, ref ix1, ref iy1);
			gbm.pixels = output;
			gbm.w = out_w;
			gbm.h = out_h;
			gbm.stride = out_stride;

			if (gbm.w != 0 && gbm.h != 0)
				gbm.Rasterize(0.35f, vertices, num_verts, scale_x, scale_y, ix0, iy0, 1);
		}

		public FakePtr<byte> GetFontNameString(FontInfo font, ref int length, int platformID, int encodingID, int languageID, int nameID)
		{
			var offset = (uint)font.fontstart;
			var nm = stbtt__find_table(font.data, offset, "name");
			if (nm == 0)
				return FakePtr<byte>.Null;
            int count = ttUSHORT(font.data + nm + 2);
            int stringOffset = (int)(nm + ttUSHORT(font.data + nm + 4));
			for (int i = 0; i < count; ++i)
			{
				var loc = (uint)(nm + 6 + 12 * i);
				if (platformID == ttUSHORT(font.data + loc + 0) && encodingID == ttUSHORT(font.data + loc + 2) &&
					languageID == ttUSHORT(font.data + loc + 4) && nameID == ttUSHORT(font.data + loc + 6))
				{
					length = ttUSHORT(font.data + loc + 8);
					return font.data + stringOffset + ttUSHORT(font.data + loc + 10);
				}
			}

			return FakePtr<byte>.Null;
		}


        #region Private Methods

        private void GetFontVerticalMetrics(out int ascent, out int descent, out int lineGap)
        {
            ascent = ttSHORT(this.data + this.hhea + 4);
            descent = ttSHORT(this.data + this.hhea + 6);
            lineGap = ttSHORT(this.data + this.hhea + 8);
        }

        private int GetFontVerticalMetricsOS2(ref int typoAscent, ref int typoDescent, ref int typoLineGap)
        {
            var tab = (int)stbtt__find_table(this.data, (uint)this.fontstart, "OS/2");
            if (tab == 0)
                return 0;
            typoAscent = ttSHORT(this.data + tab + 68);
            typoDescent = ttSHORT(this.data + tab + 70);
            typoLineGap = ttSHORT(this.data + tab + 72);
            return 1;
        }

        private int GetGlyfOffset(int glyph_index)
        {
            var g1 = 0;
            var g2 = 0;
            if (glyph_index >= this.numGlyphs)
                return -1;
            if (this.indexToLocFormat >= 2)
                return -1;
            if (this.indexToLocFormat == 0)
            {
                g1 = this.glyf + ttUSHORT(this.data + this.loca + glyph_index * 2) * 2;
                g2 = this.glyf + ttUSHORT(this.data + this.loca + glyph_index * 2 + 2) * 2;
            }
            else
            {
                g1 = (int)(this.glyf + ttULONG(this.data + this.loca + glyph_index * 4));
                g2 = (int)(this.glyf + ttULONG(this.data + this.loca + glyph_index * 4 + 4));
            }

            return g1 == g2 ? -1 : g1;
        }

        private int GetGlyphShapeTT(int glyph_index, out GlyphVertex[] pvertices)
        {
            short numberOfContours = 0;
            FakePtr<byte> endPtsOfContours;
            var data = this.data;
            GlyphVertex[] vertices = null;
            var num_vertices = 0;
            var g = GetGlyfOffset(glyph_index);
            pvertices = null;
            if (g < 0)
                return 0;
            numberOfContours = ttSHORT(data + g);
            if (numberOfContours > 0)
            {
                var flags = (byte)0;
                byte flagcount = 0;
                var ins = 0;
                var i = 0;
                var j = 0;
                var m = 0;
                var n = 0;
                var next_move = 0;
                var was_off = 0;
                var off = 0;
                var start_off = 0;
                var x = 0;
                var y = 0;
                var cx = 0;
                var cy = 0;
                var sx = 0;
                var sy = 0;
                var scx = 0;
                var scy = 0;
                FakePtr<byte> points;
                endPtsOfContours = data + g + 10;
                ins = ttUSHORT(data + g + 10 + numberOfContours * 2);
                points = data + g + 10 + numberOfContours * 2 + 2 + ins;
                n = 1 + ttUSHORT(endPtsOfContours + numberOfContours * 2 - 2);
                m = n + 2 * numberOfContours;
                vertices = new GlyphVertex[m];
                next_move = 0;
                flagcount = 0;
                off = m - n;
                for (i = 0; i < n; ++i)
                {
                    if (flagcount == 0)
                    {
                        flags = points.GetAndIncrease();
                        if ((flags & 8) != 0)
                            flagcount = points.GetAndIncrease();
                    }
                    else
                    {
                        --flagcount;
                    }

                    vertices[off + i].type = flags;
                }

                x = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    if ((flags & 2) != 0)
                    {
                        var dx = (short)points.GetAndIncrease();
                        x += (flags & 16) != 0 ? dx : -dx;
                    }
                    else
                    {
                        if ((flags & 16) == 0)
                        {
                            x = x + (short)(points[0] * 256 + points[1]);
                            points += 2;
                        }
                    }

                    vertices[off + i].x = (short)x;
                }

                y = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    if ((flags & 4) != 0)
                    {
                        var dy = (short)points.GetAndIncrease();
                        y += (flags & 32) != 0 ? dy : -dy;
                    }
                    else
                    {
                        if ((flags & 32) == 0)
                        {
                            y = y + (short)(points[0] * 256 + points[1]);
                            points += 2;
                        }
                    }

                    vertices[off + i].y = (short)y;
                }

                num_vertices = 0;
                sx = sy = cx = cy = scx = scy = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    x = vertices[off + i].x;
                    y = vertices[off + i].y;
                    if (next_move == i)
                    {
                        if (i != 0)
                            num_vertices = stbtt__close_shape(vertices, num_vertices, was_off, start_off, sx, sy, scx,
                                scy, cx, cy);
                        start_off = (flags & 1) != 0 ? 0 : 1;
                        if (start_off != 0)
                        {
                            scx = x;
                            scy = y;
                            if ((vertices[off + i + 1].type & 1) == 0)
                            {
                                sx = (x + vertices[off + i + 1].x) >> 1;
                                sy = (y + vertices[off + i + 1].y) >> 1;
                            }
                            else
                            {
                                sx = vertices[off + i + 1].x;
                                sy = vertices[off + i + 1].y;
                                ++i;
                            }
                        }
                        else
                        {
                            sx = x;
                            sy = y;
                        }

                        stbtt_setvertex(ref vertices[num_vertices++], STBTT_vmove, sx, sy, 0, 0);
                        was_off = 0;
                        next_move = 1 + ttUSHORT(endPtsOfContours + j * 2);
                        ++j;
                    }
                    else
                    {
                        if ((flags & 1) == 0)
                        {
                            if (was_off != 0)
                                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, (cx + x) >> 1,
                                    (cy + y) >> 1, cx, cy);
                            cx = x;
                            cy = y;
                            was_off = 1;
                        }
                        else
                        {
                            if (was_off != 0)
                                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, x, y, cx, cy);
                            else
                                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vline, x, y, 0, 0);
                            was_off = 0;
                        }
                    }
                }

                num_vertices = stbtt__close_shape(vertices, num_vertices, was_off, start_off, sx, sy, scx, scy, cx, cy);
            }
            else if (numberOfContours < 0)
            {
                var more = 1;
                var comp = data + g + 10;
                num_vertices = 0;
                vertices = null;
                while (more != 0)
                {
                    ushort flags = 0;
                    ushort gidx = 0;
                    var comp_num_verts = 0;
                    var i = 0;
                    GlyphVertex[] comp_verts;
                    GlyphVertex[] tmp;
                    var mtx = new float[6];
                    mtx[0] = 1;
                    mtx[1] = 0;
                    mtx[2] = 0;
                    mtx[3] = 1;
                    mtx[4] = 0;
                    mtx[5] = 0;
                    float m = 0;
                    float n = 0;
                    flags = (ushort)ttSHORT(comp);
                    comp += 2;
                    gidx = (ushort)ttSHORT(comp);
                    comp += 2;
                    if ((flags & 2) != 0)
                    {
                        if ((flags & 1) != 0)
                        {
                            mtx[4] = ttSHORT(comp);
                            comp += 2;
                            mtx[5] = ttSHORT(comp);
                            comp += 2;
                        }
                        else
                        {
                            mtx[4] = (sbyte)comp.Value;
                            comp += 1;
                            mtx[5] = (sbyte)comp.Value;
                            comp += 1;
                        }
                    }

                    if ((flags & (1 << 3)) != 0)
                    {
                        mtx[0] = mtx[3] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                        mtx[1] = mtx[2] = 0;
                    }
                    else if ((flags & (1 << 6)) != 0)
                    {
                        mtx[0] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                        mtx[1] = mtx[2] = 0;
                        mtx[3] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                    }
                    else if ((flags & (1 << 7)) != 0)
                    {
                        mtx[0] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                        mtx[1] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                        mtx[2] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                        mtx[3] = ttSHORT(comp) / 16384.0f;
                        comp += 2;
                    }

                    m = (float)Math.Sqrt(mtx[0] * mtx[0] + mtx[1] * mtx[1]);
                    n = (float)Math.Sqrt(mtx[2] * mtx[2] + mtx[3] * mtx[3]);
                    comp_num_verts = GetGlyphShape(gidx, out comp_verts);
                    if (comp_num_verts > 0)
                    {
                        for (i = 0; i < comp_num_verts; ++i)
                        {
                            short x = 0;
                            short y = 0;
                            x = comp_verts[i].x;
                            y = comp_verts[i].y;
                            comp_verts[i].x = (short)(mtx[0] * x + mtx[2] * y + mtx[4] * m);
                            comp_verts[i].y = (short)(mtx[1] * x + mtx[3] * y + mtx[5] * n);
                            x = comp_verts[i].cx;
                            y = comp_verts[i].cy;
                            comp_verts[i].cx = (short)(mtx[0] * x + mtx[2] * y + mtx[4] * m);
                            comp_verts[i].cy = (short)(mtx[1] * x + mtx[3] * y + mtx[5] * n);

                            //v->x = (stbtt_vertex_type)(mtx[0] * x + mtx[2] * y + mtx[4] * m);
                            //v->y = (stbtt_vertex_type)(mtx[1] * x + mtx[3] * y + mtx[5] * n);
                            //x = v->cx; y = v->cy;
                            //v->cx = (stbtt_vertex_type)(mtx[0] * x + mtx[2] * y + mtx[4] * m);
                            //v->cy = (stbtt_vertex_type)(mtx[1] * x + mtx[3] * y + mtx[5] * n);
                        }

                        tmp = new GlyphVertex[num_vertices + comp_num_verts];
                        if (num_vertices > 0)
                            Array.Copy(vertices, tmp, num_vertices);

                        Array.Copy(comp_verts, 0, tmp, num_vertices, comp_num_verts);
                        vertices = tmp;
                        num_vertices += comp_num_verts;
                    }

                    more = flags & (1 << 5);
                }
            }

            pvertices = vertices;
            return num_vertices;
        }

        private Buf CidGetGlyphSubrs(int glyph_index)
        {
            var fdselect = this.fdselect;
            var nranges = 0;
            var start = 0;
            var end = 0;
            var v = 0;
            var fmt = 0;
            var fdselector = -1;
            var i = 0;
            fdselect.stbtt__buf_seek(0);
            fmt = fdselect.stbtt__buf_get8();
            if (fmt == 0)
            {
                fdselect.stbtt__buf_skip(glyph_index);
                fdselector = fdselect.stbtt__buf_get8();
            }
            else if (fmt == 3)
            {
                nranges = (int)fdselect.stbtt__buf_get(2);
                start = (int)fdselect.stbtt__buf_get(2);
                for (i = 0; i < nranges; i++)
                {
                    v = fdselect.stbtt__buf_get8();
                    end = (int)fdselect.stbtt__buf_get(2);
                    if (glyph_index >= start && glyph_index < end)
                    {
                        fdselector = v;
                        break;
                    }

                    start = end;
                }
            }

            if (fdselector == -1)
                new Buf(FakePtr<byte>.Null, 0);
            return Buf.stbtt__get_subrs(this.cff, fontdicts.stbtt__cff_index_get(fdselector));
        }

        private int RunCharstring(int glyph_index, CharStringContext c)
        {
            var in_header = 1;
            var maskbits = 0;
            var subr_stack_height = 0;
            var sp = 0;
            var v = 0;
            var i = 0;
            var b0 = 0;
            var has_subrs = 0;
            var clear_stack = 0;
            var s = new float[48];
            var subr_stack = new Buf[10];
            for (i = 0; i < subr_stack.Length; ++i)
                subr_stack[i] = null;

            var subrs = this.subrs;
            float f = 0;
            var b = this.charstrings.stbtt__cff_index_get(glyph_index);
            while (b.cursor < b.size)
            {
                i = 0;
                clear_stack = 1;
                b0 = b.stbtt__buf_get8();
                switch (b0)
                {
                    case 0x13:
                    case 0x14:
                        if (in_header != 0)
                            maskbits += sp / 2;
                        in_header = 0;
                        b.stbtt__buf_skip((maskbits + 7) / 8);
                        break;
                    case 0x01:
                    case 0x03:
                    case 0x12:
                    case 0x17:
                        maskbits += sp / 2;
                        break;
                    case 0x15:
                        in_header = 0;
                        if (sp < 2)
                            return 0;
                        c.stbtt__csctx_rmove_to(s[sp - 2], s[sp - 1]);
                        break;
                    case 0x04:
                        in_header = 0;
                        if (sp < 1)
                            return 0;
                        c.stbtt__csctx_rmove_to(0, s[sp - 1]);
                        break;
                    case 0x16:
                        in_header = 0;
                        if (sp < 1)
                            return 0;
                        c.stbtt__csctx_rmove_to(s[sp - 1], 0);
                        break;
                    case 0x05:
                        if (sp < 2)
                            return 0;
                        for (; i + 1 < sp; i += 2)
                            c.stbtt__csctx_rline_to(s[i], s[i + 1]);
                        break;
                    case 0x07:
                    case 0x06:
                        if (sp < 1)
                            return 0;
                        var goto_vlineto = b0 == 0x07 ? 1 : 0;
                        for (; ; )
                        {
                            if (goto_vlineto == 0)
                            {
                                if (i >= sp)
                                    break;
                                c.stbtt__csctx_rline_to(s[i], 0);
                                i++;
                            }

                            goto_vlineto = 0;
                            if (i >= sp)
                                break;
                            c.stbtt__csctx_rline_to(0, s[i]);
                            i++;
                        }

                        break;
                    case 0x1F:
                    case 0x1E:
                        if (sp < 4)
                            return 0;
                        var goto_hvcurveto = b0 == 0x1F ? 1 : 0;
                        for (; ; )
                        {
                            if (goto_hvcurveto == 0)
                            {
                                if (i + 3 >= sp)
                                    break;
                                c.stbtt__csctx_rccurve_to(0, s[i], s[i + 1], s[i + 2], s[i + 3],
                                    sp - i == 5 ? s[i + 4] : 0.0f);
                                i += 4;
                            }

                            goto_hvcurveto = 0;
                            if (i + 3 >= sp)
                                break;
                            c.stbtt__csctx_rccurve_to(s[i], 0, s[i + 1], s[i + 2], sp - i == 5 ? s[i + 4] : 0.0f,
                                s[i + 3]);
                            i += 4;
                        }

                        break;
                    case 0x08:
                        if (sp < 6)
                            return 0;
                        for (; i + 5 < sp; i += 6)
                            c.stbtt__csctx_rccurve_to(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                        break;
                    case 0x18:
                        if (sp < 8)
                            return 0;
                        for (; i + 5 < sp - 2; i += 6)
                            c.stbtt__csctx_rccurve_to(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                        if (i + 1 >= sp)
                            return 0;
                        c.stbtt__csctx_rline_to(s[i], s[i + 1]);
                        break;
                    case 0x19:
                        if (sp < 8)
                            return 0;
                        for (; i + 1 < sp - 6; i += 2)
                            c.stbtt__csctx_rline_to(s[i], s[i + 1]);
                        if (i + 5 >= sp)
                            return 0;
                        c.stbtt__csctx_rccurve_to(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                        break;
                    case 0x1A:
                    case 0x1B:
                        if (sp < 4)
                            return 0;
                        f = (float)0.0;
                        if ((sp & 1) != 0)
                        {
                            f = s[i];
                            i++;
                        }

                        for (; i + 3 < sp; i += 4)
                        {
                            if (b0 == 0x1B)
                                c.stbtt__csctx_rccurve_to(s[i], f, s[i + 1], s[i + 2], s[i + 3], (float)0.0);
                            else
                                c.stbtt__csctx_rccurve_to(f, s[i], s[i + 1], s[i + 2], (float)0.0, s[i + 3]);
                            f = (float)0.0;
                        }

                        break;
                    case 0x0A:
                    case 0x1D:
                        if (b0 == 0x0A)
                            if (has_subrs == 0)
                            {
                                if (this.fdselect.size != 0)
                                    subrs = CidGetGlyphSubrs(glyph_index);
                                has_subrs = 1;
                            }

                        if (sp < 1)
                            return 0;
                        v = (int)s[--sp];
                        if (subr_stack_height >= 10)
                            return 0;
                        subr_stack[subr_stack_height++] = b;
                        b = b0 == 0x0A ? subrs.stbtt__get_subr(v) : this.gsubrs.stbtt__get_subr(v);
                        if (b.size == 0)
                            return 0;
                        b.cursor = 0;
                        clear_stack = 0;
                        break;
                    case 0x0B:
                        if (subr_stack_height <= 0)
                            return 0;
                        b = subr_stack[--subr_stack_height];
                        clear_stack = 0;
                        break;
                    case 0x0E:
                        c.stbtt__csctx_close_shape();
                        return 1;
                    case 0x0C:
                    {
                        float dx1 = 0;
                        float dx2 = 0;
                        float dx3 = 0;
                        float dx4 = 0;
                        float dx5 = 0;
                        float dx6 = 0;
                        float dy1 = 0;
                        float dy2 = 0;
                        float dy3 = 0;
                        float dy4 = 0;
                        float dy5 = 0;
                        float dy6 = 0;
                        float dx = 0;
                        float dy = 0;
                        var b1 = (int)b.stbtt__buf_get8();
                        switch (b1)
                        {
                            case 0x22:
                                if (sp < 7)
                                    return 0;
                                dx1 = s[0];
                                dx2 = s[1];
                                dy2 = s[2];
                                dx3 = s[3];
                                dx4 = s[4];
                                dx5 = s[5];
                                dx6 = s[6];
                                c.stbtt__csctx_rccurve_to(dx1, 0, dx2, dy2, dx3, 0);
                                c.stbtt__csctx_rccurve_to(dx4, 0, dx5, -dy2, dx6, 0);
                                break;
                            case 0x23:
                                if (sp < 13)
                                    return 0;
                                dx1 = s[0];
                                dy1 = s[1];
                                dx2 = s[2];
                                dy2 = s[3];
                                dx3 = s[4];
                                dy3 = s[5];
                                dx4 = s[6];
                                dy4 = s[7];
                                dx5 = s[8];
                                dy5 = s[9];
                                dx6 = s[10];
                                dy6 = s[11];
                                c.stbtt__csctx_rccurve_to(dx1, dy1, dx2, dy2, dx3, dy3);
                                c.stbtt__csctx_rccurve_to(dx4, dy4, dx5, dy5, dx6, dy6);
                                break;
                            case 0x24:
                                if (sp < 9)
                                    return 0;
                                dx1 = s[0];
                                dy1 = s[1];
                                dx2 = s[2];
                                dy2 = s[3];
                                dx3 = s[4];
                                dx4 = s[5];
                                dx5 = s[6];
                                dy5 = s[7];
                                dx6 = s[8];
                                c.stbtt__csctx_rccurve_to(dx1, dy1, dx2, dy2, dx3, 0);
                                c.stbtt__csctx_rccurve_to(dx4, 0, dx5, dy5, dx6, -(dy1 + dy2 + dy5));
                                break;
                            case 0x25:
                                if (sp < 11)
                                    return 0;
                                dx1 = s[0];
                                dy1 = s[1];
                                dx2 = s[2];
                                dy2 = s[3];
                                dx3 = s[4];
                                dy3 = s[5];
                                dx4 = s[6];
                                dy4 = s[7];
                                dx5 = s[8];
                                dy5 = s[9];
                                dx6 = dy6 = s[10];
                                dx = dx1 + dx2 + dx3 + dx4 + dx5;
                                dy = dy1 + dy2 + dy3 + dy4 + dy5;
                                if (Math.Abs((double)dx) > Math.Abs((double)dy))
                                    dy6 = -dy;
                                else
                                    dx6 = -dx;
                                c.stbtt__csctx_rccurve_to(dx1, dy1, dx2, dy2, dx3, dy3);
                                c.stbtt__csctx_rccurve_to(dx4, dy4, dx5, dy5, dx6, dy6);
                                break;
                            default:
                                return 0;
                        }
                    }
                    break;
                    default:
                        if (b0 != 255 && b0 != 28 && (b0 < 32 || b0 > 254))
                            return 0;
                        if (b0 == 255)
                        {
                            f = (float)(int)b.stbtt__buf_get(4) / 0x10000;
                        }
                        else
                        {
                            b.stbtt__buf_skip(-1);
                            f = (short)b.stbtt__cff_int();
                        }

                        if (sp >= 48)
                            return 0;
                        s[sp++] = f;
                        clear_stack = 0;
                        break;
                }

                if (clear_stack != 0)
                    sp = 0;
            }

            return 0;
        }

        private int GetGlyphShapeT2(int glyph_index, out GlyphVertex[] pvertices)
        {
            var count_ctx = new CharStringContext();
            count_ctx.bounds = 1;
            var output_ctx = new CharStringContext();
            if (RunCharstring(glyph_index, count_ctx) != 0)
            {
                pvertices = new GlyphVertex[count_ctx.num_vertices];
                output_ctx.pvertices = pvertices;
                if (RunCharstring(glyph_index, output_ctx) != 0)
                    return output_ctx.num_vertices;
            }

            pvertices = null;
            return 0;
        }

        private int GetGlyphInfoT2(int glyph_index, ref int x0, ref int y0, ref int x1, ref int y1)
        {
            var c = new CharStringContext();
            c.bounds = 1;
            var r = RunCharstring(glyph_index, c);
            x0 = r != 0 ? c.min_x : 0;
            y0 = r != 0 ? c.min_y : 0;
            x1 = r != 0 ? c.max_x : 0;
            y1 = r != 0 ? c.max_y : 0;
            return r != 0 ? c.num_vertices : 0;
        }


        private int GetGlyphKernInfoAdvance(int glyph1, int glyph2)
        {
            var data = this.data + this.kern;
            if (this.kern == 0)
                return 0;
            if (ttUSHORT(data + 2) < 1)
                return 0;
            if (ttUSHORT(data + 8) != 1)
                return 0;
            int l = 0;
            int r = ttUSHORT(data + 10) - 1;
            uint needle = (uint)((glyph1 << 16) | glyph2);
            while (l <= r)
            {
                int m = (l + r) >> 1;
                uint straw = ttULONG(data + 18 + m * 6);
                if (needle < straw)
                    r = m - 1;
                else if (needle > straw)
                    l = m + 1;
                else
                    return ttSHORT(data + 22 + m * 6);
            }

            return 0;
        }

        private int GetGlyphGPOSInfoAdvance(int glyph1, int glyph2)
        {
            ushort lookupListOffset = 0;
            FakePtr<byte> lookupList;
            ushort lookupCount = 0;
            FakePtr<byte> data;
            var i = 0;
            if (this.gpos == 0)
                return 0;
            data = this.data + this.gpos;
            if (ttUSHORT(data + 0) != 1)
                return 0;
            if (ttUSHORT(data + 2) != 0)
                return 0;
            lookupListOffset = ttUSHORT(data + 8);
            lookupList = data + lookupListOffset;
            lookupCount = ttUSHORT(lookupList);
            for (i = 0; i < lookupCount; ++i)
            {
                var lookupOffset = ttUSHORT(lookupList + 2 + 2 * i);
                var lookupTable = lookupList + lookupOffset;
                var lookupType = ttUSHORT(lookupTable);
                var subTableCount = ttUSHORT(lookupTable + 4);
                var subTableOffsets = lookupTable + 6;
                switch (lookupType)
                {
                    case 2:
                    {
                        var sti = 0;
                        for (sti = 0; sti < subTableCount; sti++)
                        {
                            var subtableOffset = ttUSHORT(subTableOffsets + 2 * sti);
                            var table = lookupTable + subtableOffset;
                            var posFormat = ttUSHORT(table);
                            var coverageOffset = ttUSHORT(table + 2);
                            var coverageIndex = stbtt__GetCoverageIndex(table + coverageOffset, glyph1);
                            if (coverageIndex == -1)
                                continue;
                            switch (posFormat)
                            {
                                case 1:
                                {
                                    var l = 0;
                                    var r = 0;
                                    var m = 0;
                                    var straw = 0;
                                    var needle = 0;
                                    var valueFormat1 = ttUSHORT(table + 4);
                                    var valueFormat2 = ttUSHORT(table + 6);
                                    var valueRecordPairSizeInBytes = 2;
                                    var pairSetCount = ttUSHORT(table + 8);
                                    var pairPosOffset = ttUSHORT(table + 10 + 2 * coverageIndex);
                                    var pairValueTable = table + pairPosOffset;
                                    var pairValueCount = ttUSHORT(pairValueTable);
                                    var pairValueArray = pairValueTable + 2;
                                    if (valueFormat1 != 4)
                                        return 0;
                                    if (valueFormat2 != 0)
                                        return 0;
                                    needle = glyph2;
                                    r = pairValueCount - 1;
                                    l = 0;
                                    while (l <= r)
                                    {
                                        ushort secondGlyph = 0;
                                        FakePtr<byte> pairValue;
                                        m = (l + r) >> 1;
                                        pairValue = pairValueArray + (2 + valueRecordPairSizeInBytes) * m;
                                        secondGlyph = ttUSHORT(pairValue);
                                        straw = secondGlyph;
                                        if (needle < straw)
                                        {
                                            r = m - 1;
                                        }
                                        else if (needle > straw)
                                        {
                                            l = m + 1;
                                        }
                                        else
                                        {
                                            var xAdvance = ttSHORT(pairValue + 2);
                                            return xAdvance;
                                        }
                                    }
                                }
                                break;
                                case 2:
                                {
                                    var valueFormat1 = ttUSHORT(table + 4);
                                    var valueFormat2 = ttUSHORT(table + 6);
                                    var classDef1Offset = ttUSHORT(table + 8);
                                    var classDef2Offset = ttUSHORT(table + 10);
                                    var glyph1class = stbtt__GetGlyphClass(table + classDef1Offset, glyph1);
                                    var glyph2class = stbtt__GetGlyphClass(table + classDef2Offset, glyph2);
                                    var class1Count = ttUSHORT(table + 12);
                                    var class2Count = ttUSHORT(table + 14);
                                    if (valueFormat1 != 4)
                                        return 0;
                                    if (valueFormat2 != 0)
                                        return 0;
                                    if (glyph1class >= 0 && glyph1class < class1Count && glyph2class >= 0 &&
                                        glyph2class < class2Count)
                                    {
                                        var class1Records = table + 16;
                                        var class2Records = class1Records + 2 * glyph1class * class2Count;
                                        var xAdvance = ttSHORT(class2Records + 2 * glyph2class);
                                        return xAdvance;
                                    }
                                }
                                break;
                            }
                        }

                        break;
                    }
                }
            }

            return 0;
        }


        #endregion

    }
}