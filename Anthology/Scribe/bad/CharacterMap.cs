using Scribe;
using System.Collections.Generic;

namespace Scribe.Internal
{
    internal class CharacterMap
    {
        Dictionary<CodePoint, int> table;

        CharacterMap(Dictionary<CodePoint, int> table)
        {
            this.table = table;
        }

        public int Lookup(CodePoint codePoint)
        {
            int index;
            if (table.TryGetValue(codePoint, out index))
                return index;
            return -1;
        }

        public static CharacterMap ReadCmap(DataReader reader, TableRecord[] tables)
        {
            SfntTables.SeekToTable(reader, tables, FourCC.Cmap, required: true);

            // skip version
            var cmapOffset = reader.Position;
            reader.Skip(sizeof(short));

            // read all of the subtable headers
            var subtableCount = reader.ReadUInt16BE();
            var subtableHeaders = new CmapSubtableHeader[subtableCount];
            for (int i = 0; i < subtableHeaders.Length; i++)
            {
                subtableHeaders[i] = new CmapSubtableHeader {
                    PlatformID = reader.ReadUInt16BE(),
                    EncodingID = reader.ReadUInt16BE(),
                    Offset = reader.ReadUInt32BE()
                };
            }

            // search for a "full" Unicode table first (format 12)
            var chosenSubtableOffset = 0u;
            var chosenFormat = 0;
            for (int i = 0; i < subtableHeaders.Length; i++)
            {
                var platform = subtableHeaders[i].PlatformID;
                var encoding = subtableHeaders[i].EncodingID;
                if (platform == PlatformID.Microsoft && encoding == WindowsEncoding.UnicodeFull ||
                    platform == PlatformID.Unicode && encoding == UnicodeEncoding.Unicode32)
                {

                    reader.Seek(cmapOffset + subtableHeaders[i].Offset);
                    var format = reader.ReadUInt16BE();
                    if (format == 12)
                    {
                        chosenSubtableOffset = subtableHeaders[i].Offset;
                        chosenFormat = format;
                        break;
                    }
                }
            }

            // if no format 12 table, look for format 4
            if (chosenSubtableOffset == 0)
            {
                for (int i = 0; i < subtableHeaders.Length; i++)
                {
                    var platform = subtableHeaders[i].PlatformID;
                    var encoding = subtableHeaders[i].EncodingID;
                    if (platform == PlatformID.Microsoft && encoding == WindowsEncoding.UnicodeBmp ||
                         platform == PlatformID.Unicode)
                    {

                        reader.Seek(cmapOffset + subtableHeaders[i].Offset);
                        var format = reader.ReadUInt16BE();
                        if (format == 4)
                        {
                            chosenSubtableOffset = subtableHeaders[i].Offset;
                            chosenFormat = format;
                            break;
                        }
                    }
                }
            }

            // if still not found, try formats 0 or 6
            if (chosenSubtableOffset == 0)
            {
                for (int i = 0; i < subtableHeaders.Length; i++)
                {
                    reader.Seek(cmapOffset + subtableHeaders[i].Offset);
                    var format = reader.ReadUInt16BE();
                    if (format == 0 || format == 6)
                    {
                        chosenSubtableOffset = subtableHeaders[i].Offset;
                        chosenFormat = format;
                        break;
                    }
                }
            }

            // no unicode support at all is an error
            if (chosenSubtableOffset == 0)
                throw new InvalidFontException("Font does not support Unicode or has unsupported cmap formats.");

            // jump to our chosen table and parse according to its format
            reader.Seek(cmapOffset + chosenSubtableOffset);
            var finalFormat = reader.ReadUInt16BE();
            switch (finalFormat)
            {
                case 0: return ReadCmapFormat0(reader);
                case 4: return ReadCmapFormat4(reader);
                case 6: return ReadCmapFormat6(reader);
                case 12: return ReadCmapFormat12(reader);
                default: throw new InvalidFontException($"Unsupported cmap format: {finalFormat}.");
            }
        }

        // Format 0: Byte encoding table
        static CharacterMap ReadCmapFormat0(DataReader reader)
        {
            // Skip over length and language
            reader.Skip(sizeof(short) * 2);

            var table = new Dictionary<CodePoint, int>();

            // Format 0 is a simple array of 256 bytes
            for (int i = 0; i < 256; i++)
            {
                var glyphIndex = reader.ReadByte();
                if (glyphIndex != 0)
                {
                    table.Add((CodePoint)i, glyphIndex);
                }
            }

            return new CharacterMap(table);
        }

