using System.Runtime.InteropServices;

namespace Prowl.Scribe.Internal
{

    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphVertex
    {
        public short x;
        public short y;
        public short cx;
        public short cy;
        public short cx1;
        public short cy1;
        public byte type;
        public byte padding;
    }

    internal static partial class Common
	{
		public const int STBTT_vmove = 1;
		public const int STBTT_vline = 2;
		public const int STBTT_vcurve = 3;
		public const int STBTT_vcubic = 4;

		public const int STBTT_PLATFORM_ID_UNICODE = 0;
		public const int STBTT_PLATFORM_ID_MAC = 1;
		public const int STBTT_PLATFORM_ID_ISO = 2;
		public const int STBTT_PLATFORM_ID_MICROSOFT = 3;

		public const int STBTT_UNICODE_EID_UNICODE_1_0 = 0;
		public const int STBTT_UNICODE_EID_UNICODE_1_1 = 1;
		public const int STBTT_UNICODE_EID_ISO_10646 = 2;
		public const int STBTT_UNICODE_EID_UNICODE_2_0_BMP = 3;
		public const int STBTT_UNICODE_EID_UNICODE_2_0_FULL = 4;

		public const int STBTT_MS_EID_SYMBOL = 0;
		public const int STBTT_MS_EID_UNICODE_BMP = 1;
		public const int STBTT_MS_EID_SHIFTJIS = 2;
		public const int STBTT_MS_EID_UNICODE_FULL = 10;

		public const int STBTT_MAC_EID_ROMAN = 0;
		public const int STBTT_MAC_EID_ARABIC = 4;
		public const int STBTT_MAC_EID_JAPANESE = 1;
		public const int STBTT_MAC_EID_HEBREW = 5;
		public const int STBTT_MAC_EID_CHINESE_TRAD = 2;
		public const int STBTT_MAC_EID_GREEK = 6;
		public const int STBTT_MAC_EID_KOREAN = 3;
		public const int STBTT_MAC_EID_RUSSIAN = 7;

		public const int STBTT_MS_LANG_ENGLISH = 0x0409;
		public const int STBTT_MS_LANG_ITALIAN = 0x0410;
		public const int STBTT_MS_LANG_CHINESE = 0x0804;
		public const int STBTT_MS_LANG_JAPANESE = 0x0411;
		public const int STBTT_MS_LANG_DUTCH = 0x0413;
		public const int STBTT_MS_LANG_KOREAN = 0x0412;
		public const int STBTT_MS_LANG_FRENCH = 0x040c;
		public const int STBTT_MS_LANG_RUSSIAN = 0x0419;
		public const int STBTT_MS_LANG_GERMAN = 0x0407;
		public const int STBTT_MS_LANG_SPANISH = 0x0409;
		public const int STBTT_MS_LANG_HEBREW = 0x040d;
		public const int STBTT_MS_LANG_SWEDISH = 0x041D;

		public const int STBTT_MAC_LANG_ENGLISH = 0;
		public const int STBTT_MAC_LANG_JAPANESE = 11;
		public const int STBTT_MAC_LANG_ARABIC = 12;
		public const int STBTT_MAC_LANG_KOREAN = 23;
		public const int STBTT_MAC_LANG_DUTCH = 4;
		public const int STBTT_MAC_LANG_RUSSIAN = 32;
		public const int STBTT_MAC_LANG_FRENCH = 1;
		public const int STBTT_MAC_LANG_SPANISH = 6;
		public const int STBTT_MAC_LANG_GERMAN = 2;
		public const int STBTT_MAC_LANG_SWEDISH = 5;
		public const int STBTT_MAC_LANG_HEBREW = 10;
		public const int STBTT_MAC_LANG_CHINESE_SIMPLIFIED = 33;
		public const int STBTT_MAC_LANG_ITALIAN = 3;
		public const int STBTT_MAC_LANG_CHINESE_TRAD = 19;

