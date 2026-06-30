namespace Prowl.Unwrapper;

/// <summary>
/// Open-addressing hash map keyed on <see cref="long"/> with <see cref="double"/> values.
/// Insert-or-update + iterate are the only ops the sparse-matrix assembler needs, so the rest
/// (delete, ordered access) are omitted.
/// </summary>
/// <remarks>
/// Designed for the tight inner loop in LSCM/LinABF assembly: millions of upserts per region
/// during merge trials. A dedicated map avoids the boxing, generic-dispatch and bucket-chain
/// indirection of <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> at this key/value pair.
/// </remarks>
internal sealed class LongDoubleMap
{
    private long[] _keys;
    private double[] _values;
    private bool[] _occupied;
    private int _capacity;
    private int _mask;
    private int _count;
    private int _resizeAt;

    public int Count => _count;

    public LongDoubleMap(int capacityHint = 16)
    {
        _capacity = NextPow2(System.Math.Max(16, capacityHint * 2));
        _mask = _capacity - 1;
        _resizeAt = (_capacity * 3) / 4;
        _keys = new long[_capacity];
        _values = new double[_capacity];
        _occupied = new bool[_capacity];
    }

    /// <summary>Reset to empty without shrinking the backing arrays.</summary>
    public void Clear()
    {
        if (_count == 0) return;
        System.Array.Clear(_occupied, 0, _capacity);
        _count = 0;
    }

    /// <summary>Replace (or insert) the value associated with <paramref name="key"/>.</summary>
    public void Set(long key, double value)
    {
        if (_count >= _resizeAt) Grow();
        int idx = (int)(Mix(key) & (uint)_mask);
        while (_occupied[idx])
        {
            if (_keys[idx] == key) { _values[idx] = value; return; }
            idx = (idx + 1) & _mask;
        }
        _occupied[idx] = true;
        _keys[idx] = key;
        _values[idx] = value;
        ++_count;
    }

    /// <summary>Add <paramref name="addend"/> to the value at <paramref name="key"/>, inserting if missing.</summary>
    public void Add(long key, double addend)
    {
        if (_count >= _resizeAt) Grow();
        int idx = (int)(Mix(key) & (uint)_mask);
        while (_occupied[idx])
        {
            if (_keys[idx] == key) { _values[idx] += addend; return; }
            idx = (idx + 1) & _mask;
        }
        _occupied[idx] = true;
        _keys[idx] = key;
        _values[idx] = addend;
        ++_count;
    }

    /// <summary>Return the value at <paramref name="key"/>, or throw if missing.</summary>
    public double GetOrThrow(long key)
    {
        int idx = (int)(Mix(key) & (uint)_mask);
        while (_occupied[idx])
        {
            if (_keys[idx] == key) return _values[idx];
            idx = (idx + 1) & _mask;
        }
        throw new System.Collections.Generic.KeyNotFoundException();
    }

    /// <summary>Iterate every (key, value) pair currently in the map. Order is implementation-defined.</summary>
    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly LongDoubleMap _map;
        private int _idx;
        public Enumerator(LongDoubleMap map) { _map = map; _idx = -1; }
        public bool MoveNext()
        {
            while (++_idx < _map._capacity)
                if (_map._occupied[_idx]) return true;
            return false;
        }
        public (long Key, double Value) Current => (_map._keys[_idx], _map._values[_idx]);
    }

    private void Grow()
    {
        long[] oldKeys = _keys;
        double[] oldValues = _values;
        bool[] oldOccupied = _occupied;
        int oldCapacity = _capacity;

        _capacity *= 2;
        _mask = _capacity - 1;
        _resizeAt = (_capacity * 3) / 4;
        _keys = new long[_capacity];
        _values = new double[_capacity];
        _occupied = new bool[_capacity];
        _count = 0;

        for (int i = 0; i < oldCapacity; ++i)
            if (oldOccupied[i]) Set(oldKeys[i], oldValues[i]);
    }

    private static ulong Mix(long key)
    {
        // SplitMix64 finaliser — strong avalanche on 64-bit input with cheap mul/shift/xor.
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
