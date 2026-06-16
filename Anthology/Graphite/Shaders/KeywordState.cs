using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;


namespace Prowl.Graphite.Shaders;


public struct KeywordState
{
    private Dictionary<int, int> _nameIDToSlot;
    private ulong _hash;

    private int[] _valueIDs;
    private Keyword[] _values;


    public KeywordState(Dictionary<int, int> nameIDToSlot, Keyword[] keywordSet)
    {
        _nameIDToSlot = nameIDToSlot;
        _valueIDs = new int[keywordSet.Length];
        _values = new Keyword[keywordSet.Length];

        for (int i = 0; i < keywordSet.Length; i++)
        {
            Keyword keyword = keywordSet[i];

            _valueIDs[i] = keyword.ValueId;
            _values[i] = keyword;

            _hash ^= HashSlot(keyword.NameId, keyword.ValueId);
        }
    }


    public void SetKeyword(Keyword keyword)
    {
        int slot = _nameIDToSlot[keyword.NameId];
        int oldValue = _valueIDs[slot];

        _hash ^= HashSlot(keyword.NameId, oldValue);
        _valueIDs[slot] = keyword.ValueId;
        _values[slot] = keyword;
        _hash ^= HashSlot(keyword.NameId, keyword.ValueId);
    }


    private static ulong HashSlot(int nameId, int valueId)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;

            h ^= (ulong)nameId * 1099511628211UL;
            h ^= (ulong)valueId * 16777619UL;

            return h;
        }
    }


    public readonly ulong LongHash() => _hash;


    public readonly bool MatchesKeywords(Keyword[] other)
    {
        int minLength = Math.Min(_values.Length, other.Length);
        for (int i = 0; i < minLength; i++)
        {
            if (!_values[i].Equals(other[i]))
                return false;
        }

        return true;
    }


    public readonly bool Matches(KeywordState other)
    {
        if (_hash != other._hash)
            return false;

        return MatchesKeywords(other._values);
    }
}