		public static ushort ttUSHORT(FakePtr<byte> p)
		{
			return (ushort)(p[0] * 256 + p[1]);
		}

		public static short ttSHORT(FakePtr<byte> p)
		{
			return (short)(p[0] * 256 + p[1]);
		}

		public static uint ttULONG(FakePtr<byte> p)
		{
			return (uint)((p[0] << 24) + (p[1] << 16) + (p[2] << 8) + p[3]);
		}

		public static int ttLONG(FakePtr<byte> p)
		{
			return (p[0] << 24) + (p[1] << 16) + (p[2] << 8) + p[3];
		}

		public static int stbtt__isfont(FakePtr<byte> font)
		{
			if (font[0] == '1' && font[1] == 0 && font[2] == 0 && font[3] == 0)
				return 1;
			if (font[0] == "typ1"[0] && font[1] == "typ1"[1] && font[2] == "typ1"[2] && font[3] == "typ1"[3])
				return 1;
			if (font[0] == "OTTO"[0] && font[1] == "OTTO"[1] && font[2] == "OTTO"[2] && font[3] == "OTTO"[3])
				return 1;
			if (font[0] == 0 && font[1] == 1 && font[2] == 0 && font[3] == 0)
				return 1;
			if (font[0] == "true"[0] && font[1] == "true"[1] && font[2] == "true"[2] && font[3] == "true"[3])
				return 1;
			return 0;
		}

		public static int stbtt_GetFontOffsetForIndex_internal(FakePtr<byte> font_collection, int index)
		{
			if (stbtt__isfont(font_collection) != 0)
				return index == 0 ? 0 : -1;
			if (font_collection[0] == "ttcf"[0] && font_collection[1] == "ttcf"[1] && font_collection[2] == "ttcf"[2] &&
				font_collection[3] == "ttcf"[3])
				if (ttULONG(font_collection + 4) == 0x00010000 || ttULONG(font_collection + 4) == 0x00020000)
				{
					var n = ttLONG(font_collection + 8);
					if (index >= n)
						return -1;
					return (int)ttULONG(font_collection + 12 + index * 4);
				}

			return -1;
		}

		public static int stbtt_GetNumberOfFonts_internal(FakePtr<byte> font_collection)
		{
			if (stbtt__isfont(font_collection) != 0)
				return 1;
			if (font_collection[0] == "ttcf"[0] && font_collection[1] == "ttcf"[1] && font_collection[2] == "ttcf"[2] &&
				font_collection[3] == "ttcf"[3])
				if (ttULONG(font_collection + 4) == 0x00010000 || ttULONG(font_collection + 4) == 0x00020000)
					return ttLONG(font_collection + 8);

			return 0;
		}

		public static void stbtt_setvertex(ref GlyphVertex v, byte type, int x, int y, int cx, int cy)
		{
			v.type = type;
			v.x = (short)x;
			v.y = (short)y;
			v.cx = (short)cx;
			v.cy = (short)cy;
		}

		public static int stbtt__close_shape(GlyphVertex[] vertices, int num_vertices, int was_off, int start_off,
			int sx, int sy, int scx, int scy, int cx, int cy)
		{
			var v = new GlyphVertex();
			if (start_off != 0)
			{
				if (was_off != 0)
				{
					stbtt_setvertex(ref v, STBTT_vcurve, (cx + scx) >> 1, (cy + scy) >> 1, cx, cy);
					vertices[num_vertices++] = v;
				}

				stbtt_setvertex(ref v, STBTT_vcurve, sx, sy, scx, scy);
				vertices[num_vertices++] = v;
			}
			else
			{
				if (was_off != 0)
				{
					stbtt_setvertex(ref v, STBTT_vcurve, sx, sy, cx, cy);
					vertices[num_vertices++] = v;
				}
				else
				{
					stbtt_setvertex(ref v, STBTT_vline, sx, sy, 0, 0);
					vertices[num_vertices++] = v;
				}
			}

			return num_vertices;
		}

