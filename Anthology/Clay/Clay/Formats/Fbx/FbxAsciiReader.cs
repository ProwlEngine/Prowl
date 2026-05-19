using System.Globalization;
using System.Text;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Reads ASCII FBX into the same <see cref="FbxNode"/> tree that <see cref="FbxBinaryReader"/>
/// produces, so the rest of the importer is agnostic to which on-disk form the file used. Two
/// passes: scan tokens into a flat list, then walk the list into Scope/Element pairs.
/// </summary>
/// <remarks>
/// Two normalizations are applied during construction so downstream code can treat ASCII the
/// same way as binary:
/// <list type="bullet">
/// <item>Array nodes (<c>Key: *N { a: v0,v1,... }</c>) collapse the inner <c>a</c> child into a
/// single typed-array <see cref="FbxProperty"/> on the outer node, matching how binary stores
/// big arrays as a single property.</item>
/// <item>Numeric data tokens are parsed eagerly as ints or doubles (whichever fits), so
/// <see cref="FbxProperty.AsDouble"/> / <c>AsInt</c> / <c>AsDoubleArray</c> work identically.</item>
/// </list>
/// </remarks>
internal sealed class FbxAsciiReader
{
    public uint Version { get; private set; }

    private readonly string _text;
    private readonly List<Tok> _tokens = new();
    private int _cursor;

    private FbxAsciiReader(string text) { _text = text; }

    public static bool LooksLikeAscii(byte[] bytes)
    {
        // Binary FBX starts with the magic "Kaydara FBX Binary  \0". Anything else with leading
        // ';' (comment) or a letter (first key of the document) is treated as ASCII.
        if (bytes.Length < 23) return bytes.Length > 0 && (bytes[0] == (byte)';' || IsAsciiLetter((char)bytes[0]));
        return !(bytes[0] == 'K' && bytes[1] == 'a' && bytes[2] == 'y' && bytes[3] == 'd' && bytes[4] == 'a' && bytes[5] == 'r' && bytes[6] == 'a');
    }

    public static FbxAsciiReader Create(byte[] bytes)
    {
        // FBX ASCII is ASCII / UTF-8 with no declared encoding. Tolerate BOM by skipping it.
        int start = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
        var text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        var r = new FbxAsciiReader(text);
        r.Tokenize();
        return r;
    }

    public FbxNode ReadRoot()
    {
        var root = new FbxNode { Name = "<root>" };
        ParseScopeInto(root, topLevel: true);
        FillVersionFromHeader(root);
        return root;
    }

    private void FillVersionFromHeader(FbxNode root)
    {
        var hdr = root.FindChild("FBXHeaderExtension");
        var verNode = hdr?.FindChild("FBXVersion");
        if (verNode is not null && verNode.Properties.Count > 0)
            Version = (uint)verNode.Properties[0].AsInt();
    }

    // -------------------------------------------------------------------------------------------
    // Tokenization: state-machine over the input chars producing a flat token list.
    // -------------------------------------------------------------------------------------------

    private enum TT { OpenBracket, CloseBracket, Comma, Key, Data }

    private readonly struct Tok
    {
        public Tok(TT t, string s, int l) { Type = t; Text = s; Line = l; }
        public TT Type { get; }
        public string Text { get; }
        public int Line { get; }
    }

    private void Tokenize()
    {
        int line = 1;
        bool inComment = false;
        bool inQuotes = false;
        int tokenBegin = -1;
        int tokenEnd = -1;

        void EmitData(int endExclusive)
        {
            if (tokenBegin < 0) return;
            int len = endExclusive - tokenBegin;
            if (len <= 0) { tokenBegin = tokenEnd = -1; return; }
            string s = _text.Substring(tokenBegin, len);
            // Strip surrounding quotes if the token was a quoted string (the tokenizer kept them).
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
            _tokens.Add(new Tok(TT.Data, s, line));
            tokenBegin = tokenEnd = -1;
        }

        for (int i = 0; i < _text.Length; i++)
        {
            char c = _text[i];
            if (c == '\r') continue;
            // Newline ends comments and any pending data token. Fall through to the whitespace
            // handler below so we don't lose tokens that aren't followed by other whitespace
            // before the next structural character (e.g. "-1.#INF\n}").
            if (c == '\n')
            {
                if (inComment) { inComment = false; line++; continue; }
                line++;
            }
            if (inComment) continue;

            if (inQuotes)
            {
                if (c == '"')
                {
                    inQuotes = false;
                    EmitData(i + 1);
                }
                else
                {
                    tokenEnd = i;
                }
                continue;
            }

            switch (c)
            {
                case ';':
                    EmitData(i);
                    inComment = true;
                    continue;
                case '"':
                    if (tokenBegin >= 0)
                        throw new InvalidDataException($"Unexpected double-quote on line {line}.");
                    tokenBegin = i; // include the opening quote so EmitData can detect it
                    inQuotes = true;
                    continue;
                case '{':
                    EmitData(i);
                    _tokens.Add(new Tok(TT.OpenBracket, "{", line));
                    continue;
                case '}':
                    EmitData(i);
                    _tokens.Add(new Tok(TT.CloseBracket, "}", line));
                    continue;
                case ',':
                    EmitData(i);
                    _tokens.Add(new Tok(TT.Comma, ",", line));
                    continue;
                case ':':
                    if (tokenBegin >= 0)
                    {
                        // Pending token is a key.
                        string keyStr = _text.Substring(tokenBegin, i - tokenBegin);
                        _tokens.Add(new Tok(TT.Key, keyStr, line));
                        tokenBegin = tokenEnd = -1;
                    }
                    else
                    {
                        throw new InvalidDataException($"Unexpected colon on line {line}.");
                    }
                    continue;
            }

            if (c == ' ' || c == '\t' || c == '\v' || c == '\f' || c == '\n')
            {
                if (tokenBegin >= 0)
                {
                    // Peek ahead past whitespace to check for a colon (then this was a key).
                    int peek = i;
                    while (peek < _text.Length && (_text[peek] == ' ' || _text[peek] == '\t' || _text[peek] == '\r' || _text[peek] == '\n'))
                    {
                        if (_text[peek] == '\n') line++;
                        peek++;
                    }
                    if (peek < _text.Length && _text[peek] == ':')
                    {
                        string keyStr = _text.Substring(tokenBegin, i - tokenBegin);
                        _tokens.Add(new Tok(TT.Key, keyStr, line));
                        i = peek; // consume colon next iteration -> actually skip it here
                        tokenBegin = tokenEnd = -1;
                        continue;
                    }
                    EmitData(i);
                }
                continue;
            }

            // Regular content character.
            tokenEnd = i;
            if (tokenBegin < 0) tokenBegin = i;
        }

        // Flush any trailing token.
        EmitData(_text.Length);
    }

