using System;
using System.Diagnostics.CodeAnalysis;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Key-value pair interned to int pair for fast hashing.
/// </summary>
public readonly struct Keyword : IEquatable<Keyword>
{
    private static Interner<string, int> s_keywordInterner = new((x) => x + 1);

    /// <summary>
    /// String key.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// Interned name ID, for hashing/comparison.
    /// </summary>
    public readonly int NameId;

    /// <summary>
    /// String value.
    /// </summary>
    public readonly string Value;

    /// <summary>
    /// Interned value ID, for hashing/comparison.
    /// </summary>
    public readonly int ValueId;


    /// <summary>
    /// Builds keyword from name-value pair.
    /// </summary>
    public Keyword(string name, string value)
    {
        Name = name;
        NameId = s_keywordInterner.Intern(name);
        Value = value;
        ValueId = s_keywordInterner.Intern(value);
    }


    /// <summary>
    /// FNV hash of the interned name/value ints.
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
