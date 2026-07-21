using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;


namespace Prowl.Graphite.ShaderDef;


internal struct KeywordState
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


    /// <summary>
    /// Sets a keyword in its slot. Returns false and leaves state unchanged if name isn't in this set.
    /// </summary>
    public bool SetKeyword(Keyword keyword)
    {
        if (!_nameIDToSlot.TryGetValue(keyword.NameId, out int slot))
            return false;

        int oldValue = _valueIDs[slot];

        _hash ^= HashSlot(keyword.NameId, oldValue);
        _valueIDs[slot] = keyword.ValueId;
        _values[slot] = keyword;
        _hash ^= HashSlot(keyword.NameId, keyword.ValueId);
        return true;
    }


    /// <summary>
    /// Counts matching slot values against other. Used to find closest variant when no exact match.
    /// </summary>
    public readonly int MatchScore(KeywordState other)
    {
        int minLength = Math.Min(_values.Length, other._values.Length);
        int score = 0;
        for (int i = 0; i < minLength; i++)
        {
            if (_values[i].Equals(other._values[i]))
                score++;
        }

        return score;
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
