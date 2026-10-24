// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.Networking.Attributes;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Serialization;
using Prowl.Warp;

namespace Prowl.Runtime.Networking.Collections;

/// <summary>A change to a <see cref="NetList{T}" />.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Index">Where. -1 for <see cref="NetChangeKind.Cleared" />.</param>
/// <param name="OldValue">The item removed or replaced.</param>
/// <param name="NewValue">The item added or put in its place.</param>
public readonly record struct NetListChange<T>(NetChangeKind Kind, int Index, T OldValue, T NewValue);

/// <summary>
///     A list that, as a <see cref="SyncAttribute" /> property, sends each change instead of the whole list.
///     Items must be sendable, as for any [Sync] value. See <see cref="NetCollection" />.
/// </summary>
public sealed class NetList<T> : NetCollection, IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items = new();

    public NetList()
    {
    }

    public NetList(IEnumerable<T> items) => _items.AddRange(items);

    public override int Count => _items.Count;

    bool ICollection<T>.IsReadOnly => false;

    public T this[int index]
    {
        get => _items[index];
        set
        {
            T old = _items[index];
            if (EqualityComparer<T>.Default.Equals(old, value) || !BeginChange(out NetWriter? record))
            {
                return;
            }

            if (record != null)
            {
                record.WriteByte((byte)NetChangeKind.Replaced);
                record.WriteVarUInt32((uint)index);
                NetSerializer<T>.Write(record, value);
            }

            _items[index] = value;
            Notify(Changed, new NetListChange<T>(NetChangeKind.Replaced, index, old, value), false);
        }
    }

    public void Add(T item) => Insert(_items.Count, item);

    public void Insert(int index, T item)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_items.Count, nameof(index));
        if (!BeginChange(out NetWriter? record))
        {
            return;
        }

        if (record != null)
        {
            record.WriteByte((byte)NetChangeKind.Added);
            record.WriteVarUInt32((uint)index);
            NetSerializer<T>.Write(record, item);
        }

        _items.Insert(index, item);
        Notify(Changed, new NetListChange<T>(NetChangeKind.Added, index, default!, item), false);
    }

    public void RemoveAt(int index) => TryRemoveAt(index);

    /// <summary>
    ///     Removes the first occurrence of <paramref name="item" />. False if it isn't there, or this peer may not change
    ///     the list.
    /// </summary>
    public bool Remove(T item)
    {
        int index = _items.IndexOf(item);
        return index >= 0 && TryRemoveAt(index);
    }

    public void Clear()
    {
        if (_items.Count == 0 || !BeginChange(out NetWriter? record))
        {
            return;
        }

        record?.WriteByte((byte)NetChangeKind.Cleared);
        _items.Clear();
        Notify(Changed, new NetListChange<T>(NetChangeKind.Cleared, -1, default!, default!), false);
    }

    public int IndexOf(T item) => _items.IndexOf(item);

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal override NetCollection CreateEmptyCopy() => new NetList<T>();

    /// <summary>Raised for each change, made here or received. See <see cref="NetCollection" />.</summary>
    public event Action<NetListChange<T>>? Changed;

    public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    private bool TryRemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_items.Count, nameof(index));
        if (!BeginChange(out NetWriter? record))
        {
            return false;
        }

        if (record != null)
        {
            record.WriteByte((byte)NetChangeKind.Removed);
            record.WriteVarUInt32((uint)index);
        }

        T old = _items[index];
        _items.RemoveAt(index);
        Notify(Changed, new NetListChange<T>(NetChangeKind.Removed, index, old, default!), false);
        return true;
    }

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
                    int index = (int)reader.ReadVarUInt32();
                    T item = NetSerializer<T>.Read(ref reader);
                    if (!apply)
                    {
                        return;
                    }

                    // Compared unsigned, so an index past int's range is a mismatch rather than negative.
                    if ((uint)index > (uint)_items.Count)
                    {
                        Mismatch($"an add at {(uint)index}");
                        return;
                    }

                    _items.Insert(index, item);
                    Notify(Changed, new NetListChange<T>(kind, index, default!, item), true);
                    return;
                }
            case NetChangeKind.Removed:
                {
                    int index = (int)reader.ReadVarUInt32();
                    if (!apply)
                    {
                        return;
                    }

                    if ((uint)index >= (uint)_items.Count)
                    {
                        Mismatch($"a removal at {(uint)index}");
                        return;
                    }

                    T old = _items[index];
                    _items.RemoveAt(index);
                    Notify(Changed, new NetListChange<T>(kind, index, old, default!), true);
                    return;
                }
            case NetChangeKind.Replaced:
                {
                    int index = (int)reader.ReadVarUInt32();
                    T item = NetSerializer<T>.Read(ref reader);
                    if (!apply)
                    {
                        return;
                    }

                    if ((uint)index >= (uint)_items.Count)
                    {
                        Mismatch($"a replacement at {(uint)index}");
                        return;
                    }

                    T old = _items[index];
                    _items[index] = item;
                    Notify(Changed, new NetListChange<T>(kind, index, old, item), true);
                    return;
                }
            case NetChangeKind.Cleared:
                if (apply)
                {
                    _items.Clear();
                    Notify(Changed, new NetListChange<T>(kind, -1, default!, default!), true);
                }

                return;
            default:
                throw new InvalidDataException($"Unknown list change {kind}.");
        }
    }
}
