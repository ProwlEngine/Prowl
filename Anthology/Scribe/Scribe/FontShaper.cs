using System.Collections.Generic;
using Prowl.Scribe.Internal;
using static Prowl.Scribe.Internal.Common;

namespace Prowl.Scribe
{
    /// <summary>
    /// A glyph in a shaping buffer: a glyph index plus the source character cluster it represents.
    /// A cluster collapses to one entry for ligatures (CharCount &gt; 1) and can fan out for
    /// decompositions (CharCount 0 on the trailing pieces).
    /// </summary>
    internal struct GsubGlyph
    {
        public int Glyph;
        public int Cluster;    // source char index where the cluster starts
        public int CharCount;  // number of source chars in this cluster

        public GsubGlyph(int glyph, int cluster, int charCount)
        {
            Glyph = glyph;
            Cluster = cluster;
            CharCount = charCount;
        }
    }

    /// <summary>A fully shaped, positioned glyph ready for the layout engine to place.</summary>
    internal struct ShapedGlyph
    {
        public AtlasGlyph Glyph;
        public float Advance;   // scaled pixels, including kerning to the next glyph in the run
        public int Cluster;     // source char index where the cluster starts
        public int CharCount;   // number of source chars in this cluster
    }

    // GSUB glyph substitution (ligatures, ccmp, etc.) plus the shared OpenType layout-table
    // navigation (feature -> lookup resolution, ValueRecords) used by both GSUB shaping here and
    // the GPOS kerning in FontFile.cs.
    public partial class FontFile
    {
        private Dictionary<string, List<int>> _gsubLookupCache;

        // Lookup-list indices of a GSUB feature for the default script (latn/DFLT), resolved once.
        // Returns null when the font has no GSUB table.
        private List<int>? GetGsubLookups(string feature)
        {
            if (this.gsub == 0)
                return null;
            _gsubLookupCache ??= new Dictionary<string, List<int>>(4);
            if (_gsubLookupCache.TryGetValue(feature, out var cached))
                return cached;

            var list = new List<int>();
            var table = this.data + this.gsub;
            if (!GetFeatureLookups(table, "latn", null, feature, list))
                GetFeatureLookups(table, "DFLT", null, feature, list);
            _gsubLookupCache[feature] = list;
            return list;
        }

        /// <summary>
        /// Applies the default GSUB substitution features (ccmp, then liga, then rlig) to a shaping
        /// buffer in place. Glyph indices may be replaced (single), expanded (multiple), or merged
        /// (ligature), with source clusters tracked so hit-testing can map back to characters.
        /// </summary>
        internal void ApplyGsub(List<GsubGlyph> buf)
        {
            if (this.gsub == 0 || buf.Count == 0)
                return;
            ApplyGsubFeature(buf, "ccmp");
            ApplyGsubFeature(buf, "liga");
            ApplyGsubFeature(buf, "rlig");
        }

        private void ApplyGsubFeature(List<GsubGlyph> buf, string feature)
        {
            var lookups = GetGsubLookups(feature);
            if (lookups == null || lookups.Count == 0)
                return;
            for (int i = 0; i < lookups.Count; i++)
                ApplyGsubLookup(buf, lookups[i]);
        }

        private void ApplyGsubLookup(List<GsubGlyph> buf, int lookupIndex)
        {
            var table = this.data + this.gsub;
            var lookup = GetLookup(table, lookupIndex, out int lookupType, out int subtableCount);
            if (lookup.IsNull)
                return;

            int i = 0;
            while (i < buf.Count)
            {
                int advance = -1;
                for (int s = 0; s < subtableCount && advance < 0; s++)
                {
                    var st = GetSubtable(lookup, s);
                    int type = lookupType;
                    if (type == 7) // Extension substitution - unwrap to the real lookup type/subtable.
                    {
                        type = ttUSHORT(st + 2);
                        st = st + (int)ttULONG(st + 4);
                    }

                    switch (type)
                    {
                        case 1:
                            if (ApplySingle(buf, i, st)) advance = 1;
                            break;
                        case 2:
                        {
                            int produced = ApplyMultiple(buf, i, st);
                            if (produced >= 0) advance = produced; // 0 = glyph deleted (don't skip ahead)
                            break;
                        }
                        case 4:
                            if (ApplyLigature(buf, i, st)) advance = 1;
                            break;
                    }
                }
                i += advance >= 0 ? advance : 1;
                if (advance == 0) i++; // safety: a deletion still moves forward eventually
            }
        }

