using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Prowl.Crumb;

internal enum FixedKind : byte
{
    Symbol,
    Keyword,
    Block,
}

internal readonly struct FixedEntry<TKind> where TKind : unmanaged, Enum
{
    public readonly string Text;
    public readonly TKind Kind;
    public readonly FixedKind Type;
    public readonly string? BlockEnd;

    public FixedEntry(string text, TKind kind, FixedKind type, string? blockEnd)
    {
        Text = text;
        Kind = kind;
        Type = type;
        BlockEnd = blockEnd;
    }
}

/// <summary>
/// Declarative, fluent configuration for a <see cref="Tokenizer{TKind}"/>. Register
/// whitespace handling, fixed tokens (keywords/symbols), dynamic rules, comments and
/// embedded blocks, then hand the compiled object to a tokenizer. The same rules
/// object is immutable once compiled and may be reused across many tokenizers.
/// </summary>
public sealed partial class TokenizerRules<TKind> where TKind : unmanaged, Enum
{
    private readonly List<FixedEntry<TKind>> _fixed = new();
    private readonly List<TokenRule<TKind>> _rules = new();
    private readonly List<(string Start, string End)> _skipComments = new();

    private CharPredicate? _whitespace;
    private CharPredicate _identifierPart = DefaultIdentifierPart;

    internal CharPredicate? WhitespacePredicate => _whitespace;
    internal TokenRule<TKind>[] Rules { get; private set; } = Array.Empty<TokenRule<TKind>>();
    internal (string Start, string End)[] SkipComments { get; private set; } = Array.Empty<(string, string)>();
    internal FrozenDictionary<char, FixedEntry<TKind>[]> Dispatch { get; private set; } =
        FrozenDictionary<char, FixedEntry<TKind>[]>.Empty;

    internal TKind EndOfFileKindValue { get; private set; }
    internal TKind ErrorKindValue { get; private set; }
    internal CharPredicate IdentifierPart => _identifierPart;

    private bool _compiled;

    private static bool DefaultIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Configures the predicate used to recognise (and skip) whitespace.</summary>
    public TokenizerRules<TKind> Whitespace(CharPredicate predicate)
    {
        ThrowIfCompiled();
        _whitespace = predicate;
        return this;
    }

    /// <summary>The token kind emitted when the end of the source is reached.</summary>
    public TokenizerRules<TKind> EndOfFile(TKind kind)
    {
        ThrowIfCompiled();
        EndOfFileKindValue = kind;
        return this;
    }

    /// <summary>The token kind emitted for a character that no rule could consume.</summary>
    public TokenizerRules<TKind> Error(TKind kind)
    {
        ThrowIfCompiled();
        ErrorKindValue = kind;
        return this;
    }

    /// <summary>
    /// Registers a symbol: a fixed string matched literally with no word-boundary
    /// requirement. Longest match wins, so registering both "=" and "==" resolves
    /// greedily to "==".
    /// </summary>
    public TokenizerRules<TKind> Symbol(string text, TKind kind)
    {
        ThrowIfCompiled();
        Require(text);
        _fixed.Add(new FixedEntry<TKind>(text, kind, FixedKind.Symbol, null));
        return this;
    }

    /// <summary>
    /// Registers a keyword: a fixed string that only matches when not immediately
    /// followed by an identifier-continuation character, so "Shader" will not match
    /// inside "Shaders".
    /// </summary>
    public TokenizerRules<TKind> Keyword(string text, TKind kind)
    {
        ThrowIfCompiled();
        Require(text);
        _fixed.Add(new FixedEntry<TKind>(text, kind, FixedKind.Keyword, null));
        return this;
    }

    /// <summary>
    /// Registers an opaque block. After the opening string is matched, the tokenizer
    /// scans forward to the closing string and emits a single token spanning the
    /// content between them, without tokenizing the embedded language.
    /// </summary>
    public TokenizerRules<TKind> Block(string open, string close, TKind kind)
    {
        ThrowIfCompiled();
        Require(open);
        Require(close);
        _fixed.Add(new FixedEntry<TKind>(open, kind, FixedKind.Block, close));
        return this;
    }

    /// <summary>Registers a custom dynamic rule. Rules run in registration order.</summary>
    public TokenizerRules<TKind> Rule(TokenRule<TKind> rule)
    {
        ThrowIfCompiled();
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    /// <summary>Compiles the registrations into fast dispatch tables. Idempotent.</summary>
    public TokenizerRules<TKind> Compile()
    {
        if (_compiled)
            return this;

        var byFirst = new Dictionary<char, List<FixedEntry<TKind>>>();
        foreach (var entry in _fixed)
        {
            char first = entry.Text[0];
            if (!byFirst.TryGetValue(first, out var list))
                byFirst[first] = list = new List<FixedEntry<TKind>>();
            list.Add(entry);
        }

        var dispatch = new Dictionary<char, FixedEntry<TKind>[]>(byFirst.Count);
        foreach (var (first, list) in byFirst)
        {
            // Longest text first so the first successful StartsWith is the longest match.
            list.Sort(static (a, b) => b.Text.Length.CompareTo(a.Text.Length));
            dispatch[first] = [.. list];
        }

        Dispatch = dispatch.ToFrozenDictionary();
        Rules = [.. _rules];
        SkipComments = [.. _skipComments];
        _compiled = true;
        return this;
    }

    private void ThrowIfCompiled()
    {
        if (_compiled)
            throw new InvalidOperationException("TokenizerRules cannot be modified after compilation.");
    }

    private static void Require(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Fixed token text must be non-empty.", nameof(text));
    }
}
