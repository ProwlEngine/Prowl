using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;


namespace Prowl.Graphite.ShaderDef;


internal struct KeywordState
{
    private Dictionary<int, int> _nameIDToSlot;
    private ulong _hash;

    private Keyword[] _values;


    public KeywordState(Dictionary<int, int> nameIDToSlot, Keyword[] keywordSet)
    {
        _nameIDToSlot = nameIDToSlot;
        _values = new Keyword[keywordSet.Length];

        for (int i = 0; i < keywordSet.Length; i++)
        {
            Keyword keyword = keywordSet[i];

            _values[i] = keyword;

            _hash ^= keyword.LongHash();
        }
    }


    /// <summary>
    /// Overwrites every slot from keywordSet (same shape as the constructor) without allocating.
    /// </summary>
    public void Reset(Keyword[] keywordSet)
    {
        _hash = 0;
        for (int i = 0; i < keywordSet.Length; i++)
        {
            _values[i] = keywordSet[i];
            _hash ^= keywordSet[i].LongHash();
        }
    }


    /// <summary>
    /// Sets a keyword in its slot. Returns false and leaves state unchanged if name isn't in this set.
    /// </summary>
    public bool SetKeyword(Keyword keyword)
    {
        if (!_nameIDToSlot.TryGetValue(keyword.NameId, out int slot))
            return false;

        // _values[slot] holds the old value for this slot and carries the same NameId as keyword
        // (slots are looked up by name id), so its LongHash replaces the old value's hash term.
        _hash ^= _values[slot].LongHash();
        _values[slot] = keyword;
        _hash ^= keyword.LongHash();
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
