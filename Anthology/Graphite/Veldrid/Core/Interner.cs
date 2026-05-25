using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Prowl.Veldrid;

/// <summary>
/// Produces the next interned value given the previous one.
/// Implementations should be atomic if the owning <see cref="Interner{TKey, TInternedValue}"/>
/// is used concurrently.
/// </summary>
public delegate T IncrementDelegate<T>(T previous);

/// <summary>
/// Process-wide, lock-free table that maps arbitrary keys to compact, monotonically
/// issued interned values. Intended for collapsing repeated string identifiers
/// (resource names, vertex semantics, etc.) into cheap value-type IDs.
/// </summary>
/// <typeparam name="TKey">Key type. Must be non-null and provide a sensible equality/hash.</typeparam>
/// <typeparam name="TInternedValue">
/// Issued value type. Must be an equatable value type so it can be stored and compared cheaply.
/// </typeparam>
public sealed class Interner<TKey, TInternedValue>
    where TKey : notnull
    where TInternedValue : struct, IEquatable<TInternedValue>
{
    private readonly ConcurrentDictionary<TKey, TInternedValue> _forward = new();
    private readonly IncrementDelegate<TInternedValue> _increment;
    private TInternedValue _last;

    /// <summary>
    /// Creates a new interner. The supplied <paramref name="increment"/> delegate is invoked
    /// every time a previously unseen key is interned; it receives the most recently issued
    /// value and returns the next one.
    /// </summary>
    public Interner(IncrementDelegate<TInternedValue> increment)
    {
        _increment = increment ?? throw new ArgumentNullException(nameof(increment));
    }

    /// <summary>
    /// Returns the interned value for <paramref name="key"/>, minting a new one via the
    /// increment delegate if the key has not been seen before.
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
    /// Reverse lookup. Linear scan of the forward map; intended for debug and explicit
    /// reverse-lookup paths only. Returns true and sets <paramref name="key"/> on hit.
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