        // GSUB LookupType 1 - Single substitution (replace one glyph with one glyph).
        private bool ApplySingle(List<GsubGlyph> buf, int i, FakePtr<byte> st)
        {
            int format = ttUSHORT(st);
            int cov = stbtt__GetCoverageIndex(st + ttUSHORT(st + 2), buf[i].Glyph);
            if (cov == -1)
                return false;

            int newGlyph;
            if (format == 1)
                newGlyph = (buf[i].Glyph + ttSHORT(st + 4)) & 0xFFFF;
            else // format 2
            {
                int glyphCount = ttUSHORT(st + 4);
                if (cov >= glyphCount) return false;
                newGlyph = ttUSHORT(st + 6 + 2 * cov);
            }

            var g = buf[i];
            g.Glyph = newGlyph;
            buf[i] = g;
            return true;
        }

        // GSUB LookupType 2 - Multiple substitution (one glyph -> sequence). Returns produced count,
        // or -1 if not applied.
        private int ApplyMultiple(List<GsubGlyph> buf, int i, FakePtr<byte> st)
        {
            if (ttUSHORT(st) != 1)
                return -1;
            int cov = stbtt__GetCoverageIndex(st + ttUSHORT(st + 2), buf[i].Glyph);
            if (cov == -1)
                return -1;
            int seqCount = ttUSHORT(st + 4);
            if (cov >= seqCount)
                return -1;

            var seq = st + ttUSHORT(st + 6 + 2 * cov);
            int glyphCount = ttUSHORT(seq);
            int cluster = buf[i].Cluster;
            int charCount = buf[i].CharCount;

            buf.RemoveAt(i);
            for (int k = 0; k < glyphCount; k++)
            {
                int g = ttUSHORT(seq + 2 + 2 * k);
                // The whole source cluster maps to the first produced glyph; the rest carry 0 chars.
                buf.Insert(i + k, new GsubGlyph(g, cluster, k == 0 ? charCount : 0));
            }
            return glyphCount;
        }

        // GSUB LookupType 4 - Ligature substitution (sequence -> one glyph).
        private bool ApplyLigature(List<GsubGlyph> buf, int i, FakePtr<byte> st)
        {
            if (ttUSHORT(st) != 1)
                return false;
            int cov = stbtt__GetCoverageIndex(st + ttUSHORT(st + 2), buf[i].Glyph);
            if (cov == -1)
                return false;
            int ligSetCount = ttUSHORT(st + 4);
            if (cov >= ligSetCount)
                return false;

            var ligSet = st + ttUSHORT(st + 6 + 2 * cov);
            int ligCount = ttUSHORT(ligSet);
            for (int l = 0; l < ligCount; l++)
            {
                var lig = ligSet + ttUSHORT(ligSet + 2 + 2 * l);
                int ligGlyph = ttUSHORT(lig);
                int componentCount = ttUSHORT(lig + 2); // includes the first (covered) glyph
                if (componentCount < 1 || i + componentCount > buf.Count)
                    continue;

                bool match = true;
                for (int c = 1; c < componentCount; c++)
                {
                    int comp = ttUSHORT(lig + 4 + 2 * (c - 1));
                    if (buf[i + c].Glyph != comp) { match = false; break; }
                }
                if (!match)
                    continue;

                int clusterStart = buf[i].Cluster;
                int clusterChars = 0;
                for (int c = 0; c < componentCount; c++)
                    clusterChars += buf[i + c].CharCount;

                if (componentCount > 1)
                    buf.RemoveRange(i + 1, componentCount - 1);
                buf[i] = new GsubGlyph(ligGlyph, clusterStart, clusterChars);
                return true;
            }
            return false;
        }

        // GSUB and GPOS share the same v1.0 header: ScriptList@4, FeatureList@6, LookupList@8. All
        // offsets are relative to the table base passed in. Coverage/ClassDef navigation reuses the
        // existing Common.stbtt__GetCoverageIndex / stbtt__GetGlyphClass. Based on opentype.js.

