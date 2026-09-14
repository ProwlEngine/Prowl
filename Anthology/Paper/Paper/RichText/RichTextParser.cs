using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Vector;

namespace Prowl.PaperUI.RichText;

/// <summary>
/// Parses rich text markup into spans over the visible text. A tag is a name and optional
/// arguments between angle brackets, such as wave 2 8 #f80, and an empty closing tag ends the most
/// recent one.
///
/// One forward pass, no regex and no backtracking. Tags are dispatched on length and first
/// character, numbers are read straight out of the source span, and the only allocation per parse
/// is the builder for the visible text plus any link hrefs. Buffers are reused between parses, so a
/// document that is parsed again costs nothing in the steady state.
/// </summary>
public sealed class RichTextParser
{
    private readonly List<RichEffectSpan> _effects = new();
    private readonly List<RichStyleSpan> _styles = new();
    private readonly List<RichColorSpan> _colors = new();
    private readonly List<RichSizeSpan> _sizes = new();
    private readonly List<RichLinkSpan> _links = new();
    private readonly List<Open> _open = new();
    private readonly System.Text.StringBuilder _visible = new();

    public string VisibleText { get; private set; } = string.Empty;
    public IReadOnlyList<RichEffectSpan> Effects => _effects;
    public IReadOnlyList<RichStyleSpan> Styles => _styles;
    public IReadOnlyList<RichColorSpan> Colors => _colors;
    public IReadOnlyList<RichSizeSpan> Sizes => _sizes;
    public IReadOnlyList<RichLinkSpan> Links => _links;

    /// <summary>True when any parsed effect animates, so a frame must evaluate glyph
    /// transforms again. Static markup skips that work entirely.</summary>
    public bool HasAnimatedEffects { get; private set; }

    private enum OpenKind : byte { Effect, Style, Color, Size, Link }

    private struct Open
    {
        public OpenKind Kind;
        public RichEffect Effect;
        public RichStyle Style;
        public Color Color;
        public float Scale;
        /// <summary>An effect given a colour as well, so both spans open and close together.</summary>
        public bool HasColor;
        public string? Href;
        public float Strength;
        public float Speed;
        public int Start;
        /// <summary>Hash of the tag name, so a named close can find its opener.</summary>
        public int NameHash;
    }

    public void Parse(string source)
    {
        _effects.Clear();
        _styles.Clear();
        _colors.Clear();
        _sizes.Clear();
        _links.Clear();
        _open.Clear();
        _visible.Clear();
        HasAnimatedEffects = false;

        if (string.IsNullOrEmpty(source))
        {
            VisibleText = string.Empty;
            return;
        }

        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];

            if (c == '\\' && i + 1 < source.Length && (source[i + 1] == '<' || source[i + 1] == '\\'))
            {
                _visible.Append(source[i + 1]);
                i += 2;
                continue;
            }

            if (c != '<')
            {
                _visible.Append(c);
                i++;
                continue;
            }

            int close = source.IndexOf('>', i + 1);
            if (close < 0)
            {
                // No terminator, so it is literal text rather than a tag.
                _visible.Append(c);
                i++;
                continue;
            }

            var body = source.AsSpan(i + 1, close - i - 1);
            if (body.IndexOf('<') < 0 && ApplyTag(body))
            {
                i = close + 1;
                continue;
            }