    // -------------------------------------------------------------------------------------------
    // Parsing: walk the flat token list into the recursive Scope/Element tree.
    // -------------------------------------------------------------------------------------------

    private void ParseScopeInto(FbxNode parent, bool topLevel)
    {
        while (_cursor < _tokens.Count)
        {
            var t = _tokens[_cursor];
            if (t.Type == TT.CloseBracket)
            {
                if (topLevel) throw new InvalidDataException($"Unexpected '}}' on line {t.Line}.");
                _cursor++;
                return;
            }
            if (t.Type != TT.Key)
                throw new InvalidDataException($"Expected key on line {t.Line}, got {t.Type} '{t.Text}'.");
            _cursor++;
            ParseElement(parent, t);
        }
        if (!topLevel) throw new InvalidDataException("Unexpected EOF inside scope.");
    }

    private void ParseElement(FbxNode parent, Tok keyToken)
    {
        var node = new FbxNode { Name = keyToken.Text };
        var dataTokens = new List<Tok>();
        FbxNode? compound = null;

        // Read tokens until either a key starts the next element or a '}' closes the scope.
        while (_cursor < _tokens.Count)
        {
            var t = _tokens[_cursor];
            if (t.Type == TT.Comma) { _cursor++; continue; }
            if (t.Type == TT.Data)
            {
                dataTokens.Add(t);
                _cursor++;
                continue;
            }
            if (t.Type == TT.OpenBracket)
            {
                _cursor++;
                compound = new FbxNode { Name = "<scope>" };
                ParseScopeInto(compound, topLevel: false);
                break;
            }
            if (t.Type == TT.Key || t.Type == TT.CloseBracket)
                break;
            _cursor++;
        }

        // Array normalization: pattern "Key: *N { a: v0,v1,... }". The outer element has a
        // single *N token and exactly one child named "a" carrying the data. Lift the a-child's
        // tokens up as a single typed-array Property on the outer node.
        if (TryNormalizeArrayNode(node, dataTokens, compound))
        {
            parent.Children.Add(node);
            return;
        }

        // Regular element: every data token becomes a property; child scope's children become
        // this node's children.
        foreach (var dt in dataTokens)
            node.Properties.Add(ParseDataTokenAsProperty(dt));
        if (compound is not null)
            foreach (var c in compound.Children)
                node.Children.Add(c);

        parent.Children.Add(node);
    }

    private bool TryNormalizeArrayNode(FbxNode outer, List<Tok> outerTokens, FbxNode? compound)
    {
        if (compound is null) return false;
        if (outerTokens.Count != 1) return false;
        var t = outerTokens[0];
        if (t.Type != TT.Data || t.Text.Length < 2 || t.Text[0] != '*') return false;
        if (compound.Children.Count != 1) return false;
        var inner = compound.Children[0];
        if (inner.Name != "a") return false;

        // The inner element has a flat list of data tokens converted to FbxProperties (we'll
        // re-derive them here from its Properties list since ParseElement already populated it).
        // We pack them into a single double[] (or int[]) property on `outer` to match binary form.
        bool allInt = true;
        var data = new double[inner.Properties.Count];
        for (int i = 0; i < inner.Properties.Count; i++)
        {
            var p = inner.Properties[i];
            switch (p.Type)
            {
                case FbxPropertyType.Int32:
                case FbxPropertyType.Int64:
                case FbxPropertyType.Int16:
                    data[i] = p.AsDouble();
                    break;
                case FbxPropertyType.Double:
                case FbxPropertyType.Float:
                    data[i] = p.AsDouble();
                    allInt = false;
                    break;
                default:
                    // Mixed/unsupported - bail out of array normalization and keep the original
                    // tree shape so downstream code can deal with it manually.
                    return false;
            }
        }

        if (allInt)
        {
            var ints = new int[data.Length];
            for (int i = 0; i < data.Length; i++) ints[i] = (int)data[i];
            outer.Properties.Add(FbxProperty.FromIntArray(ints));
        }
        else
        {
            outer.Properties.Add(FbxProperty.FromDoubleArray(data));
        }
        return true;
    }

