namespace Prowl.Unwrapper;

/// <summary>
/// Open-addressing hash map keyed on <see cref="long"/> with <see cref="int"/> values.
/// Same shape as <see cref="LongDoubleMap"/>; the sparse-matrix assembler and the non-manifold
/// fixer both upsert millions of times with similar keys, where the per-op overhead of
/// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> matters.
/// </summary>
internal sealed class LongIntMap
{
    private long[] _keys;
    private int[] _values;
    private bool[] _occupied;
    private int _capacity;
    private int _mask;
    private int _count;
    private int _resizeAt;

    public int Count => _count;

    public LongIntMap(int capacityHint = 16)
    {
        _capacity = NextPow2(System.Math.Max(16, capacityHint * 2));
        _mask = _capacity - 1;
        _resizeAt = (_capacity * 3) / 4;
        _keys = new long[_capacity];
        _values = new int[_capacity];
        _occupied = new bool[_capacity];
    }

    public void Clear()
    {
        if (_count == 0) return;
        System.Array.Clear(_occupied, 0, _capacity);
        _count = 0;
    }

    /// <summary>Returns true if the key existed; otherwise inserts <paramref name="defaultValue"/>.</summary>
    public bool TryGetOrAdd(long key, int defaultValue, out int value)
    {
        if (_count >= _resizeAt) Grow();
        int idx = (int)(Mix(key) & (uint)_mask);
        while (_occupied[idx])
        {
            if (_keys[idx] == key) { value = _values[idx]; return true; }
            idx = (idx + 1) & _mask;
        }
        _occupied[idx] = true;
        _keys[idx] = key;
        _values[idx] = defaultValue;
        value = defaultValue;
        ++_count;
        return false;
    }

    public int Get(long key)
    {
        int idx = (int)(Mix(key) & (uint)_mask);
        while (_occupied[idx])
        {
            if (_keys[idx] == key) return _values[idx];
            idx = (idx + 1) & _mask;
        }
        throw new System.Collections.Generic.KeyNotFoundException();
    }

    /// <summary>Non-mutating lookup. Returns true and sets <paramref name="value"/> if present.</summary>
    public bool TryGet(long key, out int value)
    {
        int idx = (int)(Mix(key) & (uint)_mask);
        while (_occupied[idx])
        {
            if (_keys[idx] == key) { value = _values[idx]; return true; }
            idx = (idx + 1) & _mask;
        }
        value = 0;
        return false;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly LongIntMap _map;
        private int _idx;
        public Enumerator(LongIntMap map) { _map = map; _idx = -1; }
        public bool MoveNext()
        {
            while (++_idx < _map._capacity)
                if (_map._occupied[_idx]) return true;
            return false;
        }
        public (long Key, int Value) Current => (_map._keys[_idx], _map._values[_idx]);
    }

    private void Grow()
    {
        long[] oldKeys = _keys;
        int[] oldValues = _values;
        bool[] oldOccupied = _occupied;
        int oldCapacity = _capacity;

        _capacity *= 2;
        _mask = _capacity - 1;
        _resizeAt = (_capacity * 3) / 4;
        _keys = new long[_capacity];
        _values = new int[_capacity];
        _occupied = new bool[_capacity];
        _count = 0;

        for (int i = 0; i < oldCapacity; ++i)
            if (oldOccupied[i])
            {
                long k = oldKeys[i];
                int v = oldValues[i];
                int idx = (int)(Mix(k) & (uint)_mask);
                while (_occupied[idx]) idx = (idx + 1) & _mask;
                _occupied[idx] = true;
                _keys[idx] = k;
                _values[idx] = v;
                ++_count;
            }
    }

    private static ulong Mix(long key)
    {
        ulong x = (ulong)key;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x = x ^ (x >> 31);
        return x;
    }

    private static int NextPow2(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }
}
