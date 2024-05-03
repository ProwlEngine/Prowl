using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.Rendering.Primitives;
using StbTrueTypeSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Prowl.Runtime.GUI.Graphics.UIDrawList;
using static Prowl.Runtime.GUI.Gui;
using static System.Net.Mime.MediaTypeNames;

namespace Prowl.Runtime
{
    public struct GlyphInfo
    {
        public int X, Y, Width, Height;
        public int XOffset, YOffset;
        public int XAdvance;
    }

    public sealed class Font : EngineObject, ISerializable
    {
        public double FontSize = 20.0;
        public Dictionary<uint, GlyphInfo> Glyphs;
        public Color32[] Bitmap;
        public int Width;
        public int Height;

        public Vector2 TexUvWhitePixel => new(0.5f * (1.0f / Width), 0.5f * (1.0f / Height));

        public Texture2D? Texture { get; private set; }

        public void CreateResource()
        {
            Texture = new Texture2D((uint)Width, (uint)Height, false, Rendering.Primitives.TextureImageFormat.Color4b);
            Memory<Color32> data = new Memory<Color32>(Bitmap);
            Texture.SetData(data);
            Texture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        }

        public static Font CreateFromTTFMemory(byte[] ttf, float fontSize, int width, int height, CharacterRange[] characterRanges)
        {
            Font font = new();
            font.FontSize = fontSize;
            font.Width = width;
            font.Height = height;
            var bitmap = new byte[width * height];
            font.Glyphs = new Dictionary<uint, GlyphInfo>();
            var context = new StbTrueType.stbtt_pack_context();
            unsafe
            {
                fixed (byte* pixelsPtr = bitmap)
                {
                    StbTrueType.stbtt_PackBegin(context, pixelsPtr, width, height, width, 1, null);
                }

                //var ttf = File.ReadAllBytes(assetPath.FullName);
                var fontInfo = StbTrueType.CreateFont(ttf, 0);
                if (fontInfo == null)
                    throw new Exception("Failed to init font.");

                var scaleFactor = StbTrueType.stbtt_ScaleForPixelHeight(fontInfo, fontSize);

                int ascent, descent, lineGap;
                StbTrueType.stbtt_GetFontVMetrics(fontInfo, &ascent, &descent, &lineGap);

                foreach (var range in characterRanges)
                {
                    if (range.Start > range.End)
                        continue;

                    var cd = new StbTrueType.stbtt_packedchar[range.End - range.Start + 1];
                    fixed (StbTrueType.stbtt_packedchar* chardataPtr = cd)
                    {
                        StbTrueType.stbtt_PackFontRange(context, fontInfo.data, 0, fontSize,
                            range.Start,
                            range.End - range.Start + 1,
                            chardataPtr);
                    }

                    for (uint i = 0; i < cd.Length; ++i)
                    {
                        var yOff = cd[i].yoff;
                        yOff += ascent * scaleFactor;

                        var glyphInfo = new GlyphInfo {
                            X = cd[i].x0 + 1, // Offset x by 1
                            Y = cd[i].y0 + 1, // Offset y by 1
                            Width = cd[i].x1 - cd[i].x0,
                            Height = cd[i].y1 - cd[i].y0,
                            XOffset = (int)cd[i].xoff,
                            YOffset = (int)Math.Round(yOff),
                            XAdvance = (int)Math.Round(cd[i].xadvance)
                        };

                        font.Glyphs[i + (uint)range.Start] = glyphInfo;
                    }
                }
            }

            // Offset by minimal offset
            var minimumOffsetY = 10000;
            foreach (var pair in font.Glyphs)
                if (pair.Value.YOffset < minimumOffsetY)
                    minimumOffsetY = pair.Value.YOffset;

            var keys = font.Glyphs.Keys.ToArray();
            foreach (var key in keys)
            {
                var pc = font.Glyphs[key];
                pc.YOffset -= minimumOffsetY;
                font.Glyphs[key] = pc;
            }

            font.Bitmap = new Color32[width * height];
            // Set the first pixel to white (TexUvWhitePixel)
            font.Bitmap[0] = new Color32 { red = 255, green = 255, blue = 255, alpha = 255 };
            for (var i = 1; i < bitmap.Length; ++i)
            {
                var b = bitmap[i - 1];
                font.Bitmap[i].red = b;
                font.Bitmap[i].green = b;
                font.Bitmap[i].blue = b;

                font.Bitmap[i].alpha = b;
            }

            font.CreateResource();

            return font;
        }

