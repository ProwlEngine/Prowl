using System;

namespace Prowl.Ember;

/// <summary>
/// The kinds of compiler generated name the engine acts on. Everything else Roslyn emits parses to
/// <see cref="Other"/> with its discriminator retained, so a new kind never needs a change here.
/// </summary>
public enum SyntheticKind
{
    None = 0,
    Other,
    LambdaMethod,               // b
    LambdaDisplayClass,         // c
    StateMachine,               // d
    LocalFunction,              // g
    AnonymousType,              // f
    AutoPropertyBackingField,   // k
    InlineArray,                // y
    ReadOnlyList,               // z
}

public readonly record struct SyntheticName(
    string? Scope,
    SyntheticKind Kind,
    char Discriminator,
    string? Suffix,
    int Ordinal,
    int Generation,
    int SubOrdinal,
    int SubGeneration,
    int Arity)
{
    public bool IsLambdaLike => Kind is SyntheticKind.LambdaMethod or SyntheticKind.LocalFunction;

    /// <summary>
    /// Scans a member name. Written as a forward scanner rather than a regex: index construction parses every
    /// type name in an assembly and most of them are not synthetic, so the common case must exit on the first
    /// character without allocating.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> name, out SyntheticName result)
    {
        result = default;

        if (name.Length < 3 || name[0] != '<') return false;

        int close = name.IndexOf('>');
        if (close < 0) return false;

        var scope = close == 1 ? null : new string(name[1..close]);

        int at = close + 1;
        if (at >= name.Length) return false;

        char discriminator = name[at];
        if (!IsDiscriminator(discriminator)) return false;
        at++;

        string? suffix = null;
        int ordinal = -1, generation = -1, subOrdinal = -1, subGeneration = -1, arity = 0;

        if (at + 1 < name.Length && name[at] == '_' && name[at + 1] == '_')
        {
            at += 2;
            suffix = ReadSuffix(name, ref at);

            if (!ReadOrdinals(name, ref at, ref ordinal, ref generation, ref subOrdinal, ref subGeneration))
                return false;
        }

        if (at < name.Length && name[at] == '`')
        {
            at++;
            if (!ReadNumber(name, ref at, out arity)) return false;
        }

        if (at != name.Length) return false;

        result = new SyntheticName(scope, ToKind(discriminator), discriminator, suffix,
            ordinal, generation, subOrdinal, subGeneration, arity);
        return true;
    }

    public static bool TryParse(string? name, out SyntheticName result)
    {
        if (name == null) { result = default; return false; }
        return TryParse(name.AsSpan(), out result);
    }

    // Roslyn's method scoped names use a lowercase letter or a digit. Uppercase discriminators (file types,
    // extension containers) are deliberately not accepted: their names are stable across a rebuild, so they
    // are better matched by full name like an ordinary type.
    private static bool IsDiscriminator(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static SyntheticKind ToKind(char discriminator) => discriminator switch
    {
        'b' => SyntheticKind.LambdaMethod,
        'c' => SyntheticKind.LambdaDisplayClass,
        'd' => SyntheticKind.StateMachine,
        'f' => SyntheticKind.AnonymousType,
        'g' => SyntheticKind.LocalFunction,
        'k' => SyntheticKind.AutoPropertyBackingField,
        'y' => SyntheticKind.InlineArray,
        'z' => SyntheticKind.ReadOnlyList,
        _ => SyntheticKind.Other,
    };

    // Two shapes: a pipe terminated suffix that may contain digits (local functions), or a bare suffix of
    // letters and underscores that stops before the ordinals.
    private static string? ReadSuffix(ReadOnlySpan<char> name, ref int at)
    {
        int end = at;
        while (end < name.Length && (IsWordStart(name[end]) || IsDigit(name[end]))) end++;

        if (end < name.Length && name[end] == '|' && end - at >= 2 && IsWordStart(name[at]))
        {
            var piped = new string(name[at..end]);
            at = end + 1;
            return piped;
        }

        end = at;
        while (end < name.Length && IsWordStart(name[end])) end++;
        if (end == at) return null;

        var bare = new string(name[at..end]);
        at = end;
        return bare;
    }

    private static bool ReadOrdinals(ReadOnlySpan<char> name, ref int at,
        ref int ordinal, ref int generation, ref int subOrdinal, ref int subGeneration)
    {
        if (at >= name.Length || !IsDigit(name[at])) return true;

        if (!ReadNumber(name, ref at, out ordinal)) return false;
        if (!ReadGeneration(name, ref at, ref generation)) return false;

        if (at < name.Length && name[at] == '_' && at + 1 < name.Length && IsDigit(name[at + 1]))
        {
            at++;
            if (!ReadNumber(name, ref at, out subOrdinal)) return false;
            if (!ReadGeneration(name, ref at, ref subGeneration)) return false;
        }

        return true;
    }

    private static bool ReadGeneration(ReadOnlySpan<char> name, ref int at, ref int generation)
    {
        if (at >= name.Length || name[at] != '#') return true;
        at++;
        return ReadNumber(name, ref at, out generation);
    }

    private static bool ReadNumber(ReadOnlySpan<char> name, ref int at, out int value)
    {
        value = 0;
        int start = at;

        while (at < name.Length && IsDigit(name[at]))
        {
            value = (value * 10) + (name[at] - '0');
            at++;
        }

        return at > start;
    }

    private static bool IsWordStart(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
