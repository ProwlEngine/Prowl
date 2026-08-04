using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>Produces the next interned value. Must be atomic if used concurrently.</summary>
public delegate T IncrementDelegate<T>(T previous);

/// <summary>Lock-free table mapping keys to compact monotonic interned values.</summary>
/// <typeparam name="TKey">Key type, non-null, needs sane equality/hash.</typeparam>
/// <typeparam name="TInternedValue">Issued value type, must be equatable value type.</typeparam>
public sealed class Interner<TKey, TInternedValue>
    where TKey : notnull
    where TInternedValue : struct, IEquatable<TInternedValue>
{
    private readonly ConcurrentDictionary<TKey, TInternedValue> _forward = new();
    private readonly IncrementDelegate<TInternedValue> _increment;
    private TInternedValue _last;

    /// <summary>New interner. Increment delegate fires on unseen keys to generate the next value.</summary>
    public Interner(IncrementDelegate<TInternedValue> increment)
    {
        _increment = increment ?? throw new ArgumentNullException(nameof(increment));
    }

    /// <summary>Gets or mints the interned value for a key.</summary>
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

    /// <summary>Reverse lookup, linear scan. Returns true and sets key on hit.</summary>
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