        // Appends the lookup-list indices of <feature> under (script, lang) into outLookups. Falls
        // back script -> DFLT and language -> the script's default LangSys. Returns true if found.
        private static bool GetFeatureLookups(FakePtr<byte> table, string script, string? lang, string feature, List<int> outLookups)
        {
            outLookups.Clear();
            if (table.IsNull)
                return false;

            var scriptList = table + ttUSHORT(table + 4);
            var featureList = table + ttUSHORT(table + 6);

            var scriptTable = FindScript(scriptList, script);
            if (scriptTable.IsNull && script != "DFLT")
                scriptTable = FindScript(scriptList, "DFLT");
            if (scriptTable.IsNull)
                return false;

            var langSys = FindLangSys(scriptTable, lang);
            if (langSys.IsNull)
                return false;

            int requiredFeature = ttUSHORT(langSys + 2);
            int featureCount = ttUSHORT(langSys + 4);

            bool found = false;
            if (requiredFeature != 0xFFFF)
                found |= AddFeatureLookups(featureList, requiredFeature, feature, outLookups);
            for (int i = 0; i < featureCount; i++)
                found |= AddFeatureLookups(featureList, ttUSHORT(langSys + 6 + 2 * i), feature, outLookups);
            return found;
        }

        private static FakePtr<byte> FindScript(FakePtr<byte> scriptList, string tag)
        {
            int count = ttUSHORT(scriptList);
            for (int i = 0; i < count; i++)
            {
                var rec = scriptList + 2 + 6 * i; // ScriptRecord: tag(4) + offset(2)
                if (TagEquals(rec, tag))
                    return scriptList + ttUSHORT(rec + 4);
            }
            return FakePtr<byte>.Null;
        }

        private static FakePtr<byte> FindLangSys(FakePtr<byte> scriptTable, string? lang)
        {
            if (!string.IsNullOrEmpty(lang) && lang != "dflt")
            {
                int count = ttUSHORT(scriptTable + 2);
                for (int i = 0; i < count; i++)
                {
                    var rec = scriptTable + 4 + 6 * i; // LangSysRecord: tag(4) + offset(2)
                    if (TagEquals(rec, lang))
                        return scriptTable + ttUSHORT(rec + 4);
                }
            }
            int defOffset = ttUSHORT(scriptTable); // defaultLangSysOffset
            return defOffset == 0 ? FakePtr<byte>.Null : scriptTable + defOffset;
        }

        private static bool AddFeatureLookups(FakePtr<byte> featureList, int featureIndex, string feature, List<int> outLookups)
        {
            int featureCount = ttUSHORT(featureList);
            if (featureIndex < 0 || featureIndex >= featureCount)
                return false;
            var rec = featureList + 2 + 6 * featureIndex; // FeatureRecord: tag(4) + offset(2)
            if (!TagEquals(rec, feature))
                return false;
            var featureTable = featureList + ttUSHORT(rec + 4);
            int lookupCount = ttUSHORT(featureTable + 2);
            for (int i = 0; i < lookupCount; i++)
                outLookups.Add(ttUSHORT(featureTable + 4 + 2 * i));
            return true;
        }

        // Resolves a lookup index in the table's LookupList to its table pointer + type + subtable count.
        private static FakePtr<byte> GetLookup(FakePtr<byte> table, int lookupIndex, out int lookupType, out int subtableCount)
        {
            var lookupList = table + ttUSHORT(table + 8);
            int count = ttUSHORT(lookupList);
            if (lookupIndex < 0 || lookupIndex >= count)
            {
                lookupType = 0; subtableCount = 0;
                return FakePtr<byte>.Null;
            }
            var lookup = lookupList + ttUSHORT(lookupList + 2 + 2 * lookupIndex);
            lookupType = ttUSHORT(lookup);
            subtableCount = ttUSHORT(lookup + 4);
            return lookup;
        }

        private static FakePtr<byte> GetSubtable(FakePtr<byte> lookup, int subtableIndex)
            => lookup + ttUSHORT(lookup + 6 + 2 * subtableIndex);

        // A GPOS ValueRecord stores one 2-byte field per set bit of valueFormat.
        private static int ValueRecordSize(int valueFormat) => 2 * PopCount(valueFormat & 0xFF);

        // The xAdvance of a ValueRecord (font units), or 0 when the format has no xAdvance field.
        // xAdvance (0x4) follows xPlacement (0x1) and yPlacement (0x2) when those are present.
        private static int ValueXAdvance(FakePtr<byte> record, int valueFormat)
        {
            if ((valueFormat & 0x0004) == 0)
                return 0;
            return ttSHORT(record + 2 * PopCount(valueFormat & 0x0003));
        }

        private static int PopCount(int v)
        {
            int c = 0;
            while (v != 0) { c += v & 1; v >>= 1; }
            return c;
        }

        private static bool TagEquals(FakePtr<byte> p, string tag)
            => p[0] == (byte)tag[0] && p[1] == (byte)tag[1] && p[2] == (byte)tag[2] && p[3] == (byte)tag[3];
    }
}
