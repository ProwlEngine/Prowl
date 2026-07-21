using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Produces the next interned value from the previous one. Must be atomic if the interner is used
/// concurrently.
/// </summary>
public delegate T IncrementDelegate<T>(T previous);

/// <summary>
/// Process-wide lock-free table mapping keys to compact, monotonically issued interned values.
/// For collapsing repeated string IDs (resource names, vertex semantics) into cheap value IDs.
/// </summary>
/// <typeparam name="TKey">Key type. Non-null, needs sane equality/hash.</typeparam>
/// <typeparam name="TInternedValue">
/// Issued value type. Must be equatable value type for cheap storage/comparison.
/// </typeparam>
public sealed class Interner<TKey, TInternedValue>
    where TKey : notnull
    where TInternedValue : struct, IEquatable<TInternedValue>
{
    private readonly ConcurrentDictionary<TKey, TInternedValue> _forward = new();
    private readonly IncrementDelegate<TInternedValue> _increment;
    private TInternedValue _last;

    /// <summary>
    /// New interner. Increment delegate fires on each unseen key, given the last issued value,
    /// returns the next.
    /// </summary>
    public Interner(IncrementDelegate<TInternedValue> increment)
    {
        _increment = increment ?? throw new ArgumentNullException(nameof(increment));
    }

    /// <summary>
    /// Gets the interned value for a key, minting one if unseen.
    /// </summary>
    public TInternedValue Intern(TKey key)
    {
        if (_forward.TryGetValue(key, out TInternedValue existing))
            return existing;

        return _forward.GetOrAdd(key, k =>
        {
            TInternedValue next = _increment(_last);
            _last = next;
            return next;
        });
    }

    /// <summary>
    /// Reverse lookup. Linear scan, debug/explicit use only. True and sets key on hit.
    /// </summary>
    public bool TryGetKey(TInternedValue value, out TKey key)
    {
        foreach (KeyValuePair<TKey, TInternedValue> kvp in _forward)
        {
            if (kvp.Value.Equals(value))
            {
                key = kvp.Key;
                return true;
            }
        }
        key = default!;
        return false;
    }
}
