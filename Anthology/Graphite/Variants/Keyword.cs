using System;
using System.Diagnostics.CodeAnalysis;

namespace Prowl.Graphite.Variants;


/// <summary>
/// A string-initialized (Key, Value) pair internally interned to an (int, int) pair for fast indexing and hashing.
/// </summary>
public readonly struct Keyword : IEquatable<Keyword>
{
    private static Interner<string, int> s_keywordInterner = new((x) => x + 1);

    /// <summary>
    /// The string key of this keyword.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// The interned name ID for this keyword. Used for hashing and fast comparisons.
    /// </summary>
    public readonly int NameId;

    /// <summary>
    /// The string value of this keyword.
    /// </summary>
    public readonly string Value;

    /// <summary>
    /// The interned value ID for this keyword. Used for hashing and fast comparisons.
    /// </summary>
    public readonly int ValueId;


    /// <summary>
    /// Initializes a keyword from a name-value pair.
    /// </summary>
    public Keyword(string name, string value)
    {
        Name = name;
        NameId = s_keywordInterner.Intern(name);
        Value = value;
        ValueId = s_keywordInterner.Intern(value);
    }


    /// <summary>
    /// The FNV hash of the interned name and value interned integers.
    /// </summary>
    public ulong LongHash()
    {
        unchecked
        {
            ulong h = 1469598103934665603UL; // FNV offset

            h ^= (ulong)NameId * 1099511628211UL;
            h ^= (ulong)ValueId * 16777619UL;

            return h;
        }
    }


    /// <inheritdoc/>
    public override int GetHashCode() => (int)LongHash();


    /// <inheritdoc/>
    public bool Equals(Keyword other)
    {
        return NameId == other.NameId && ValueId == other.ValueId;
    }


    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is Keyword keyword)
            return Equals(keyword);

        return false;
    }


    /// <inheritdoc/>
    public static bool operator ==(Keyword left, Keyword right)
    {
        return left.Equals(right);
    }


    /// <inheritdoc/>
    public static bool operator !=(Keyword left, Keyword right)
    {
        return !(left == right);
    }
}
