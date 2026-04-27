using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Prowl.Scribe
{
    /// <summary>
    /// Style flags applied to a range of visible characters.
    /// Multiple flags may stack via nested tags.
    /// </summary>
    [Flags]
    public enum RichStyleFlags : byte
    {
        None = 0,
        Bold = 1 << 0,
        Italic = 1 << 1,
        Underline = 1 << 2,
        Strike = 1 << 3,
        Mono = 1 << 4,
        Link = 1 << 5,
    }

    public enum RichEffectKind : byte
    {
        Shake,
        Wave,
        Rainbow,
        Pulse,
        Fade,
        Jitter,
        Typewriter,
    }

    /// <summary>A static styling override that applies to a range of visible characters.</summary>
    public struct RichStyleSpan
    {
        /// <summary>Inclusive start (visible char index).</summary>
        public int Start;
        /// <summary>Exclusive end (visible char index).</summary>
        public int End;
        public RichStyleFlags Flags;
        /// <summary>Pixel size override; <see cref="float.NaN"/> means "inherit".</summary>
        public float PixelSize;
        /// <summary>Color override; null means "inherit".</summary>
        public FontColor? Color;
        /// <summary>Optional link target string (set when <see cref="Flags"/> contains <see cref="RichStyleFlags.Link"/>).</summary>
        public string LinkHref;
    }

    /// <summary>An animated effect span over a range of visible characters.</summary>
    public struct RichEffectSpan
    {
        public RichEffectKind Kind;
        /// <summary>Inclusive start (visible char index).</summary>
        public int Start;
        /// <summary>Exclusive end (visible char index).</summary>
        public int End;

        // Generic parameter slots — interpretation depends on Kind. See <see cref="RichTextEffects"/>.
        public float P0, P1, P2, P3;
    }

    /// <summary>Result of parsing rich-text source.</summary>
    public class RichTextParseResult
    {
        /// <summary>Visible text after stripping tags.</summary>
        public string VisibleText;
        /// <summary>For each visible char index, the source-text index of its first source char (for hit-testing back to source).</summary>
        public int[] SourceMap;
        public List<RichStyleSpan> Styles;
        public List<RichEffectSpan> Effects;
        /// <summary>Diagnostics for malformed tags. Non-fatal — the source is still parsed leniently.</summary>
        public List<string> Warnings;
    }

    /// <summary>
    /// Unity-style rich-text tag parser.
    ///
    /// Supported tag forms:
    ///   &lt;b&gt; &lt;/b&gt;                              — boolean style on/off
    ///   &lt;color=#ff0&gt; &lt;color=red&gt;                — single value (interpreted by context)
    ///   &lt;shake amp=2 freq=8&gt;                       — multiple key=value attributes
    ///   &lt;link=https://...&gt;text&lt;/link&gt;             — single value as href
    ///
    /// Unknown tags are emitted as literal text. Use a literal '&lt;' in source by escaping it as
    /// "\&lt;" (backslash + &lt;) — the backslash is consumed and the &lt; is treated as a normal char.
    /// </summary>
    public static class RichTextParser
    {
        public static RichTextParseResult Parse(string source)
        {
            var result = new RichTextParseResult {
                VisibleText = string.Empty,
                Styles = new List<RichStyleSpan>(),
                Effects = new List<RichEffectSpan>(),
                Warnings = new List<string>(),
            };

            if (string.IsNullOrEmpty(source))
            {
                result.SourceMap = Array.Empty<int>();
                return result;
            }

            var sb = new StringBuilder(source.Length);
            var sourceMap = new List<int>(source.Length);

            // Open tag tracking — when a closing tag arrives we pop the most recent matching open
            // and emit a finalized span covering [openStart..currentVisibleIndex).
            var openStyles = new Stack<OpenStyle>();
            var openEffects = new Stack<OpenEffect>();

            int i = 0;
            int len = source.Length;
            while (i < len)
            {
                char c = source[i];

                // Backslash escape for a literal '<' (e.g. "\<not a tag>")
                if (c == '\\' && i + 1 < len && source[i + 1] == '<')
                {
                    sb.Append('<');
                    sourceMap.Add(i);
                    i += 2;
                    continue;
                }

                if (c != '<')
                {
                    sb.Append(c);
                    sourceMap.Add(i);
                    i++;
                    continue;
                }

                // Try to parse a tag at i. If it doesn't look like one, fall back to literal '<'.
                if (!TryParseTagAt(source, i, out int tagEnd, out string tagName, out bool isClosing,
                                   out string singleValue, out List<KeyValuePair<string, string>> attrs))
                {
                    sb.Append('<');
                    sourceMap.Add(i);
                    i++;
                    continue;
                }

                int visibleIndex = sb.Length;
                if (isClosing)
                    HandleClose(tagName, visibleIndex, openStyles, openEffects, result);
                else
                    HandleOpen(tagName, visibleIndex, singleValue, attrs, openStyles, openEffects, result);

                i = tagEnd;
            }

            // Anything still open at EOF — close it implicitly at the end of visible text.
            int finalVisible = sb.Length;
            while (openStyles.Count > 0)
            {
                var os = openStyles.Pop();
                if (os.Start < finalVisible)
                    EmitStyle(result, os, finalVisible);
                result.Warnings.Add($"Unclosed style tag <{os.TagName}>");
            }
            while (openEffects.Count > 0)
            {
                var oe = openEffects.Pop();
                if (oe.Start < finalVisible)
                    EmitEffect(result, oe, finalVisible);
                result.Warnings.Add($"Unclosed effect tag <{oe.TagName}>");
            }

            result.VisibleText = sb.ToString();
            result.SourceMap = sourceMap.ToArray();
            return result;
        }

        // -----------------------------------------------------------------------------------
        // Open / close handling
        // -----------------------------------------------------------------------------------

        private struct OpenStyle
        {
            public string TagName;
            public int Start;
            public RichStyleFlags Flag;
            public bool SetSize; public float PixelSize;
            public bool SetColor; public FontColor Color;
            public string LinkHref;
        }

        private struct OpenEffect
        {
            public string TagName;
            public int Start;
            public RichEffectKind Kind;
            public float P0, P1, P2, P3;
        }

        private static void HandleOpen(string tag, int visibleIndex, string singleValue,
            List<KeyValuePair<string, string>> attrs,
            Stack<OpenStyle> openStyles, Stack<OpenEffect> openEffects, RichTextParseResult result)
        {
            switch (tag)
            {
                case "b": openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.Bold)); return;
                case "i": openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.Italic)); return;
                case "u": openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.Underline)); return;
                case "s": openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.Strike)); return;
                case "mono":
                case "code": openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.Mono)); return;

                case "color":
                {
                    if (string.IsNullOrEmpty(singleValue) || !TryParseColor(singleValue, out var col))
                    {
                        result.Warnings.Add($"<color> expected a value (e.g. <color=#ff0> or <color=red>)");
                        // Push a no-op so matching </color> still pops cleanly
                        openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.None));
                        return;
                    }
                    var os = NewStyle(tag, visibleIndex, RichStyleFlags.None);
                    os.SetColor = true; os.Color = col;
                    openStyles.Push(os);
                    return;
                }

                case "size":
                {
                    if (string.IsNullOrEmpty(singleValue) || !TryParseSize(singleValue, out var px))
                    {
                        result.Warnings.Add($"<size> expected a value (e.g. <size=24> or <size=120%>)");
                        openStyles.Push(NewStyle(tag, visibleIndex, RichStyleFlags.None));
                        return;
                    }
                    var os = NewStyle(tag, visibleIndex, RichStyleFlags.None);
                    os.SetSize = true; os.PixelSize = px;
                    openStyles.Push(os);
                    return;
                }

                case "font":
                {
                    // <font=mono> is the only built-in mapping; other names are accepted but ignored
                    // (host code can interpret link/font names by post-processing the styles list).
                    var os = NewStyle(tag, visibleIndex, RichStyleFlags.None);
                    if (!string.IsNullOrEmpty(singleValue) && singleValue.Equals("mono", StringComparison.OrdinalIgnoreCase))
                        os.Flag = RichStyleFlags.Mono;
                    openStyles.Push(os);
                    return;
                }

                case "link":
                {
                    var os = NewStyle(tag, visibleIndex, RichStyleFlags.Link);
                    os.LinkHref = singleValue;
                    openStyles.Push(os);
                    return;
                }

                case "shake":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Shake, attrs, singleValue));
                    return;
                case "wave":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Wave, attrs, singleValue));
                    return;
                case "rainbow":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Rainbow, attrs, singleValue));
                    return;
                case "pulse":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Pulse, attrs, singleValue));
                    return;
                case "fade":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Fade, attrs, singleValue));
                    return;
                case "jitter":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Jitter, attrs, singleValue));
                    return;
                case "typewriter":
                    openEffects.Push(BuildEffect(tag, visibleIndex, RichEffectKind.Typewriter, attrs, singleValue));
                    return;

                default:
                    result.Warnings.Add($"Unknown tag <{tag}>");
                    return;
            }
        }

        private static OpenStyle NewStyle(string tag, int start, RichStyleFlags flag)
            => new OpenStyle { TagName = tag, Start = start, Flag = flag };

        private static OpenEffect BuildEffect(string tag, int start, RichEffectKind kind,
            List<KeyValuePair<string, string>> attrs, string singleValue)
        {
            // NaN sentinel = "not set"; lets the evaluator distinguish "use default" from
            // an explicit user value of 0 (e.g., <shake amp=0>).
            var oe = new OpenEffect {
                TagName = tag, Start = start, Kind = kind,
                P0 = float.NaN, P1 = float.NaN, P2 = float.NaN, P3 = float.NaN,
            };
            // Some effects accept a single value as their primary parameter:
            //   <wave=4>      → amp = 4
            //   <typewriter=40> → speed = 40 (chars per second)
            //   <fade=2>      → speed = 2
            //   <pulse=3>     → speed = 3
            //   <shake=2>     → amp = 2
            //   <jitter=1>    → amp = 1
            //   <rainbow=1>   → speed = 1
            if (!string.IsNullOrEmpty(singleValue) && float.TryParse(singleValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                switch (kind)
                {
                    case RichEffectKind.Shake:
                    case RichEffectKind.Wave:
                    case RichEffectKind.Jitter:
                        oe.P0 = v; break;
                    case RichEffectKind.Rainbow:
                    case RichEffectKind.Pulse:
                    case RichEffectKind.Fade:
                    case RichEffectKind.Typewriter:
                        oe.P0 = v; break;
                }
            }

            if (attrs != null)
            {
                foreach (var kv in attrs)
                    ApplyEffectAttr(ref oe, kv.Key, kv.Value);
            }

            return oe;
        }

        private static void ApplyEffectAttr(ref OpenEffect oe, string key, string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
            switch (oe.Kind)
            {
                case RichEffectKind.Shake:
                    if (key == "amp") oe.P0 = v;
                    else if (key == "freq") oe.P1 = v;
                    break;
                case RichEffectKind.Wave:
                    if (key == "amp") oe.P0 = v;
                    else if (key == "freq") oe.P1 = v;
                    else if (key == "phase") oe.P2 = v;
                    break;
                case RichEffectKind.Rainbow:
                    if (key == "speed") oe.P0 = v;
                    else if (key == "spread") oe.P1 = v;
                    else if (key == "sat") oe.P2 = v;
                    else if (key == "value" || key == "val") oe.P3 = v;
                    break;
                case RichEffectKind.Pulse:
                    if (key == "speed") oe.P0 = v;
                    else if (key == "amp") oe.P1 = v;
                    break;
                case RichEffectKind.Fade:
                    if (key == "speed") oe.P0 = v;
                    break;
                case RichEffectKind.Jitter:
                    if (key == "amp") oe.P0 = v;
                    else if (key == "freq") oe.P1 = v;
                    break;
                case RichEffectKind.Typewriter:
                    if (key == "speed") oe.P0 = v;
                    else if (key == "fade") oe.P1 = v;
                    break;
            }
        }

        private static void HandleClose(string tag, int visibleIndex,
            Stack<OpenStyle> openStyles, Stack<OpenEffect> openEffects, RichTextParseResult result)
        {
            // Effects and styles share namespace by tag name. Try effect stack first if the name
            // is one of our effect tags — otherwise it's a style.
            switch (tag)
            {
                case "shake":
                case "wave":
                case "rainbow":
                case "pulse":
                case "fade":
                case "jitter":
                case "typewriter":
                    if (TryPop(openEffects, tag, out var oe))
                        EmitEffect(result, oe, visibleIndex);
                    else
                        result.Warnings.Add($"</{tag}> with no matching open");
                    return;
            }

            // Style tags (b/i/u/s/color/size/font/link/code/mono)
            if (TryPop(openStyles, tag, out var os))
                EmitStyle(result, os, visibleIndex);
            else
                result.Warnings.Add($"</{tag}> with no matching open");
        }

        private static bool TryPop<T>(Stack<T> stack, string tag, out T popped) where T : struct
        {
            // Pop the most recent matching tag — tolerates incorrectly-nested tags by walking down.
            // (We snapshot then restore in original order if not found, to keep behavior deterministic.)
            if (stack.Count == 0) { popped = default; return false; }
            var buffer = new List<T>();
            while (stack.Count > 0)
            {
                var top = stack.Pop();
                string name = (top is OpenStyle s) ? s.TagName : ((OpenEffect)(object)top).TagName;
                if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
                {
                    // Restore intervening items in original order
                    for (int k = buffer.Count - 1; k >= 0; k--) stack.Push(buffer[k]);
                    popped = top;
                    return true;
                }
                buffer.Add(top);
            }
            // Not found — restore everything and signal failure
            for (int k = buffer.Count - 1; k >= 0; k--) stack.Push(buffer[k]);
            popped = default;
            return false;
        }

        private static void EmitStyle(RichTextParseResult result, OpenStyle os, int end)
        {
            if (end <= os.Start) return; // empty span
            result.Styles.Add(new RichStyleSpan {
                Start = os.Start,
                End = end,
                Flags = os.Flag,
                PixelSize = os.SetSize ? os.PixelSize : float.NaN,
                Color = os.SetColor ? (FontColor?)os.Color : null,
                LinkHref = os.LinkHref,
            });
        }

        private static void EmitEffect(RichTextParseResult result, OpenEffect oe, int end)
        {
            if (end <= oe.Start) return;
            result.Effects.Add(new RichEffectSpan {
                Kind = oe.Kind,
                Start = oe.Start,
                End = end,
                P0 = oe.P0, P1 = oe.P1, P2 = oe.P2, P3 = oe.P3,
            });
        }

        // -----------------------------------------------------------------------------------
        // Tag tokenizer
        // -----------------------------------------------------------------------------------

        private static bool TryParseTagAt(string s, int start, out int end, out string name,
            out bool isClosing, out string singleValue, out List<KeyValuePair<string, string>> attrs)
        {
            end = start; name = null; isClosing = false; singleValue = null; attrs = null;
            int len = s.Length;
            if (start >= len || s[start] != '<') return false;

            int i = start + 1;
            if (i >= len) return false;

            if (s[i] == '/') { isClosing = true; i++; }

            // Read tag name: [a-zA-Z][a-zA-Z0-9_-]*
            int nameStart = i;
            if (i >= len || !IsTagNameStart(s[i])) return false;
            while (i < len && IsTagNamePart(s[i])) i++;
            if (i == nameStart) return false;
            name = s.Substring(nameStart, i - nameStart).ToLowerInvariant();

            // Optional single-value form: <name=value>
            if (i < len && s[i] == '=')
            {
                i++;
                if (!ReadValue(s, ref i, out singleValue)) return false;
            }

            // Optional space-separated key=value attrs: <name k=v k2="v 2">
            while (i < len && (s[i] == ' ' || s[i] == '\t'))
            {
                i++;
                if (i >= len) return false;
                if (s[i] == '>') break;

                int kStart = i;
                if (!IsTagNameStart(s[i])) return false;
                while (i < len && IsTagNamePart(s[i])) i++;
                string key = s.Substring(kStart, i - kStart).ToLowerInvariant();
                if (i >= len || s[i] != '=') return false;
                i++;
                if (!ReadValue(s, ref i, out string val)) return false;

                if (attrs == null) attrs = new List<KeyValuePair<string, string>>(2);
                attrs.Add(new KeyValuePair<string, string>(key, val));
            }

            if (i >= len || s[i] != '>') return false;
            end = i + 1;
            return true;
        }

        private static bool ReadValue(string s, ref int i, out string value)
        {
            value = null;
            int len = s.Length;
            if (i >= len) return false;

            // Quoted: "..." or '...'
            if (s[i] == '"' || s[i] == '\'')
            {
                char q = s[i++];
                int vs = i;
                while (i < len && s[i] != q) i++;
                if (i >= len) return false;
                value = s.Substring(vs, i - vs);
                i++;
                return true;
            }

            // Bare: read until whitespace or '>'
            int start = i;
            while (i < len && s[i] != ' ' && s[i] != '\t' && s[i] != '>')
                i++;
            if (i == start) return false;
            value = s.Substring(start, i - start);
            return true;
        }

        private static bool IsTagNameStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        private static bool IsTagNamePart(char c) => IsTagNameStart(c) || (c >= '0' && c <= '9') || c == '-';

        // -----------------------------------------------------------------------------------
        // Value parsers
        // -----------------------------------------------------------------------------------

        private static bool TryParseColor(string s, out FontColor c)
        {
            c = default;
            if (string.IsNullOrEmpty(s)) return false;

            if (s[0] == '#')
            {
                try { c = FontColor.FromHex(s); return true; } catch { return false; }
            }

            // Named color via reflection of FontColor static properties
            var prop = typeof(FontColor).GetProperty(s, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null && prop.PropertyType == typeof(FontColor))
            {
                c = (FontColor)prop.GetValue(null);
                return true;
            }
            return false;
        }

        private static bool TryParseSize(string s, out float px)
        {
            px = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            // Percent form: <size=120%> — caller is expected to multiply by base size.
            // We encode percent as a negative number sentinel: -value is "percent of base".
            if (s.EndsWith("%", StringComparison.Ordinal))
            {
                if (!float.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    return false;
                px = -pct; // negative encodes "percent"
                return true;
            }
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out px);
        }
    }
}