		public static int stbtt__GetCoverageIndex(FakePtr<byte> coverageTable, int glyph)
		{
			var coverageFormat = ttUSHORT(coverageTable);
			switch (coverageFormat)
			{
				case 1:
				{
					var glyphCount = ttUSHORT(coverageTable + 2);
					var l = 0;
					var r = glyphCount - 1;
					var m = 0;
					var straw = 0;
					var needle = glyph;
					while (l <= r)
					{
						var glyphArray = coverageTable + 4;
						ushort glyphID = 0;
						m = (l + r) >> 1;
						glyphID = ttUSHORT(glyphArray + 2 * m);
						straw = glyphID;
						if (needle < straw)
							r = m - 1;
						else if (needle > straw)
							l = m + 1;
						else
							return m;
					}
				}
				break;
				case 2:
				{
					var rangeCount = ttUSHORT(coverageTable + 2);
					var rangeArray = coverageTable + 4;
					var l = 0;
					var r = rangeCount - 1;
					var m = 0;
					var strawStart = 0;
					var strawEnd = 0;
					var needle = glyph;
					while (l <= r)
					{
						FakePtr<byte> rangeRecord;
						m = (l + r) >> 1;
						rangeRecord = rangeArray + 6 * m;
						strawStart = ttUSHORT(rangeRecord);
						strawEnd = ttUSHORT(rangeRecord + 2);
						if (needle < strawStart)
						{
							r = m - 1;
						}
						else if (needle > strawEnd)
						{
							l = m + 1;
						}
						else
						{
							var startCoverageIndex = ttUSHORT(rangeRecord + 4);
							return startCoverageIndex + glyph - strawStart;
						}
					}
				}
				break;
				default:
				{
				}
				break;
			}

			return -1;
		}

		public static int stbtt__GetGlyphClass(FakePtr<byte> classDefTable, int glyph)
		{
			var classDefFormat = ttUSHORT(classDefTable);
			switch (classDefFormat)
			{
				case 1:
				{
					var startGlyphID = ttUSHORT(classDefTable + 2);
					var glyphCount = ttUSHORT(classDefTable + 4);
					var classDef1ValueArray = classDefTable + 6;
					if (glyph >= startGlyphID && glyph < startGlyphID + glyphCount)
						return ttUSHORT(classDef1ValueArray + 2 * (glyph - startGlyphID));
					classDefTable = classDef1ValueArray + 2 * glyphCount;
				}
				break;
				case 2:
				{
					var classRangeCount = ttUSHORT(classDefTable + 2);
					var classRangeRecords = classDefTable + 4;
					var l = 0;
					var r = classRangeCount - 1;
					var m = 0;
					var strawStart = 0;
					var strawEnd = 0;
					var needle = glyph;
					while (l <= r)
					{
						FakePtr<byte> classRangeRecord;
						m = (l + r) >> 1;
						classRangeRecord = classRangeRecords + 6 * m;
						strawStart = ttUSHORT(classRangeRecord);
						strawEnd = ttUSHORT(classRangeRecord + 2);
						if (needle < strawStart)
							r = m - 1;
						else if (needle > strawEnd)
							l = m + 1;
						else
							return ttUSHORT(classRangeRecord + 4);
					}

					classDefTable = classRangeRecords + 6 * classRangeCount;
				}
				break;
				default:
				{
				}
				break;
			}

			return -1;
		}

		//public static void stbtt_GetScaledFontVMetrics(byte[] fontdata, int index, float size, ref float ascent,
		//	ref float descent, ref float lineGap)
		//{
		//	var i_ascent = 0;
		//	var i_descent = 0;
		//	var i_lineGap = 0;
		//	float scale = 0;
		//	var info = new FontInfo();
		//	info.InitFont(fontdata, stbtt_GetFontOffsetForIndex(fontdata, index));
		//	scale = size > 0 ? info.ScaleForPixelHeight(size) : info.ScaleForMappingEmToPixels(-size);
		//	info.GetFontVerticalMetrics(out i_ascent, out i_descent, out i_lineGap);
		//	ascent = i_ascent * scale;
		//	descent = i_descent * scale;
		//	lineGap = i_lineGap * scale;
		//}