        public Vector2 CalcTextSize(string str, int beginIndex, double wrap_width = -1f)
        {
            int text_display_end = str.Length;

            double font_size = FontSize;
            if (beginIndex == text_display_end)
                return new Vector2(0.0f, font_size);
            Vector2 text_size;
            CalcTextSizeA(out text_size, font_size, double.MaxValue, wrap_width, str, beginIndex, text_display_end);

            // Cancel out character spacing for the last character of a line (it is baked into glyph->XAdvance field)
            double font_scale = font_size / FontSize;
            double character_spacing_x = 1.0f * font_scale;
            if (text_size.x > 0.0f)
                text_size.x -= character_spacing_x;
            text_size.x = (int)(text_size.x + 0.95f);

            return text_size;
        }

        public int CalcTextSizeA(out Vector2 textSize, double size, double maxWidth, double wrapWidth, string text, int textBegin, int textEnd = -1)
        {
            if (textEnd == -1)
                textEnd = text.Length; // FIXME-OPT: Need to avoid this.

            double lineHeight = size;
            double scale = size / FontSize;
            Vector2 textSizeResult = new Vector2(0, 0);
            double lineWidth = 0.0f;
            bool wordWrapEnabled = (wrapWidth > 0.0f);
            int wordWrapEol = -1;
            int s = textBegin;

            while (s < textEnd)
            {
                if (wordWrapEnabled)
                {
                    // Calculate how far we can render. Requires two passes on the string data but keeps the code simple and not intrusive for what's essentially an uncommon feature.
                    if (wordWrapEol == -1)
                    {
                        wordWrapEol = CalcWordWrapPositionA(scale, text, s, textEnd, wrapWidth - lineWidth);
                        if (wordWrapEol == s) // Wrap_width is too small to fit anything. Force displaying 1 character to minimize the height discontinuity.
                            wordWrapEol++; // +1 may not be a character start point in UTF-8 but it's ok because we use s >= word_wrap_eol below
                    }

                    if (s >= wordWrapEol)
                    {
                        if (textSizeResult.x < lineWidth)
                            textSizeResult.x = lineWidth;

                        textSizeResult.y += lineHeight;
                        lineWidth = 0.0;
                        wordWrapEol = -1;

                        // Wrapping skips upcoming blanks
                        while (s < textEnd)
                        {
                            char wc = text[s];
                            if (char.IsSeparator(wc)) { s++; }
                            else if (wc == '\n') { s++; break; }
                            else { break; }
                        }
                        continue;
                    }
                }

                // Decode and advance source
                int prevS = s;
                char c = text[s++];

                if (c < 32)
                {
                    if (c == '\n')
                    {
                        textSizeResult.x = Math.Max(textSizeResult.x, lineWidth);
                        textSizeResult.y += lineHeight;
                        lineWidth = 0.0;
                        continue;
                    }
                    if (c == '\r')
                        continue;
                }

                double charWidth = 0.0;
                if (Glyphs.TryGetValue(c, out GlyphInfo glyph))
                {
                    charWidth = glyph.XAdvance * scale;
                }

                if (lineWidth + charWidth >= maxWidth)
                {
                    s = prevS;
                    break;
                }

                lineWidth += charWidth;
            }

            if (textSizeResult.x < lineWidth)
                textSizeResult.x = lineWidth;

            if (lineWidth > 0 || textSizeResult.y == 0.0)
                textSizeResult.y += lineHeight;

            textSize = textSizeResult;
            return s; // Return the position we stopped at
        }

