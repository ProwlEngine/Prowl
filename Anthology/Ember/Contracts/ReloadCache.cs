using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Ember;

/// <summary>
/// A cache that empties itself across a hot reload, so entries derived from the previous assembly's types are
/// recomputed against the current ones instead of pinning what they came from. The usual case is a map keyed on
/// <see cref="Type"/> or a member handle, where every entry is stale the moment the assembly is replaced.
/// </summary>
/// <remarks>
/// Hold instances in a <c>static readonly</c> field so the reload reaches them and fires the reset. The backing
/// map is opted out of migration deliberately: rebuilding a map that is about to be emptied is pure waste, and
/// rehashing keys that are themselves being replaced is worse than pointless.
/// </remarks>
public sealed class ReloadCache<TKey, TValue> : IReloadAware, IReloadObserver
    where TKey : notnull
{
    [ReloadIgnore] private ConcurrentDictionary<TKey, TValue> _entries = new();

    private readonly Func<TKey, TValue>? _factory;
    private readonly KeyValuePair<TKey, TValue>[] _seed;

    /// <summary>A cache whose indexer computes and stores a value per key on first use.</summary>
    public ReloadCache(Func<TKey, TValue> factory, params KeyValuePair<TKey, TValue>[] seed)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _seed = seed;
        Reset();
    }

    /// <summary>
    /// A cache filled by hand through <see cref="TryGetValue"/> and <see cref="Set"/>, for the cases a factory
    /// cannot express: skipping failures, holding a lock, or computing recursively.
    /// </summary>
    public ReloadCache(params KeyValuePair<TKey, TValue>[] seed)
    {
        _seed = seed;
        Reset();
    }

    /// <summary>Computes and caches the value for <paramref name="key"/>. Factory caches only.</summary>
    public TValue this[TKey key] => _factory is not null
        ? _entries.GetOrAdd(key, _factory)
        : throw new InvalidOperationException(
            $"This {nameof(ReloadCache<TKey, TValue>)} was built without a factory. Use TryGetValue and Set.");

    public bool TryGetValue(TKey key, out TValue value) => _entries.TryGetValue(key, out value!);
    public void Set(TKey key, TValue value) => _entries[key] = value;
    public bool TryAdd(TKey key, TValue value) => _entries.TryAdd(key, value);
    public int Count => _entries.Count;

    /// <summary>Empties the cache and puts the seed entries back. Called for you on reload.</summary>
    public void Reset()
    {
        // Assigned rather than cleared: on the path where this instance is itself replaced, an opted-out field
        // arrives holding whatever a fresh instance would have, and reassigning is the one shape that is correct
        // on both paths.
        var entries = new ConcurrentDictionary<TKey, TValue>();

        foreach (var item in _seed)
            entries.TryAdd(item.Key, item.Value);

        _entries = entries;
    }

    // Fires when this instance carried over untouched, which is the usual case for a cache living in an
    // assembly that was not itself replaced.
    void IReloadObserver.OnReloadPreserved() => Reset();

    // Fires when this instance was replaced, which happens once a type argument is one of the replaced types.
    void IReloadAware.OnReloadAttach(ReloadState state) => Reset();
}