		public static int stbtt__CompareUTF8toUTF16_bigendian_prefix(FakePtr<byte> s1, int len1, FakePtr<byte> s2,
			int len2)
		{
			var i = 0;
			while (len2 != 0)
			{
				var ch = (ushort)(s2[0] * 256 + s2[1]);
				if (ch < 0x80)
				{
					if (i >= len1)
						return -1;
					if (s1[i++] != ch)
						return -1;
				}
				else if (ch < 0x800)
				{
					if (i + 1 >= len1)
						return -1;
					if (s1[i++] != 0xc0 + (ch >> 6))
						return -1;
					if (s1[i++] != 0x80 + (ch & 0x3f))
						return -1;
				}
				else if (ch >= 0xd800 && ch < 0xdc00)
				{
					uint c = 0;
					var ch2 = (ushort)(s2[2] * 256 + s2[3]);
					if (i + 3 >= len1)
						return -1;
					c = (uint)(((ch - 0xd800) << 10) + (ch2 - 0xdc00) + 0x10000);
					if (s1[i++] != 0xf0 + (c >> 18))
						return -1;
					if (s1[i++] != 0x80 + ((c >> 12) & 0x3f))
						return -1;
					if (s1[i++] != 0x80 + ((c >> 6) & 0x3f))
						return -1;
					if (s1[i++] != 0x80 + (c & 0x3f))
						return -1;
					s2 += 2;
					len2 -= 2;
				}
				else if (ch >= 0xdc00 && ch < 0xe000)
				{
					return -1;
				}
				else
				{
					if (i + 2 >= len1)
						return -1;
					if (s1[i++] != 0xe0 + (ch >> 12))
						return -1;
					if (s1[i++] != 0x80 + ((ch >> 6) & 0x3f))
						return -1;
					if (s1[i++] != 0x80 + (ch & 0x3f))
						return -1;
				}

				s2 += 2;
				len2 -= 2;
			}

			return i;
		}

		public static int stbtt_CompareUTF8toUTF16_bigendian_internal(FakePtr<byte> s1, int len1, FakePtr<byte> s2,
			int len2)
		{
			return len1 == stbtt__CompareUTF8toUTF16_bigendian_prefix(s1, len1, s2, len2) ? 1 : 0;
		}

		public static int stbtt__matchpair(FakePtr<byte> fc, uint nm, FakePtr<byte> name, int nlen, int target_id,
			int next_id)
		{
			var i = 0;
			var count = (int)ttUSHORT(fc + nm + 2);
			var stringOffset = (int)(nm + ttUSHORT(fc + nm + 4));
			for (i = 0; i < count; ++i)
			{
				var loc = (uint)(nm + 6 + 12 * i);
				var id = (int)ttUSHORT(fc + loc + 6);
				if (id == target_id)
				{
					var platform = (int)ttUSHORT(fc + loc + 0);
					var encoding = (int)ttUSHORT(fc + loc + 2);
					var language = (int)ttUSHORT(fc + loc + 4);
					if (platform == 0 || platform == 3 && encoding == 1 || platform == 3 && encoding == 10)
					{
						var slen = (int)ttUSHORT(fc + loc + 8);
						var off = (int)ttUSHORT(fc + loc + 10);
						var matchlen =
							stbtt__CompareUTF8toUTF16_bigendian_prefix(name, nlen, fc + stringOffset + off, slen);
						if (matchlen >= 0)
						{
							if (i + 1 < count && ttUSHORT(fc + loc + 12 + 6) == next_id &&
								ttUSHORT(fc + loc + 12) == platform && ttUSHORT(fc + loc + 12 + 2) == encoding &&
								ttUSHORT(fc + loc + 12 + 4) == language)
							{
								slen = ttUSHORT(fc + loc + 12 + 8);
								off = ttUSHORT(fc + loc + 12 + 10);
								if (slen == 0)
								{
									if (matchlen == nlen)
										return 1;
								}
								else if (matchlen < nlen && name[matchlen] == ' ')
								{
									++matchlen;
									if (stbtt_CompareUTF8toUTF16_bigendian_internal(name + matchlen, nlen - matchlen,
											fc + stringOffset + off, slen) != 0)
										return 1;
								}
							}
							else
							{
								if (matchlen == nlen)
									return 1;
							}
						}
					}
				}
			}

			return 0;
		}

