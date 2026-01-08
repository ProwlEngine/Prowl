namespace Prowl.Wicked.Sync;

using System.Collections;
using Prowl.Echo;

/// <summary>
/// A synchronized dictionary that automatically replicates changes from server to clients.
/// Changes made on the server are tracked and sent to all clients efficiently using delta synchronization.
/// Uses Prowl.Echo for serialization of contained values.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
public class SyncDictionary<TKey, TValue> : SyncObject, IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    /// <summary>
    /// Types of operations that can be performed on the dictionary.
    /// </summary>
    public enum Operation : byte
    {
        Add,
        Set,
        Remove,
        Clear
    }

    /// <summary>
    /// Called after an item is added to the dictionary.
    /// Parameter: key of the added item.
    /// </summary>
    public Action<TKey>? OnAdd;

    /// <summary>
    /// Called after an item is changed in the dictionary.
    /// Parameters: key, old value.
    /// </summary>
    public Action<TKey, TValue>? OnSet;

    /// <summary>
    /// Called after an item is removed from the dictionary.
    /// Parameters: key, removed value.
    /// </summary>
    public Action<TKey, TValue>? OnRemove;

    /// <summary>
    /// Called before the dictionary is cleared (so items can be accessed).
    /// </summary>
    public Action? OnClear;

    /// <summary>
    /// Called for any change to the dictionary.
    /// Parameters: operation, key, value.
    /// </summary>
    public Action<Operation, TKey, TValue>? OnChange;

    private readonly IDictionary<TKey, TValue> _items;

    private struct Change
    {
        public Operation Operation;
        public TKey Key;
        public TValue Item;
    }

    private readonly List<Change> _changes = new();
    private int _changesAhead;

    /// <summary>
    /// Creates a new empty SyncDictionary.
    /// </summary>
    public SyncDictionary()
    {
        _items = new Dictionary<TKey, TValue>();
    }

    /// <summary>
    /// Creates a new empty SyncDictionary with a custom equality comparer.
    /// </summary>
    public SyncDictionary(IEqualityComparer<TKey> comparer)
    {
        _items = new Dictionary<TKey, TValue>(comparer);
    }

    /// <summary>
    /// Creates a SyncDictionary wrapping an existing dictionary.
    /// </summary>
    public SyncDictionary(IDictionary<TKey, TValue> items)
    {
        _items = items;
    }

    /// <summary>
    /// The number of items in the dictionary.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Returns true if the dictionary is read-only (not writable).
    /// </summary>
    public bool IsReadOnly => !IsWritable();

    /// <summary>
    /// Gets the collection of keys in the dictionary.
    /// </summary>
    public ICollection<TKey> Keys => _items.Keys;

    /// <summary>
    /// Gets the collection of values in the dictionary.
    /// </summary>
    public ICollection<TValue> Values => _items.Values;

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _items.Keys;

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _items.Values;

    private void AddOperation(Operation op, TKey key, TValue item, TValue oldItem, bool checkAccess)
    {
        if (checkAccess && IsReadOnly)
            throw new InvalidOperationException("SyncDictionary can only be modified by the owner.");

        var change = new Change
        {
            Operation = op,
            Key = key,
            Item = item
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
                OnAdd?.Invoke(key);
                OnChange?.Invoke(op, key, item);
                break;
            case Operation.Set:
                OnSet?.Invoke(key, oldItem);
                OnChange?.Invoke(op, key, oldItem);
                break;
            case Operation.Remove:
                OnRemove?.Invoke(key, oldItem);
                OnChange?.Invoke(op, key, oldItem);
                break;
            case Operation.Clear:
                OnClear?.Invoke();
                OnChange?.Invoke(op, default!, default!);
                break;
        }
    }

    #region Echo Serialization Helpers

    private void WriteKey(BinaryWriter writer, TKey key)
    {
        var echo = Serializer.Serialize(key);
        echo.WriteToBinary(writer);
    }

    private TKey ReadKey(BinaryReader reader)
    {
        var echo = EchoObject.ReadFromBinary(reader);
        return Serializer.Deserialize<TKey>(echo)!;
    }

    private void WriteValue(BinaryWriter writer, TValue value)
    {
        var echo = Serializer.Serialize(value);
        echo.WriteToBinary(writer);
    }

    private TValue ReadValue(BinaryReader reader)
    {
        var echo = EchoObject.ReadFromBinary(reader);
        return Serializer.Deserialize<TValue>(echo)!;
    }

    #endregion

    #region SyncObject Implementation

    public override void ClearChanges() => _changes.Clear();

    public override void Reset()
    {
        _changes.Clear();
        _changesAhead = 0;
        _items.Clear();
    }

    public override void OnSerializeAll(BinaryWriter writer)
    {
        // Write the full dictionary
        writer.Write((uint)_items.Count);
        foreach (var kvp in _items)
        {
            WriteKey(writer, kvp.Key);
            WriteValue(writer, kvp.Value);
        }

        // Write how many changes are pending (client needs to skip these)
        writer.Write((uint)_changes.Count);
    }

    public override void OnSerializeDelta(BinaryWriter writer)
    {
        // Write all queued changes
        writer.Write((uint)_changes.Count);

        for (int i = 0; i < _changes.Count; i++)
        {
            var change = _changes[i];
            writer.Write((byte)change.Operation);

            switch (change.Operation)
            {
                case Operation.Add:
                case Operation.Set:
                    WriteKey(writer, change.Key);
                    WriteValue(writer, change.Item);
                    break;
                case Operation.Remove:
                    WriteKey(writer, change.Key);
                    break;
                case Operation.Clear:
                    // No data needed
                    break;
            }
        }
    }

    public override void OnDeserializeAll(BinaryReader reader)
    {
        // Read the full dictionary
        int count = (int)reader.ReadUInt32();

        _items.Clear();
        _changes.Clear();

        for (int i = 0; i < count; i++)
        {
            TKey key = ReadKey(reader);
            TValue value = ReadValue(reader);
            _items.Add(key, value);
        }

        // How many changes to skip
        _changesAhead = (int)reader.ReadUInt32();
    }

    public override void OnDeserializeDelta(BinaryReader reader)
    {
        int changesCount = (int)reader.ReadUInt32();

        for (int i = 0; i < changesCount; i++)
        {
            var operation = (Operation)reader.ReadByte();
            bool apply = _changesAhead == 0;

            TKey key = default!;
            TValue item = default!;
            TValue oldItem = default!;

            switch (operation)
            {
                case Operation.Add:
                case Operation.Set:
                    key = ReadKey(reader);
                    item = ReadValue(reader);
                    if (apply)
                    {
                        if (_items.TryGetValue(key, out oldItem!))
                        {
                            _items[key] = item;
                            AddOperation(Operation.Set, key, item, oldItem, false);
                        }
                        else
                        {
                            _items[key] = item;
                            AddOperation(Operation.Add, key, item, default!, false);
                        }
                    }
                    break;

                case Operation.Clear:
                    if (apply)
                    {
                        AddOperation(Operation.Clear, default!, default!, default!, false);
                        _items.Clear();
                    }
                    break;

                case Operation.Remove:
                    key = ReadKey(reader);
                    if (apply)
                    {
                        if (_items.TryGetValue(key, out oldItem!))
                        {
                            _items.Remove(key);
                            AddOperation(Operation.Remove, key, oldItem, oldItem, false);
                        }
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

    #region IDictionary<TKey, TValue> Implementation

    public TValue this[TKey key]
    {
        get => _items[key];
        set
        {
            if (_items.TryGetValue(key, out TValue? oldItem))
            {
                _items[key] = value;
                AddOperation(Operation.Set, key, value, oldItem, true);
            }
            else
            {
                _items[key] = value;
                AddOperation(Operation.Add, key, value, default!, true);
            }
        }
    }

    public void Add(TKey key, TValue value)
    {
        _items.Add(key, value);
        AddOperation(Operation.Add, key, value, default!, true);
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        AddOperation(Operation.Clear, default!, default!, default!, true);
        _items.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return _items.TryGetValue(item.Key, out TValue? val) &&
               EqualityComparer<TValue>.Default.Equals(val, item.Value);
    }

    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("The destination array is too small.");

        int i = arrayIndex;
        foreach (var item in _items)
        {
            array[i] = item;
            i++;
        }
    }

    public bool Remove(TKey key)
    {
        if (_items.TryGetValue(key, out TValue? oldItem) && _items.Remove(key))
        {
            AddOperation(Operation.Remove, key, oldItem, oldItem, true);
            return true;
        }
        return false;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (_items.Remove(item.Key))
        {
            AddOperation(Operation.Remove, item.Key, item.Value, item.Value, true);
            return true;
        }
        return false;
    }

    public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value!);

    #endregion

    #region Enumerator

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    #endregion
}
