using System;

namespace Prowl.Graphite.ShaderDef;


internal sealed class KeywordMap
{
    private struct Entry
    {
        public ulong Hash;
        public int VariantIndex;
        public int Next;
    }


    private readonly int[] _buckets;
    private readonly Entry[] _entries;
    private readonly KeywordState[] _states;
    private int _count;


    public KeywordMap(KeywordState[] states)
    {
        _states = states;

        _buckets = new int[NextPowerOfTwo(states.Length)];
        Array.Fill(_buckets, -1);

        _entries = new Entry[states.Length];

        for (int i = 0; i < states.Length; i++)
        {
            Add(states[i], i);
        }
    }


    private void Add(KeywordState state, int variantIndex)
    {
        int bucketIndex = Bucket(state.LongHash());

        _entries[_count] = new Entry
        {
            Hash = state.LongHash(),
            VariantIndex = variantIndex,
            Next = _buckets[bucketIndex]
        };

        _buckets[bucketIndex] = _count;
        _count++;
    }


    public int Find(KeywordState state)
    {
        int bucketIndex = Bucket(state.LongHash());

        int entryIndex = _buckets[bucketIndex];

        // The bucket hash narrows the candidates; Matches() does the precise comparison.
        // A hash-only match is not sufficient: the per-slot hash is XOR-folded, so symmetric
        // value swaps (e.g. X=0,Y=1 vs X=1,Y=0) collide.
        while (entryIndex != -1)
        {
            ref Entry entry = ref _entries[entryIndex];

            if (_states[entry.VariantIndex].Matches(state))
                return entry.VariantIndex;

            entryIndex = entry.Next;
        }

        return -1;
    }


    /// <summary>
    /// Returns the exact variant for <paramref name="state"/> if one was compiled, otherwise the
    /// compiled variant that shares the most keyword slots with it. Always returns a valid index
    /// (there is at least one variant), so a requested combination that was never compiled degrades
    /// to the closest available variant instead of failing.
    /// </summary>
    public int FindNearest(KeywordState state)
    {
        int exact = Find(state);
        if (exact >= 0)
            return exact;

        int best = 0;
        int bestScore = -1;
        for (int i = 0; i < _states.Length; i++)
        {
            int score = _states[i].MatchScore(state);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }


    private int Bucket(ulong hash)
    {
        return (int)(hash & ((uint)_buckets.Length - 1));
    }


    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;

        return Math.Max(v, 2);
    }
}
