// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>A change to a <see cref="NetDictionary{TKey,TValue}" />.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Key">Whose value. Default for <see cref="NetChangeKind.Cleared" />.</param>
/// <param name="OldValue">The value removed or replaced.</param>
/// <param name="NewValue">The value added or put in its place.</param>
public readonly record struct NetDictionaryChange<TKey, TValue>(
    NetChangeKind Kind,
    TKey Key,
    TValue OldValue,
    TValue NewValue);

/// <summary>
///     A dictionary that, as a <see cref="SyncAttribute" /> property, sends each change instead of the whole
///     dictionary. Keys and values must be sendable, as for any [Sync] value. See <see cref="NetCollection" />.
/// </summary>
public sealed class NetDictionary<TKey, TValue> : NetCollection, IDictionary<TKey, TValue>,
    IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _items = new();

    public Dictionary<TKey, TValue>.KeyCollection Keys => _items.Keys;

    public Dictionary<TKey, TValue>.ValueCollection Values => _items.Values;

    public override int Count => _items.Count;

    ICollection<TKey> IDictionary<TKey, TValue>.Keys => _items.Keys;

    ICollection<TValue> IDictionary<TKey, TValue>.Values => _items.Values;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    public TValue this[TKey key]
    {
        get => _items[key];
        set => Set(key, value, false);
    }

    /// <exception cref="ArgumentException">There's already a value under <paramref name="key" />.</exception>
    public void Add(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_items.ContainsKey(key))
        {
            throw new ArgumentException($"There's already a value under {key}.", nameof(key));
        }

        Set(key, value, true);
    }

    /// <summary>False if there's nothing under the key, or this peer may not change the dictionary.</summary>
    public bool Remove(TKey key)
    {
        if (!_items.TryGetValue(key, out TValue? old) || !BeginChange(out NetWriter? record))
        {
            return false;
        }

        if (record != null)
        {
            record.WriteByte((byte)NetChangeKind.Removed);
            NetSerializer<TKey>.Write(record, key);
        }

        _items.Remove(key);
        Notify(Changed, new NetDictionaryChange<TKey, TValue>(NetChangeKind.Removed, key, old, default!), false);
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0 || !BeginChange(out NetWriter? record))
        {
            return;
        }

        record?.WriteByte((byte)NetChangeKind.Cleared);
        _items.Clear();
        Notify(Changed, new NetDictionaryChange<TKey, TValue>(NetChangeKind.Cleared, default!, default!, default!),
            false);
    }

    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _items.TryGetValue(key, out value);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_items).Contains(item);

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)this).Contains(item) && Remove(item.Key);

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _items.Keys;

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _items.Values;

    internal override NetCollection CreateEmptyCopy() => new NetDictionary<TKey, TValue>();

    /// <summary>Raised for each change, made here or received. See <see cref="NetCollection" />.</summary>
    public event Action<NetDictionaryChange<TKey, TValue>>? Changed;

    /// <summary>
    ///     Adds the value unless there's one under the key already. False if there is, or this peer may not change the
    ///     dictionary.
    /// </summary>
    public bool TryAdd(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !_items.ContainsKey(key) && Set(key, value, true);
    }

    public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _items.GetEnumerator();

    private bool Set(TKey key, TValue value, bool add)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool had = _items.TryGetValue(key, out TValue? old);
        if (had && EqualityComparer<TValue>.Default.Equals(old, value))
        {
            return true;
        }

        if (!BeginChange(out NetWriter? record))
        {
            return false;
        }

        NetChangeKind kind = had ? NetChangeKind.Replaced : NetChangeKind.Added;
        if (record != null)
        {
            record.WriteByte((byte)kind);
            NetSerializer<TKey>.Write(record, key);
            NetSerializer<TValue>.Write(record, value);
        }

        _items[key] = value;
        Notify(Changed, new NetDictionaryChange<TKey, TValue>(kind, key, old!, value), false);
        return true;
    }

    private protected override void WriteItems(NetWriter writer)
    {
        writer.WriteVarUInt32((uint)_items.Count);
        foreach ((TKey key, TValue value) in _items)
        {
            NetSerializer<TKey>.Write(writer, key);
            NetSerializer<TValue>.Write(writer, value);
        }
    }

    private protected override void ReadItems(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        _items.Clear();
        for (uint i = 0; i < count; i++)
        {
            TKey key = NetSerializer<TKey>.Read(ref reader);
            TValue value = NetSerializer<TValue>.Read(ref reader);
            // A key that doesn't survive the trip, like a reference to an object this peer doesn't have, is dropped.
            if (key != null)
            {
                _items[key] = value;
            }
        }
    }

    private protected override void ReadChange(ref NetReader reader, bool apply)
    {
        NetChangeKind kind = (NetChangeKind)reader.ReadByte();
        switch (kind)
        {
            case NetChangeKind.Added or NetChangeKind.Replaced:
                {
                    TKey key = NetSerializer<TKey>.Read(ref reader);
                    TValue value = NetSerializer<TValue>.Read(ref reader);
                    if (!apply || key == null)
                    {
                        return;
                    }

                    // Added or replaced by what this peer has, which is what its handlers care about.
                    bool had = _items.TryGetValue(key, out TValue? old);
                    _items[key] = value;
                    Notify(Changed,
                        new NetDictionaryChange<TKey, TValue>(had ? NetChangeKind.Replaced : NetChangeKind.Added, key,
                            old!, value), true);
                    return;
                }
            case NetChangeKind.Removed:
                {
                    TKey key = NetSerializer<TKey>.Read(ref reader);
                    if (!apply || key == null)
                    {
                        return;
                    }

                    if (!_items.Remove(key, out TValue? old))
                    {
                        Mismatch($"the removal of {key}");
                        return;
                    }

                    Notify(Changed, new NetDictionaryChange<TKey, TValue>(kind, key, old, default!), true);
                    return;
                }
            case NetChangeKind.Cleared:
                if (apply)
                {
                    _items.Clear();
                    Notify(Changed, new NetDictionaryChange<TKey, TValue>(kind, default!, default!, default!), true);
                }

                return;
            default:
                throw new InvalidDataException($"Unknown dictionary change {kind}.");
        }
    }
}