        int CalcWordWrapPositionA(double scale, string text, int textBegin, int textEnd, double wrapWidth)
        {
            // Simple word-wrapping for English, not full-featured. Please submit failing cases!
            // FIXME: Much possible improvements (don't cut things like "word !", "word!!!" but cut within "word,,,,", more sensible support for punctuations, support for Unicode punctuations, etc.)
            // For references, possible wrap point marked with ^
            // "aaa bbb, ccc,ddd. eee fff. ggg!"
            // ^ ^ ^ ^ ^__ ^ ^
            // List of hardcoded separators: .,;!?'"
            // Skip extra blanks after a line returns (that includes not counting them in width computation)
            // e.g. "Hello world" --> "Hello" "World"
            // Cut words that cannot possibly fit within one line.
            // e.g.: "The tropical fish" with ~5 characters worth of width --> "The tr" "opical" "fish"

            double lineWidth = 0.0;
            double wordWidth = 0.0;
            double blankWidth = 0.0;
            int wordEnd = textBegin;
            int prevWordEnd = -1;
            bool insideWord = true;

            int s = textBegin;
            while (s < textEnd)
            {
                char c = text[s];
                int nextS = s + 1;

                if (c == 0)
                    break;

                if (c < 32)
                {
                    if (c == '\n')
                    {
                        lineWidth = wordWidth = blankWidth = 0.0f;
                        insideWord = true;
                        s = nextS;
                        continue;
                    }
                    if (c == '\r')
                    {
                        s = nextS;
                        continue;
                    }
                }

                double charWidth = 0.0;
                if (Glyphs.TryGetValue(c, out GlyphInfo glyph))
                {
                    charWidth = glyph.XAdvance * scale;
                }

                if (char.IsSeparator(c))
                {
                    if (insideWord)
                    {
                        lineWidth += blankWidth;
                        blankWidth = 0.0;
                    }
                    blankWidth += charWidth;
                    insideWord = false;
                }
                else
                {
                    wordWidth += charWidth;
                    if (insideWord)
                    {
                        wordEnd = nextS;
                    }
                    else
                    {
                        prevWordEnd = wordEnd;
                        lineWidth += wordWidth + blankWidth;
                        wordWidth = blankWidth = 0.0;
                    }
                    // Allow wrapping after punctuation.
                    insideWord = !(c == '.' || c == ',' || c == ';' || c == '!' || c == '?' || c == '\"');
                }

                // We ignore blank width at the end of the line (they can be skipped)
                if (lineWidth + wordWidth >= wrapWidth)
                {
                    // Words that cannot possibly fit within an entire line will be cut anywhere.
                    if (wordWidth < wrapWidth)
                        s = prevWordEnd > -1 ? prevWordEnd : wordEnd;
                    break;
                }

                s = nextS;
            }

            return s;
        }

