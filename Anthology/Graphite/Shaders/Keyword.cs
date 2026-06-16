using System;
using System.Diagnostics.CodeAnalysis;

namespace Prowl.Graphite.Shaders;


public readonly struct Keyword : IEquatable<Keyword>
{
    private static Interner<string, int> s_keywordInterner = new((x) => x + 1);


    public readonly string Name;
    public readonly int NameId;

    public readonly string Value;
    public readonly int ValueId;


    public Keyword(string name, string value)
    {
        Name = name;
        NameId = s_keywordInterner.Intern(name);
        Value = value;
        ValueId = s_keywordInterner.Intern(value);
    }


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



    public override int GetHashCode() => (int)LongHash();


    public bool Equals(Keyword other)
    {
        return NameId == other.NameId && ValueId == other.ValueId;
    }


    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is Keyword keyword)
            return Equals(keyword);

        return false;
    }


    public static bool operator ==(Keyword left, Keyword right)
    {
        return left.Equals(right);
    }


    public static bool operator !=(Keyword left, Keyword right)
    {
        return !(left == right);
    }
}