        // Format 6: Trimmed table mapping
        static CharacterMap ReadCmapFormat6(DataReader reader)
        {
            // Skip over length and language
            reader.Skip(sizeof(short) * 2);

            var firstCode = reader.ReadUInt16BE();
            var entryCount = reader.ReadUInt16BE();

            var table = new Dictionary<CodePoint, int>();

            for (int i = 0; i < entryCount; i++)
            {
                var codePoint = firstCode + i;
                var glyphIndex = reader.ReadUInt16BE();
                if (glyphIndex != 0)
                {
                    if (!table.ContainsKey((CodePoint)codePoint))
                        table.Add((CodePoint)codePoint, glyphIndex);
                }
            }

            return new CharacterMap(table);
        }

        // Format 12: Segmented coverage (32 bit)
        static CharacterMap ReadCmapFormat12(DataReader reader)
        {
            // Format 12 is a 32-bit format, so we need to read 16 bits to get the full format (usually 12.0)
            reader.Skip(sizeof(short));

            // Skip over length (32-bit) and language (32-bit)
            reader.Skip(sizeof(int) * 2);

            var numGroups = reader.ReadUInt32BE();

            var table = new Dictionary<CodePoint, int>();

            for (uint i = 0; i < numGroups; i++)
            {
                var startCharCode = reader.ReadUInt32BE();
                var endCharCode = reader.ReadUInt32BE();
                var startGlyphID = reader.ReadUInt32BE();

                for (uint j = startCharCode, glyphIndex = startGlyphID; j <= endCharCode; j++, glyphIndex++)
                {
                    // Only add valid Unicode code points
                    if (glyphIndex != 0 && j <= 0x10FFFF)
                    {
                        if (!table.ContainsKey((CodePoint)j))
                            table.Add((CodePoint)j, (int)glyphIndex);
                    }
                }
            }

            return new CharacterMap(table);
        }

        unsafe static CharacterMap ReadCmapFormat4(DataReader reader)
        {
            // skip over length and language
            reader.Skip(sizeof(short) * 2);

            // figure out how many segments we have
            var segmentCount = reader.ReadUInt16BE() / 2;

            // skip over searchRange, entrySelector, and rangeShift
            reader.Skip(sizeof(short) * 3);

            // read in segment ranges
            var endCount = stackalloc int[segmentCount];
            for (int i = 0; i < segmentCount; i++)
                endCount[i] = reader.ReadUInt16BE();

            reader.Skip(sizeof(short));     // padding

            var startCount = stackalloc int[segmentCount];
            for (int i = 0; i < segmentCount; i++)
                startCount[i] = reader.ReadUInt16BE();

            var idDelta = stackalloc int[segmentCount];
            for (int i = 0; i < segmentCount; i++)
                idDelta[i] = reader.ReadInt16BE();

            // build table from each segment
            var table = new Dictionary<CodePoint, int>();
            for (int i = 0; i < segmentCount; i++)
            {
                // read the "idRangeOffset" for the current segment
                // if nonzero, we need to jump into the glyphIdArray to figure out the mapping
                // the layout is bizarre; see the OpenType spec for details
                var idRangeOffset = reader.ReadUInt16BE();
                if (idRangeOffset != 0)
                {
                    var currentOffset = reader.Position;
                    reader.Seek(currentOffset + idRangeOffset - sizeof(ushort));

                    var end = endCount[i];
                    var delta = idDelta[i];
                    for (var codepoint = startCount[i]; codepoint <= end; codepoint++)
                    {
                        var glyphId = reader.ReadUInt16BE();
                        if (glyphId != 0)
                        {
                            var glyphIndex = glyphId + delta & 0xFFFF;
                            if (glyphIndex != 0)
                            {
                                if (!table.ContainsKey((CodePoint)codepoint))
                                    table.Add((CodePoint)codepoint, glyphIndex);
                            }
                        }
                    }

                    reader.Seek(currentOffset);
                }
                else
                {
                    // otherwise, do a straight iteration through the segment
                    var end = endCount[i];
                    var delta = idDelta[i];
                    for (var codepoint = startCount[i]; codepoint <= end; codepoint++)
                    {
                        var glyphIndex = codepoint + delta & 0xFFFF;
                        if (glyphIndex != 0)
                        {
                            if (!table.ContainsKey((CodePoint)codepoint))
                                table.Add((CodePoint)codepoint, glyphIndex);
                        }
                    }
                }
            }

            return new CharacterMap(table);
        }

        struct CmapSubtableHeader
        {
            public int PlatformID;
            public int EncodingID;
            public uint Offset;
        }
    }
}
