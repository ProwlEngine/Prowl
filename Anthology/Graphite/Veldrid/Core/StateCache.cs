using System;
using System.Collections.Concurrent;

namespace Prowl.Veldrid;

/// <summary>
/// Factory-backed concurrent cache. Values are produced on demand by the factory
/// supplied at construction time and are never disposed by the cache itself; callers
/// own the lifetime of stored values.
/// </summary>
/// <typeparam name="TKey">Key type. Equality and hashing follow the key's own implementation.</typeparam>
/// <typeparam name="TValue">Cached value type.</typeparam>
public sealed class StateCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _entries = new();
    private readonly Func<TKey, TValue> _factory;

    /// <summary>
    /// Creates a new cache backed by the supplied <paramref name="factory"/>.
    /// The factory may run concurrently for the same key under contention; both
    /// values must be safe to construct, but only one will be retained.
    /// </summary>
    public StateCache(Func<TKey, TValue> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Returns the existing value for <paramref name="key"/> or invokes the factory to produce
    /// and store a new one.
    /// </summary>
    public TValue GetOrAdd(TKey key) => _entries.GetOrAdd(key, _factory);

    /// <summary>
    /// Removes a single entry. Returns true if an entry existed. Does not dispose the value.
    /// </summary>
    public bool Evict(TKey key) => _entries.TryRemove(key, out _);

    /// <summary>
    /// Drops every entry. Does not dispose values.
    /// </summary>
    public void Clear() => _entries.Clear();
}
