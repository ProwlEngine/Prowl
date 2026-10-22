// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>A change to a <see cref="NetSet{T}" />.</summary>
/// <param name="Kind">
///     What happened: <see cref="NetChangeKind.Added" />, <see cref="NetChangeKind.Removed" /> or
///     <see cref="NetChangeKind.Cleared" />.
/// </param>
/// <param name="Item">The item added or removed. Default for <see cref="NetChangeKind.Cleared" />.</param>
public readonly record struct NetSetChange<T>(NetChangeKind Kind, T Item);

/// <summary>
///     A set that, as a <see cref="SyncAttribute" /> property, sends each change instead of the whole set.
///     Items must be sendable, as for any [Sync] value. See <see cref="NetCollection" />.
/// </summary>
public sealed class NetSet<T> : NetCollection, ISet<T>, IReadOnlySet<T>
{
    private readonly HashSet<T> _items = new();

    public NetSet()
    {
    }

    public NetSet(IEnumerable<T> items) => _items.UnionWith(items);

    public override int Count => _items.Count;

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>False if it's there already, or this peer may not change the set.</summary>
    public bool Add(T item)
    {
        if (_items.Contains(item) || !BeginChange(out NetWriter? record))
        {
            return false;
        }

        if (record != null)
        {
            record.WriteByte((byte)NetChangeKind.Added);
            NetSerializer<T>.Write(record, item);
        }

        _items.Add(item);
        Notify(Changed, new NetSetChange<T>(NetChangeKind.Added, item), false);
        return true;
    }

    /// <summary>False if it isn't there, or this peer may not change the set.</summary>
    public bool Remove(T item)
    {
        if (!_items.Contains(item) || !BeginChange(out NetWriter? record))
        {
            return false;
        }

        if (record != null)
        {
            record.WriteByte((byte)NetChangeKind.Removed);
            NetSerializer<T>.Write(record, item);
        }

        _items.Remove(item);
        Notify(Changed, new NetSetChange<T>(NetChangeKind.Removed, item), false);
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
        Notify(Changed, new NetSetChange<T>(NetChangeKind.Cleared, default!), false);
    }

    public bool Contains(T item) => _items.Contains(item);

    // The changing operations go item by item, so each is sent and raised like any other change.

    public void UnionWith(IEnumerable<T> other)
    {
        foreach (T item in other)
        {
            Add(item);
        }
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        foreach (T item in other)
        {
            Remove(item);
        }
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        HashSet<T> keep = new(other, _items.Comparer);
        foreach (T item in _items.Where(i => !keep.Contains(i)).ToList())
        {
            Remove(item);
        }
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        foreach (T item in new HashSet<T>(other, _items.Comparer))
        {
            if (!Remove(item) && !_items.Contains(item))
            {
                Add(item);
            }
        }
    }

    public bool IsSubsetOf(IEnumerable<T> other) => _items.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => _items.IsSupersetOf(other);

    public bool IsProperSubsetOf(IEnumerable<T> other) => _items.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => _items.IsProperSupersetOf(other);

    public bool Overlaps(IEnumerable<T> other) => _items.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => _items.SetEquals(other);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    void ICollection<T>.Add(T item) => Add(item);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Raised for each change, made here or received. See <see cref="NetCollection" />.</summary>
    public event Action<NetSetChange<T>>? Changed;

    public HashSet<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    private protected override void WriteItems(NetWriter writer)
    {
        writer.WriteVarUInt32((uint)_items.Count);
        foreach (T item in _items)
        {
            NetSerializer<T>.Write(writer, item);
        }
    }

    private protected override void ReadItems(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        _items.Clear();
        for (uint i = 0; i < count; i++)
        {
            _items.Add(NetSerializer<T>.Read(ref reader));
        }
    }

    private protected override void ReadChange(ref NetReader reader, bool apply)
    {
        NetChangeKind kind = (NetChangeKind)reader.ReadByte();
        switch (kind)
        {
            case NetChangeKind.Added:
                {
                    T item = NetSerializer<T>.Read(ref reader);
                    if (apply && _items.Add(item))
                    {
                        Notify(Changed, new NetSetChange<T>(kind, item), true);
                    }

                    return;
                }
            case NetChangeKind.Removed:
                {
                    T item = NetSerializer<T>.Read(ref reader);
                    if (apply && _items.Remove(item))
                    {
                        Notify(Changed, new NetSetChange<T>(kind, item), true);
                    }

                    return;
                }
            case NetChangeKind.Cleared:
                if (apply)
                {
                    _items.Clear();
                    Notify(Changed, new NetSetChange<T>(kind, default!), true);
                }

                return;
            default:
                throw new InvalidDataException($"Unknown set change {kind}.");
        }
    }
}
