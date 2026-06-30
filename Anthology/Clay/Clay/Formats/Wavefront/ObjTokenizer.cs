using System.Globalization;

namespace Prowl.Clay.Formats.Wavefront;

/// <summary>
/// Line-oriented tokenizer for Wavefront OBJ and MTL files. Operates on a
/// <see cref="ReadOnlySpan{T}"/> so the caller can stream lines from disk or a memory buffer
/// without per-line allocations.
/// </summary>
internal ref struct ObjTokenizer
{
    private ReadOnlySpan<char> _line;
    private int _pos;

    public ObjTokenizer(ReadOnlySpan<char> line)
    {
        _line = line;
        _pos = 0;
    }

    /// <summary>True when there are no more non-whitespace characters to read.</summary>
    public bool AtEnd
    {
        get
        {
            SkipWhitespace();
            return _pos >= _line.Length;
        }
    }

    /// <summary>Returns the next whitespace-delimited token, or an empty span when the line is exhausted.</summary>
    public ReadOnlySpan<char> NextToken()
    {
        SkipWhitespace();
        int start = _pos;
        while (_pos < _line.Length && !IsWhitespace(_line[_pos]))
            _pos++;
        return _line[start.._pos];
    }

    /// <summary>Returns the remainder of the line trimmed of leading/trailing whitespace.</summary>
    public ReadOnlySpan<char> Rest()
    {
        SkipWhitespace();
        return _line[_pos..].TrimEnd();
    }

    /// <summary>Parses the next token as an invariant-culture single-precision float.</summary>
    public float NextFloat()
    {
        var tok = NextToken();
        if (tok.IsEmpty)
            throw new FormatException("Expected float but reached end of line.");
        if (!float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            throw new FormatException($"Could not parse '{tok}' as float.");
        return v;
    }

    /// <summary>Parses the next token as an integer.</summary>
    public int NextInt()
    {
        var tok = NextToken();
        if (tok.IsEmpty)
            throw new FormatException("Expected int but reached end of line.");
        if (!int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            throw new FormatException($"Could not parse '{tok}' as int.");
        return v;
    }

    /// <summary>Parses the next token as a float if available, otherwise returns <paramref name="fallback"/>.</summary>
    public float NextFloatOr(float fallback)
    {
        SkipWhitespace();
        if (_pos >= _line.Length) return fallback;
        return NextFloat();
    }

    private void SkipWhitespace()
    {
        while (_pos < _line.Length && IsWhitespace(_line[_pos]))
            _pos++;
    }

    private static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\r';
}
