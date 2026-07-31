using System;
using System.Collections.Generic;

namespace Prowl.Ember;

/// <summary>
/// The typed bag carried from <see cref="IReloadAware.OnReloadDetach"/> on the outgoing instance to
/// <see cref="IReloadAware.OnReloadAttach"/> on the incoming one. Stored values are themselves migrated in
/// between, so the incoming instance reads current side objects.
/// </summary>
public sealed class ReloadState
{
    private readonly List<KeyValuePair<string, object?>> _entries = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    /// <summary>Keys in the order they were first set.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var entry in _entries)
                yield return entry.Key;
        }
    }

    public void Set<T>(string key, T value)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        if (_index.TryGetValue(key, out var at))
            _entries[at] = new KeyValuePair<string, object?>(key, value);
        else
        {
            _index[key] = _entries.Count;
            _entries.Add(new KeyValuePair<string, object?>(key, value));
        }
    }

    /// <summary>
    /// Reads a stored value. Returns false when the key is absent or the stored value is not a
    /// <typeparamref name="T"/>, which is the usual outcome when the stashed type itself changed shape.
    /// </summary>
    public bool TryGet<T>(string key, out T value)
    {
        if (key != null && _index.TryGetValue(key, out var at) && _entries[at].Value is T stored)
        {
            value = stored;
            return true;
        }
        value = default!;
        return false;
    }

    public T? GetOrDefault<T>(string key) => TryGet<T>(key, out var value) ? value : default;

    public bool Contains(string key) => key != null && _index.ContainsKey(key);

    public bool Remove(string key)
    {
        if (key == null || !_index.TryGetValue(key, out var at)) return false;

        _entries.RemoveAt(at);
        _index.Remove(key);
        for (int i = at; i < _entries.Count; i++)
            _index[_entries[i].Key] = i;
        return true;
    }

    internal void Clear()
    {
        _entries.Clear();
        _index.Clear();
    }

    /// <summary>
    /// Passes every stored value through <paramref name="migrate"/>, in place. Iterating by index keeps this
    /// safe without a scratch copy.
    /// </summary>
    internal void Remap(Func<object?, object?> migrate)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.Value == null) continue;
            if (entry.Value.GetType().IsPrimitive) continue;

            var migrated = migrate(entry.Value);
            if (!ReferenceEquals(migrated, entry.Value))
                _entries[i] = new KeyValuePair<string, object?>(entry.Key, migrated);
        }
    }
}