        public Rect RenderText(double size, Vector2 pos, uint color, Vector4 clipRect, string text, int textBegin, int textEnd, UIDrawList drawList, double wrapWidth = 0.0, bool cpuFineClip = false)
        {
            if (textEnd == -1)
                textEnd = text.Length;

            // Align to be pixel perfect
            pos.x = (int)pos.x;
            pos.y = (int)pos.y;
            double x = pos.x;
            double y = pos.y;
            if (y > clipRect.w)
                return Rect.Empty;

            double scale = size / FontSize;
            double lineHeight = FontSize * scale;
            bool wordWrapEnabled = wrapWidth > 0.0;
            int wordWrapEol = -1;

            int vtxWrite = drawList._VtxWritePtr;
            int idxWrite = drawList._IdxWritePtr;
            uint vtxCurrentIdx = drawList._VtxCurrentIdx;

            int s = textBegin;
            if (!wordWrapEnabled && y + lineHeight < clipRect.y)
                while (s < textEnd && text[s] != '\n')  // Fast-forward to next line
                    s++;

            while (s < textEnd)
            {
                if (wordWrapEnabled)
                {
                    // Calculate how far we can render. Requires two passes on the string data but keeps the code simple and not intrusive for what's essentially an uncommon feature.
                    if (wordWrapEol == -1)
                    {
                        wordWrapEol = CalcWordWrapPositionA(scale, text, s, textEnd, wrapWidth - (x - pos.x));
                        if (wordWrapEol == s) // Wrap_width is too small to fit anything. Force displaying 1 character to minimize the height discontinuity.
                            wordWrapEol++;    // +1 may not be a character start point in UTF-8 but it's ok because we use s >= word_wrap_eol below
                    }

                    if (s >= wordWrapEol)
                    {
                        x = pos.x;
                        y += lineHeight;
                        wordWrapEol = -1;

                        // Wrapping skips upcoming blanks
                        while (s < textEnd)
                        {
                            char wc = text[s];
                            if (char.IsSeparator(wc)) { s++; }
                            else if (wc == '\n') { s++; break; }
                            else { break; }
                        }
                        continue;
                    }
                }

                // Decode and advance source
                char c = text[s++];

                if (c < 32)
                {
                    if (c == '\n')
                    {
                        x = pos.x;
                        y += lineHeight;

                        if (y > clipRect.w)
                            break;
                        if (!wordWrapEnabled && y + lineHeight < clipRect.y)
                            while (s < textEnd && text[s] != '\n')  // Fast-forward to next line
                                s++;

                        continue;
                    }
                    if (c == '\r')
                        continue;
                }

                double charWidth = 0.0f;
                if (Glyphs.TryGetValue(c, out GlyphInfo glyph))
                {
                    charWidth = glyph.XAdvance * scale;

                    // Arbitrarily assume that both space and tabs are empty glyphs as an optimization
                    if (c != ' ' && c != '\t')
                    {
                        double x1 = x + glyph.XOffset * scale;
                        double x2 = x1 + glyph.Width * scale;
                        double y1 = y + glyph.YOffset * scale;
                        double y2 = y1 + glyph.Height * scale;

                        if (x1 <= clipRect.z && x2 >= clipRect.x)
                        {
                            // Render a character
                            double u1 = (double)glyph.X / Width;
                            double v1 = (double)glyph.Y / Height;
                            double u2 = (double)(glyph.X + glyph.Width) / Width;
                            double v2 = (double)(glyph.Y + glyph.Height) / Height;

                            // CPU side clipping used to fit text in their frame when the frame is too small. Only does clipping for axis aligned quads.
                            if (cpuFineClip)
                            {
                                if (x1 < clipRect.x)
                                {
                                    u1 = u1 + (1.0f - (x2 - clipRect.x) / (x2 - x1)) * (u2 - u1);
                                    x1 = clipRect.x;
                                }
                                if (y1 < clipRect.y)
                                {
                                    v1 = v1 + (1.0f - (y2 - clipRect.y) / (y2 - y1)) * (v2 - v1);
                                    y1 = clipRect.y;
                                }
                                if (x2 > clipRect.z)
                                {
                                    u2 = u1 + (clipRect.z - x1) / (x2 - x1) * (u2 - u1);
                                    x2 = clipRect.z;
                                }
                                if (y2 > clipRect.w)
                                {
                                    v2 = v1 + (clipRect.w - y1) / (y2 - y1) * (v2 - v1);
                                    y2 = clipRect.w;
                                }
                                if (y1 >= y2)
                                {
                                    x += charWidth;
                                    continue;
                                }
                            }

                            // We are NOT calling PrimRectUV() here because non-inlined causes too much overhead in a debug build.
                            // Inlined here:
                            {
                                drawList.IdxBuffer[idxWrite++] = (ushort)vtxCurrentIdx; drawList.IdxBuffer[idxWrite++] = (ushort)(vtxCurrentIdx + 1); drawList.IdxBuffer[idxWrite++] = (ushort)(vtxCurrentIdx + 2);
                                drawList.IdxBuffer[idxWrite++] = (ushort)vtxCurrentIdx; drawList.IdxBuffer[idxWrite++] = (ushort)(vtxCurrentIdx + 2); drawList.IdxBuffer[idxWrite++] = (ushort)(vtxCurrentIdx + 3);
                                drawList.VtxBuffer[vtxWrite++] = new UIVertex { pos = new Vector2(x1, y1), uv = new Vector2(u1, v1), col = color };
                                drawList.VtxBuffer[vtxWrite++] = new UIVertex { pos = new Vector2(x2, y1), uv = new Vector2(u2, v1), col = color };
                                drawList.VtxBuffer[vtxWrite++] = new UIVertex { pos = new Vector2(x2, y2), uv = new Vector2(u2, v2), col = color };
                                drawList.VtxBuffer[vtxWrite++] = new UIVertex { pos = new Vector2(x1, y2), uv = new Vector2(u1, v2), col = color };
                                vtxCurrentIdx += 4;
                            }
                        }
                    }
                }

                x += charWidth;
            }

            drawList._VtxWritePtr = vtxWrite;
            drawList._VtxCurrentIdx = vtxCurrentIdx;
            drawList._IdxWritePtr = idxWrite;

            return new Rect(pos.x, pos.y, x, y + lineHeight);
        }


        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            var compoundTag = SerializedProperty.NewCompound();
            compoundTag.Add("Width", new(Width));
            compoundTag.Add("Height", new(Height));

            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memoryStream))
            {
                for (int i = 0; i < Bitmap.Length; i++)
                {
                    writer.Write(Bitmap[i].red);
                    writer.Write(Bitmap[i].green);
                    writer.Write(Bitmap[i].blue);
                    writer.Write(Bitmap[i].alpha);
                }
                compoundTag.Add("Bitmap", new(memoryStream.ToArray()));
            }

            SerializedProperty glyphsTag = SerializedProperty.NewList();
            foreach (var glyph in Glyphs)
            {
                var glyphTag = SerializedProperty.NewCompound();
                glyphTag.Add("Unicode", new(glyph.Key));
                glyphTag.Add("X", new(glyph.Value.X));
                glyphTag.Add("Y", new(glyph.Value.Y));
                glyphTag.Add("Width", new(glyph.Value.Width));
                glyphTag.Add("Height", new(glyph.Value.Height));
                glyphTag.Add("XOffset", new(glyph.Value.XOffset));
                glyphTag.Add("YOffset", new(glyph.Value.YOffset));
                glyphTag.Add("XAdvance", new(glyph.Value.XAdvance));
                glyphsTag.ListAdd(glyphTag);
            }
            compoundTag.Add("Glyphs", glyphsTag);

            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Width = value["Width"].IntValue;
            Height = value["Height"].IntValue;
            //Bitmap = value["Bitmap"].ByteArrayValue;

            using (MemoryStream memoryStream = new MemoryStream(value["Bitmap"].ByteArrayValue))
            using (BinaryReader reader = new BinaryReader(memoryStream))
            {
                Bitmap = new Color32[Width * Height];
                for (int i = 0; i < Bitmap.Length; i++)
                {
                    Bitmap[i].red = reader.ReadByte();
                    Bitmap[i].green = reader.ReadByte();
                    Bitmap[i].blue = reader.ReadByte();
                    Bitmap[i].alpha = reader.ReadByte();
                }
            }

            Glyphs = new();
            var glyphsTag = value.Get("Glyphs");
            foreach (var glyphTag in glyphsTag.List)
            {
                var glyph = new GlyphInfo {
                    X = glyphTag["X"].IntValue,
                    Y = glyphTag["Y"].IntValue,
                    Width = glyphTag["Width"].IntValue,
                    Height = glyphTag["Height"].IntValue,
                    XOffset = glyphTag["XOffset"].IntValue,
                    YOffset = glyphTag["YOffset"].IntValue,
                    XAdvance = glyphTag["XAdvance"].IntValue
                };
                Glyphs.Add(glyphTag["Unicode"].UIntValue, glyph);
            }

            CreateResource();
        }

        public struct CharacterRange
        {
            public static readonly CharacterRange BasicLatin = new CharacterRange(0x0020, 0x007F);
            public static readonly CharacterRange Latin1Supplement = new CharacterRange(0x00A0, 0x00FF);
            public static readonly CharacterRange LatinExtendedA = new CharacterRange(0x0100, 0x017F);
            public static readonly CharacterRange LatinExtendedB = new CharacterRange(0x0180, 0x024F);
            public static readonly CharacterRange Cyrillic = new CharacterRange(0x0400, 0x04FF);
            public static readonly CharacterRange CyrillicSupplement = new CharacterRange(0x0500, 0x052F);
            public static readonly CharacterRange Hiragana = new CharacterRange(0x3040, 0x309F);
            public static readonly CharacterRange Katakana = new CharacterRange(0x30A0, 0x30FF);
            public static readonly CharacterRange Greek = new CharacterRange(0x0370, 0x03FF);
            public static readonly CharacterRange CjkSymbolsAndPunctuation = new CharacterRange(0x3000, 0x303F);
            public static readonly CharacterRange CjkUnifiedIdeographs = new CharacterRange(0x4e00, 0x9fff);
            public static readonly CharacterRange HangulCompatibilityJamo = new CharacterRange(0x3130, 0x318f);
            public static readonly CharacterRange HangulSyllables = new CharacterRange(0xac00, 0xd7af);

            public int Start { get; }

            public int End { get; }

            public int Size => End - Start + 1;

            public CharacterRange(int start, int end)
            {
                Start = start;
                End = end;
            }

            public CharacterRange(int single) : this(single, single)
            {
            }
        }
    }
}