            // Not a tag, so the bracket is text. Scanning resumes right after it rather than after
            // the next '>', which may belong to a real tag further on.
            _visible.Append(c);
            i++;
        }

        // Anything still open runs to the end of the text.
        for (int o = _open.Count - 1; o >= 0; o--)
        {
            CloseAt(o, _visible.Length);
        }

        _open.Clear();
        VisibleText = _visible.ToString();
    }

    private bool ApplyTag(ReadOnlySpan<char> body)
    {
        if (body.Length == 0)
        {
            return false;
        }

        if (body[0] == '/')
        {
            var name = body.Slice(1).Trim();
            if (name.Length == 0)
            {
                // </> closes the most recent open tag, and is consumed even if there is none.
                if (_open.Count > 0)
                {
                    CloseAt(_open.Count - 1, _visible.Length);
                    _open.RemoveAt(_open.Count - 1);
                }

                return true;
            }

            int hash = NameHash(name);
            for (int o = _open.Count - 1; o >= 0; o--)
            {
                if (_open[o].NameHash == hash)
                {
                    CloseAt(o, _visible.Length);
                    _open.RemoveAt(o);
                    break;
                }
            }

            // A close tag is always consumed. A typo should cost the author their formatting,
            // not leave "</b>" sitting in the middle of the rendered text.
            return true;
        }

        return OpenTag(body);
    }

    private bool OpenTag(ReadOnlySpan<char> body)
    {
        int space = body.IndexOf(' ');
        var name = space < 0 ? body : body.Slice(0, space);
        var rest = space < 0 ? ReadOnlySpan<char>.Empty : body.Slice(space + 1);

        // "2 < 3 and 4 > 1" looks like a tag but has no name, so it is ordinary prose.
        if (name.Length == 0)
        {
            return false;
        }

        var open = new Open { Start = _visible.Length, NameHash = NameHash(name), Strength = float.NaN, Speed = float.NaN };

        // A bare colour: <#f80> or <red>.
        if (name[0] == '#')
        {
            if (!TryParseColor(name, out Color bare))
            {
                return false;
            }

            open.Kind = OpenKind.Color;
            open.Color = bare;
            _open.Add(open);
            return true;
        }

        if (TryResolveEffect(name, out RichEffect effect))
        {
            open.Kind = OpenKind.Effect;
            open.Effect = effect;
            ReadParameters(rest, ref open);
            open.HasColor = TryFindColor(rest, out open.Color);
            _open.Add(open);
            if (effect != RichEffect.None)
            {
                HasAnimatedEffects = true;
            }

            return true;
        }

        if (TryResolveStyle(name, out RichStyle style))
        {
            open.Kind = OpenKind.Style;
            open.Style = style;
            _open.Add(open);
            return true;
        }

        if (name.SequenceEqual("size".AsSpan()))
        {
            open.Kind = OpenKind.Size;
            open.Scale = ReadFloat(rest, 1f);
            _open.Add(open);
            return true;
        }

        if (name.SequenceEqual("link".AsSpan()))
        {
            open.Kind = OpenKind.Link;
            open.Href = rest.Trim().ToString();
            _open.Add(open);
            return true;
        }

        if (TryParseNamedColor(name, out Color named))
        {
            open.Kind = OpenKind.Color;
            open.Color = named;
            _open.Add(open);
            return true;
        }

        return false;
    }

    private void CloseAt(int index, int end)
    {
        var open = _open[index];
        if (end <= open.Start)
        {
            return;
        }

        switch (open.Kind)
        {
            case OpenKind.Effect:
                _effects.Add(new RichEffectSpan(open.Effect, open.Start, end, open.Strength, open.Speed));
                if (open.HasColor) _colors.Add(new RichColorSpan(open.Color, open.Start, end));
                break;
            case OpenKind.Style:
                _styles.Add(new RichStyleSpan(open.Style, open.Start, end));
                break;
            case OpenKind.Color:
                _colors.Add(new RichColorSpan(open.Color, open.Start, end));
                break;
            case OpenKind.Size:
                _sizes.Add(new RichSizeSpan(open.Scale, open.Start, end));
                break;
            case OpenKind.Link:
                _links.Add(new RichLinkSpan(open.Href ?? string.Empty, open.Start, end));
                break;
        }
    }

    /// <summary>Positional parameters: strength then speed, with an optional #colour anywhere.</summary>
    private static void ReadParameters(ReadOnlySpan<char> rest, ref Open open)
    {
        int found = 0;
        int i = 0;
        while (i < rest.Length && found < 2)
        {
            while (i < rest.Length && (rest[i] == ' ' || rest[i] == ','))
            {
                i++;
            }

            if (i >= rest.Length)
            {
                break;
            }

            int start = i;
            while (i < rest.Length && rest[i] != ' ' && rest[i] != ',')
            {
                i++;
            }

            var token = rest.Slice(start, i - start);
            if (token.Length == 0 || token[0] == '#')
            {
                continue;
            }

            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                if (found == 0)
                {
                    open.Strength = value;
                }
                else
                {
                    open.Speed = value;
                }

                found++;
            }
        }
    }

    private static float ReadFloat(ReadOnlySpan<char> rest, float fallback)
    {
        var token = rest.Trim();
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
    }

    private static bool TryFindColor(ReadOnlySpan<char> rest, out Color color)
    {
        color = default;
        int hash = rest.IndexOf('#');
        if (hash < 0)
        {
            return false;
        }

        int end = hash;
        while (end < rest.Length && rest[end] != ' ' && rest[end] != ',')
        {
            end++;
        }

        return TryParseColor(rest.Slice(hash, end - hash), out color);
    }

    /// <summary>#rgb, #rrggbb or #rrggbbaa.</summary>
    private static bool TryParseColor(ReadOnlySpan<char> token, out Color color)
    {
        color = default;
        if (token.Length < 2 || token[0] != '#')
        {
            return false;
        }

        var digits = token.Slice(1);
        Span<int> v = stackalloc int[8];
        if (digits.Length != 3 && digits.Length != 6 && digits.Length != 8)
        {
            return false;
        }

        for (int i = 0; i < digits.Length; i++)
        {
            int d = HexValue(digits[i]);
            if (d < 0)
            {
                return false;
            }

            v[i] = d;
        }

        if (digits.Length == 3)
        {
            color = new Color((byte)(v[0] * 17), (byte)(v[1] * 17), (byte)(v[2] * 17), (byte)255);
            return true;
        }

        int r = v[0] * 16 + v[1];
        int g = v[2] * 16 + v[3];
        int b = v[4] * 16 + v[5];
        int a = digits.Length == 8 ? v[6] * 16 + v[7] : 255;
        color = new Color((byte)r, (byte)g, (byte)b, (byte)a);
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    private static bool TryResolveEffect(ReadOnlySpan<char> name, out RichEffect effect)
    {
        effect = RichEffect.None;
        // Dispatch on length first so most names are rejected without any comparison.
        switch (name.Length)
        {
            case 4:
                if (Same(name, "wave")) { effect = RichEffect.Wave; return true; }
                if (Same(name, "spin")) { effect = RichEffect.Rotate; return true; }
                return false;
            case 5:
                if (Same(name, "shake")) { effect = RichEffect.Shake; return true; }
                if (Same(name, "slide")) { effect = RichEffect.Slide; return true; }
                if (Same(name, "swell")) { effect = RichEffect.Swell; return true; }
                if (Same(name, "pulse")) { effect = RichEffect.Pulse; return true; }
                if (Same(name, "swing")) { effect = RichEffect.Swing; return true; }
                return false;
            case 6:
                if (Same(name, "wiggle")) { effect = RichEffect.Wiggle; return true; }
                if (Same(name, "bounce")) { effect = RichEffect.Bounce; return true; }
                if (Same(name, "rotate")) { effect = RichEffect.Rotate; return true; }
                if (Same(name, "dangle")) { effect = RichEffect.Dangle; return true; }
                return false;
            case 7:
                if (Same(name, "rainbow")) { effect = RichEffect.Rainbow; return true; }
                return false;
            case 8:
                if (Same(name, "pendulum")) { effect = RichEffect.Pendulum; return true; }
                if (Same(name, "sizewave")) { effect = RichEffect.Swell; return true; }
                return false;
            default:
                return false;
        }
    }

    private static bool TryResolveStyle(ReadOnlySpan<char> name, out RichStyle style)
    {
        style = RichStyle.None;
        switch (name.Length)
        {
            case 1:
                style = name[0] switch
                {
                    'b' => RichStyle.Bold,
                    'i' => RichStyle.Italic,
                    'u' => RichStyle.Underline,
                    's' => RichStyle.Strike,
                    _ => RichStyle.None
                };
                return style != RichStyle.None;
            case 4:
                if (Same(name, "mono")) { style = RichStyle.Mono; return true; }
                if (Same(name, "bold")) { style = RichStyle.Bold; return true; }
                if (Same(name, "code")) { style = RichStyle.Mono; return true; }
                return false;
            case 6:
                if (Same(name, "italic")) { style = RichStyle.Italic; return true; }
                if (Same(name, "strike")) { style = RichStyle.Strike; return true; }
                return false;
            case 9:
                if (Same(name, "underline")) { style = RichStyle.Underline; return true; }
                return false;
            default:
                return false;
        }
    }

    private static bool TryParseNamedColor(ReadOnlySpan<char> name, out Color color)
    {
        color = default;
        switch (name.Length)
        {
            case 3:
                if (Same(name, "red")) { color = new Color((byte)220, (byte)60, (byte)60, (byte)255); return true; }
                return false;
            case 4:
                if (Same(name, "blue")) { color = new Color((byte)70, (byte)130, (byte)235, (byte)255); return true; }
                if (Same(name, "cyan")) { color = new Color((byte)70, (byte)200, (byte)220, (byte)255); return true; }
                if (Same(name, "gray")) { color = new Color((byte)140, (byte)140, (byte)150, (byte)255); return true; }
                if (Same(name, "grey")) { color = new Color((byte)140, (byte)140, (byte)150, (byte)255); return true; }
                return false;
            case 5:
                if (Same(name, "green")) { color = new Color((byte)80, (byte)200, (byte)120, (byte)255); return true; }
                if (Same(name, "white")) { color = new Color((byte)245, (byte)245, (byte)250, (byte)255); return true; }
                if (Same(name, "black")) { color = new Color((byte)20, (byte)20, (byte)24, (byte)255); return true; }
                return false;
            case 6:
                if (Same(name, "yellow")) { color = new Color((byte)240, (byte)200, (byte)80, (byte)255); return true; }
                if (Same(name, "orange")) { color = new Color((byte)240, (byte)150, (byte)70, (byte)255); return true; }
                if (Same(name, "purple")) { color = new Color((byte)170, (byte)110, (byte)230, (byte)255); return true; }
                return false;
            case 7:
                if (Same(name, "magenta")) { color = new Color((byte)230, (byte)100, (byte)200, (byte)255); return true; }
                return false;
            default:
                return false;
        }
    }

    private static bool Same(ReadOnlySpan<char> a, string b) => a.SequenceEqual(b.AsSpan());

    private static int NameHash(ReadOnlySpan<char> name)
    {
        int hash = 17;
        for (int i = 0; i < name.Length; i++)
        {
            hash = hash * 31 + name[i];
        }

        return hash;
    }
}
