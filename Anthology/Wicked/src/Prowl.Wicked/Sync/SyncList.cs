namespace Prowl.Wicked.Sync;

using System.Collections;
using Prowl.Wicked.Network.Serialization;

/// <summary>
/// A synchronized list that automatically replicates changes from server to clients.
/// Changes made on the server are tracked and sent to all clients efficiently using delta synchronization.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class SyncList<T> : SyncObject, IList<T>, IReadOnlyList<T>
{
    /// <summary>
    /// Types of operations that can be performed on the list.
    /// </summary>
    public enum Operation : byte
    {
        Add,
        Set,
        Insert,
        RemoveAt,
        Clear
    }

    /// <summary>
    /// Called after an item is added to the list.
    /// Parameter: index of the added item.
    /// </summary>
    public Action<int>? OnAdd;

    /// <summary>
    /// Called after an item is inserted into the list.
    /// Parameter: index where the item was inserted.
    /// </summary>
    public Action<int>? OnInsert;

    /// <summary>
    /// Called after an item is changed in the list.
    /// Parameters: index, old value.
    /// </summary>
    public Action<int, T>? OnSet;

    /// <summary>
    /// Called after an item is removed from the list.
    /// Parameters: index, removed value.
    /// </summary>
    public Action<int, T>? OnRemove;

    /// <summary>
    /// Called before the list is cleared (so items can be accessed).
    /// </summary>
    public Action? OnClear;

    /// <summary>
    /// Called for any change to the list.
    /// Parameters: operation, index, old value, new value.
    /// </summary>
    public Action<Operation, int, T, T>? OnChange;

    private readonly IList<T> _items;
    private readonly IEqualityComparer<T> _comparer;

    private struct Change
    {
        public Operation Operation;
        public int Index;
        public T Item;
    }

    private readonly List<Change> _changes = new();
    private int _changesAhead;

    /// <summary>
    /// Creates a new empty SyncList.
    /// </summary>
    public SyncList() : this(EqualityComparer<T>.Default) { }

    /// <summary>
    /// Creates a new empty SyncList with a custom equality comparer.
    /// </summary>
    public SyncList(IEqualityComparer<T> comparer)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _items = new List<T>();
    }

    /// <summary>
    /// Creates a SyncList wrapping an existing list.
    /// </summary>
    public SyncList(IList<T> items, IEqualityComparer<T>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _items = items;
    }

    /// <summary>
    /// The number of items in the list.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Returns true if the list is read-only (not writable).
    /// </summary>
    public bool IsReadOnly => !IsWritable();

    private void AddOperation(Operation op, int index, T oldItem, T newItem, bool checkAccess)
    {
        if (checkAccess && IsReadOnly)
            throw new InvalidOperationException("SyncList can only be modified by the owner.");

        var change = new Change
        {
            Operation = op,
            Index = index,
            Item = newItem
        };

        if (IsRecording())
        {
            _changes.Add(change);
            OnDirty?.Invoke();
        }

        // Invoke callbacks
        switch (op)
        {
            case Operation.Add:
                OnAdd?.Invoke(index);
                OnChange?.Invoke(op, index, oldItem, newItem);
                break;
            case Operation.Insert:
                OnInsert?.Invoke(index);
                OnChange?.Invoke(op, index, oldItem, newItem);
                break;
            case Operation.Set:
                OnSet?.Invoke(index, oldItem);
                OnChange?.Invoke(op, index, oldItem, newItem);
                break;
            case Operation.RemoveAt:
                OnRemove?.Invoke(index, oldItem);
                OnChange?.Invoke(op, index, oldItem, newItem);
                break;
            case Operation.Clear:
                OnClear?.Invoke();
                OnChange?.Invoke(op, index, default!, default!);
                break;
        }
    }

    #region SyncObject Implementation

    public override void ClearChanges() => _changes.Clear();

    public override void Reset()
    {
        _changes.Clear();
        _changesAhead = 0;
        _items.Clear();
    }

    public override void OnSerializeAll(NetworkWriter writer)
    {
        // Write the full list
        writer.WriteUInt((uint)_items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            writer.Write(_items[i]);
        }

        // Write how many changes are pending (client needs to skip these)
        writer.WriteUInt((uint)_changes.Count);
    }

    public override void OnSerializeDelta(NetworkWriter writer)
    {
        // Write all queued changes
        writer.WriteUInt((uint)_changes.Count);

        for (int i = 0; i < _changes.Count; i++)
        {
            var change = _changes[i];
            writer.WriteByte((byte)change.Operation);

            switch (change.Operation)
            {
                case Operation.Add:
                    writer.Write(change.Item);
                    break;
                case Operation.Clear:
                    // No data needed
                    break;
                case Operation.RemoveAt:
                    writer.WriteUInt((uint)change.Index);
                    break;
                case Operation.Insert:
                case Operation.Set:
                    writer.WriteUInt((uint)change.Index);
                    writer.Write(change.Item);
                    break;
            }
        }
    }

    public override void OnDeserializeAll(NetworkReader reader)
    {
        // Read the full list
        int count = (int)reader.ReadUInt();

        _items.Clear();
        _changes.Clear();

        for (int i = 0; i < count; i++)
        {
            T item = reader.Read<T>();
            _items.Add(item);
        }

        // How many changes to skip
        _changesAhead = (int)reader.ReadUInt();
    }

    public override void OnDeserializeDelta(NetworkReader reader)
    {
        int changesCount = (int)reader.ReadUInt();

        for (int i = 0; i < changesCount; i++)
        {
            var operation = (Operation)reader.ReadByte();
            bool apply = _changesAhead == 0;

            int index = 0;
            T oldItem = default!;
            T newItem = default!;

            switch (operation)
            {
                case Operation.Add:
                    newItem = reader.Read<T>();
                    if (apply)
                    {
                        index = _items.Count;
                        _items.Add(newItem);
                        AddOperation(Operation.Add, index, default!, newItem, false);
                    }
                    break;

                case Operation.Clear:
                    if (apply)
                    {
                        AddOperation(Operation.Clear, 0, default!, default!, false);
                        _items.Clear();
                    }
                    break;

                case Operation.Insert:
                    index = (int)reader.ReadUInt();
                    newItem = reader.Read<T>();
                    if (apply)
                    {
                        _items.Insert(index, newItem);
                        AddOperation(Operation.Insert, index, default!, newItem, false);
                    }
                    break;

                case Operation.RemoveAt:
                    index = (int)reader.ReadUInt();
                    if (apply)
                    {
                        oldItem = _items[index];
                        _items.RemoveAt(index);
                        AddOperation(Operation.RemoveAt, index, oldItem, default!, false);
                    }
                    break;

                case Operation.Set:
                    index = (int)reader.ReadUInt();
                    newItem = reader.Read<T>();
                    if (apply)
                    {
                        oldItem = _items[index];
                        _items[index] = newItem;
                        AddOperation(Operation.Set, index, oldItem, newItem, false);
                    }
                    break;
            }

            if (!apply)
            {
                _changesAhead--;
            }
        }
    }

    #endregion

    #region IList<T> Implementation

    public T this[int index]
    {
        get => _items[index];
        set
        {
            if (!_comparer.Equals(_items[index], value))
            {
                T oldItem = _items[index];
                _items[index] = value;
                AddOperation(Operation.Set, index, oldItem, value, true);
            }
        }
    }

    public void Add(T item)
    {
        _items.Add(item);
        AddOperation(Operation.Add, _items.Count - 1, default!, item, true);
    }

    public void AddRange(IEnumerable<T> range)
    {
        foreach (T item in range)
            Add(item);
    }

    public void Clear()
    {
        AddOperation(Operation.Clear, 0, default!, default!, true);
        _items.Clear();
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public int IndexOf(T item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_comparer.Equals(item, _items[i]))
                return i;
        }
        return -1;
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        AddOperation(Operation.Insert, index, default!, item, true);
    }

    public void InsertRange(int index, IEnumerable<T> range)
    {
        foreach (T item in range)
        {
            Insert(index, item);
            index++;
        }
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }
        return false;
    }

    public void RemoveAt(int index)
    {
        T oldItem = _items[index];
        _items.RemoveAt(index);
        AddOperation(Operation.RemoveAt, index, oldItem, default!, true);
    }

    public int RemoveAll(Predicate<T> match)
    {
        var toRemove = new List<T>();
        for (int i = 0; i < _items.Count; i++)
        {
            if (match(_items[i]))
                toRemove.Add(_items[i]);
        }

        foreach (T item in toRemove)
            Remove(item);

        return toRemove.Count;
    }

    #endregion

    #region Additional Methods

    public int FindIndex(Predicate<T> match)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (match(_items[i]))
                return i;
        }
        return -1;
    }

    public T? Find(Predicate<T> match)
    {
        int i = FindIndex(match);
        return i != -1 ? _items[i] : default;
    }

    public List<T> FindAll(Predicate<T> match)
    {
        var results = new List<T>();
        for (int i = 0; i < _items.Count; i++)
        {
            if (match(_items[i]))
                results.Add(_items[i]);
        }
        return results;
    }

    #endregion

    #region Enumerator

    public Enumerator GetEnumerator() => new Enumerator(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    /// <summary>
    /// Struct enumerator to avoid allocations during foreach loops.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly SyncList<T> _list;
        private int _index;

        public T Current { get; private set; }

        public Enumerator(SyncList<T> list)
        {
            _list = list;
            _index = -1;
            Current = default!;
        }

        public bool MoveNext()
        {
            if (++_index >= _list.Count)
                return false;

            Current = _list[_index];
            return true;
        }

        public void Reset() => _index = -1;

        object IEnumerator.Current => Current!;

        public void Dispose() { }
    }

    #endregion
}