		public static int stbtt__matches(byte[] data, uint offset, FakePtr<byte> name, int flags)
		{
			var nlen = 0;
			var ptr = name;

			while (ptr.GetAndIncrease() != '\0')
				ptr++;

			nlen = ptr.Offset - name.Offset - 1;
			uint nm = 0;
			uint hd = 0;

			var fc = new FakePtr<byte>(data);
			if (stbtt__isfont(fc + offset) == 0)
				return 0;
			if (flags != 0)
			{
				hd = FindTable(fc, offset, "head");
				if ((ttUSHORT(fc + hd + 44) & 7) != (flags & 7))
					return 0;
			}

			nm = FindTable(fc, offset, "name");
			if (nm == 0)
				return 0;
			if (flags != 0)
			{
				if (stbtt__matchpair(fc, nm, name, nlen, 16, -1) != 0)
					return 1;
				if (stbtt__matchpair(fc, nm, name, nlen, 1, -1) != 0)
					return 1;
				if (stbtt__matchpair(fc, nm, name, nlen, 3, -1) != 0)
					return 1;
			}
			else
			{
				if (stbtt__matchpair(fc, nm, name, nlen, 16, 17) != 0)
					return 1;
				if (stbtt__matchpair(fc, nm, name, nlen, 1, 2) != 0)
					return 1;
				if (stbtt__matchpair(fc, nm, name, nlen, 3, -1) != 0)
					return 1;
			}

			return 0;

			uint FindTable(FakePtr<byte> data, uint fontstart, string tag)
			{
			    int num_tables = ttUSHORT(data + fontstart + 4);
			    var tabledir = fontstart + 12;
			    int i;
			    for (i = 0; i < num_tables; ++i)
			    {
			        var loc = (uint)(tabledir + 16 * i);
			        if ((data + loc + 0)[0] == tag[0] && (data + loc + 0)[1] == tag[1] &&
			            (data + loc + 0)[2] == tag[2] && (data + loc + 0)[3] == tag[3])
			            return ttULONG(data + loc + 8);
			    }

			    return 0;
			}
		}

		public static int stbtt_FindMatchingFont_internal(byte[] font_collection, FakePtr<byte> name_utf8, int flags)
		{
			var i = 0;
			for (i = 0; ; ++i)
			{
				var off = stbtt_GetFontOffsetForIndex(font_collection, i);
				if (off < 0)
					return off;
				if (stbtt__matches(font_collection, (uint)off, name_utf8, flags) != 0)
					return off;
			}
		}

		public static int stbtt_GetFontOffsetForIndex(byte[] data, int index)
		{
			return stbtt_GetFontOffsetForIndex_internal(new FakePtr<byte>(data), index);
		}

		public static int stbtt_GetNumberOfFonts(FakePtr<byte> data)
		{
			return stbtt_GetNumberOfFonts_internal(data);
		}

		public static int stbtt_FindMatchingFont(byte[] fontdata, FakePtr<byte> name, int flags)
		{
			return stbtt_FindMatchingFont_internal(fontdata, name, flags);
		}

		public static int stbtt_CompareUTF8toUTF16_bigendian(FakePtr<byte> s1, int len1, FakePtr<byte> s2, int len2)
		{
			return stbtt_CompareUTF8toUTF16_bigendian_internal(s1, len1, s2, len2);
		}
	}
}