    private static FbxProperty ParseDataTokenAsProperty(Tok t)
    {
        string s = t.Text;
        // Bare * tokens (array length markers like "*36") get dropped from the property list
        // because the array data follows separately and is normalized in TryNormalizeArrayNode.
        // For non-array contexts, treat *N as just the integer N so we don't lose the value.
        if (s.Length > 1 && s[0] == '*')
        {
            if (int.TryParse(s.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return FbxProperty.FromInt(n);
        }

        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ll))
        {
            if (ll >= int.MinValue && ll <= int.MaxValue) return FbxProperty.FromInt((int)ll);
            return FbxProperty.FromLong(ll);
        }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dd))
        {
            return FbxProperty.FromDouble(dd);
        }
        // Windows-MSVCRT special-value formats that double.TryParse does not recognize.
        // FBX exports from older Max / Maya on Windows can emit these for NaN / Inf vertex data.
        if (TryParseWindowsSpecialFloat(s, out double special))
            return FbxProperty.FromDouble(special);
        // Fallback: treat as string. Common for type identifiers like "T", "Color", etc.
        return FbxProperty.FromString(s);
    }

    /// <summary>
    /// Parses every NaN / Inf textual form FBX exporters emit that double.TryParse rejects
    /// under NumberStyles.Float / CultureInfo.InvariantCulture:
    /// the MSVCRT forms (<c>1.#INF</c>, <c>-1.#NAN</c>, <c>1.#IND</c>, <c>1.#QNAN</c>,
    /// <c>1.#SNAN</c>) plus all-cased / abbreviated literals (<c>inf</c>, <c>INF</c>,
    /// <c>+INFINITY</c>, <c>-Infinity</c>, <c>nan</c>, <c>NaN</c>, ...). Returns <c>true</c> and
    /// the corresponding double when the input matches, <c>false</c> otherwise.
    /// </summary>
    private static bool TryParseWindowsSpecialFloat(string s, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;

        // Strip a leading sign so we can match the suffix case-insensitively below.
        bool neg = false;
        int start = 0;
        if (s[0] == '+') start = 1;
        else if (s[0] == '-') { start = 1; neg = true; }
        string body = start == 0 ? s : s[start..];
        if (body.Length == 0) return false;

        // MSVCRT shape: "1.#TAG[junk]" or "#TAG[junk]". The numeric prefix is informational.
        // The part after '#' may have trailing digits/letters (e.g. 2.#IND12345678) which we
        // ignore - any string with a # is intended as a numeric special-value.
        int hash = body.IndexOf('#');
        if (hash >= 0)
        {
            if (hash + 1 >= body.Length) return false;
            string tag = body[(hash + 1)..].ToUpperInvariant();
            if (tag.StartsWith("INFINITY", StringComparison.Ordinal) || tag.StartsWith("INF", StringComparison.Ordinal))
            { value = neg ? double.NegativeInfinity : double.PositiveInfinity; return true; }
            if (tag.StartsWith("QNAN", StringComparison.Ordinal) || tag.StartsWith("SNAN", StringComparison.Ordinal)
                || tag.StartsWith("NAN", StringComparison.Ordinal) || tag.StartsWith("IND", StringComparison.Ordinal))
            { value = double.NaN; return true; }
            return false;
        }

        // No '#' present - require an EXACT match (case-insensitive) against the well-known
        // names, or "nan(payload)" form. Anything else (e.g. ordinary words like "Index" or
        // "Indirect" that happen to start with a special-value prefix) MUST NOT be treated as
        // a special float; we'd corrupt unrelated string properties.
        string upper = body.ToUpperInvariant();
        if (upper == "INF" || upper == "INFINITY")
        { value = neg ? double.NegativeInfinity : double.PositiveInfinity; return true; }
        if (upper == "NAN" || upper == "QNAN" || upper == "SNAN" || upper == "IND")
        { value = double.NaN; return true; }
        // "nan(payload)" - well-known IEEE 754 NaN-with-payload form, e.g. nan(12345), nan(ind).
        int paren = upper.IndexOf('(');
        if (paren > 0 && upper.EndsWith(")", StringComparison.Ordinal))
        {
            string head = upper[..paren];
            if (head == "NAN" || head == "QNAN" || head == "SNAN")
            { value = double.NaN; return true; }
        }
        return false;
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
}
