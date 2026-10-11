// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    // Compounds above this size look keys up through a dictionary, smaller ones scan the entries.
    private const int IndexThreshold = 16;

    // Entries in insertion order.
    internal sealed class CompoundStorage
    {
        public KeyValuePair<string, EchoObject>[] Entries;
        public int Count;
        public int Version;
        public Dictionary<string, int>? Index;

        public CompoundStorage(int capacity)
        {
            Entries = capacity == 0 ? Array.Empty<KeyValuePair<string, EchoObject>>() : new KeyValuePair<string, EchoObject>[capacity];
        }

        public int IndexOf(string key)
        {
            if (Count > IndexThreshold)
            {
                Index ??= BuildIndex();
                return Index.TryGetValue(key, out int found) ? found : -1;
            }

            var entries = Entries;
            for (int i = 0; i < Count; i++)
                if (string.Equals(entries[i].Key, key))
                    return i;
            return -1;
        }

        public void Append(string key, EchoObject value)
        {
            if (Count == Entries.Length)
                Array.Resize(ref Entries, Math.Max(4, Count * 2));
            Index?.Add(key, Count);
            Entries[Count++] = new KeyValuePair<string, EchoObject>(key, value);
            Version++;
        }

        public void RemoveAt(int index)
        {
            Count--;
            if (index < Count)
                Array.Copy(Entries, index + 1, Entries, index, Count - index);
            Entries[Count] = default;
            Index = null;
            Version++;
        }

        private Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>(Count);
            for (int i = 0; i < Count; i++)
                index[Entries[i].Key] = i;
            return index;
        }
    }

    private CompoundStorage Compound => (CompoundStorage)_ref!;

    internal CompoundStorage CompoundEntries => TagType == EchoType.Compound
        ? Compound
        : throw new InvalidOperationException("Cannot get tags from non-compound tag");

    /// <summary> A read only view of this compound's tags, in insertion order. </summary>
    public CompoundTags Tags => new(this);

    private void SetCompoundEntries(IEnumerable<KeyValuePair<string, EchoObject>> entries, int capacity)
    {
        var storage = new CompoundStorage(capacity);
        foreach (var (key, tag) in entries)
        {
            tag.Parent = this;
            tag.CompoundKey = key;
            storage.Append(key, tag);
        }
        _ref = storage;
    }

    /// <summary>
    /// Get or set a tag by name in this CompoundTag.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <returns>The tag if found, otherwise null</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or empty or the value that is being set is null</exception>
    public EchoObject this[string tagName]
    {
        get { return Get(tagName); }
        set
        {
            if (TagType != EchoType.Compound)
                throw new InvalidOperationException("Cannot set tag on non-compound tag");

            if (string.IsNullOrEmpty(tagName))
                throw new ArgumentNullException(nameof(tagName));

            if (value is null)
                throw new ArgumentNullException(nameof(value));

            if (value.Parent is not null)
                throw new ArgumentException("Tag already has a parent, Did you want to clone this?", nameof(value));

            var storage = Compound;
            int index = storage.IndexOf(tagName);
            if (index >= 0)
            {
                var oldValue = storage.Entries[index].Value;
                oldValue.Parent = null;
                oldValue.CompoundKey = null;
                storage.Entries[index] = new KeyValuePair<string, EchoObject>(tagName, value);
            }
            else
            {
                storage.Append(tagName, value);
            }

            value.Parent = this;
            value.CompoundKey = tagName;
        }
    }

    /// <summary> Gets a collection containing all tag names in this CompoundTag. </summary>
    /// <returns>A collection of all tag names</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public IEnumerable<string> GetNames()
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get all tag names on non-compound tag");
        return Tags.Keys;
    }

    /// <summary> Gets a collection containing all tags in this CompoundTag. </summary>
    /// <returns>A collection of all tags</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public IEnumerable<EchoObject> GetAllTags()
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get all tags on non-compound tag");
        return Tags.Values;
    }

    /// <summary>
    /// Get a tag from this compound tag by name.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <returns>The tag if found, otherwise null</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or empty</exception>
    public EchoObject? Get(string tagName)
    {
        return TryGet(tagName, out var result) ? result : null;
    }

    /// <summary>
    /// Try to get a tag from this compound tag by name.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <param name="result">The tag if found, otherwise null</param>
    /// <returns>True if the tag was found, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or empty</exception>
    public bool TryGet(string tagName, out EchoObject? result)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrEmpty(tagName))
            throw new ArgumentNullException(nameof(tagName));

        var storage = Compound;
        int index = storage.IndexOf(tagName);
        result = index >= 0 ? storage.Entries[index].Value : null;
        return index >= 0;
    }

    // Checks the entry at hint first, which hits when tags are read in the order they were written.
    internal bool TryGet(string tagName, int hint, out EchoObject? result)
    {
        var storage = Compound;
        if ((uint)hint < (uint)storage.Count && string.Equals(storage.Entries[hint].Key, tagName))
        {
            result = storage.Entries[hint].Value;
            return true;
        }
        return TryGet(tagName, out result);
    }

    /// <summary>
    /// Check if this compound tag contains a tag by name.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <returns>True if the tag exists, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or empty</exception>
    public bool Contains(string tagName) => TryGet(tagName, out _);

    /// <summary>
    /// Add a tag to this compound tag.
    /// </summary>
    /// <param name="name">The name of the tag</param>
    /// <param name="newTag">The tag to add</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or empty</exception>
    /// <exception cref="ArgumentException">Thrown if the new tag is null or the same as this tag</exception>
    public void Add(string name, EchoObject newTag)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        if (newTag is null)
            throw new ArgumentException(null, nameof(newTag));
        else if (object.ReferenceEquals(newTag, this))
            throw new ArgumentException("Cannot add tag to self", nameof(newTag));

        if (newTag.Parent is not null)
            throw new ArgumentException("Tag already has a parent, Did you want to clone this?", nameof(newTag));

        var storage = Compound;
        if (storage.IndexOf(name) >= 0)
            throw new ArgumentException("Tag with this name already exists", nameof(name));

        storage.Append(name, newTag);
        newTag.Parent = this;
        newTag.CompoundKey = name;
    }

    // Add for callers that already guarantee a new, unparented tag under a unique name.
    internal void AddUnique(string name, EchoObject newTag)
    {
        Compound.Append(name, newTag);
        newTag.Parent = this;
        newTag.CompoundKey = name;
    }

    /// <summary>
    /// Remove a tag from this compound tag by name.
    /// </summary>
    /// <param name="name">The name of the tag to remove</param>
    /// <returns>True if the tag was removed, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or empty</exception>
    public bool Remove(string name)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        var storage = Compound;
        int index = storage.IndexOf(name);
        if (index < 0)
            return false;

        var tag = storage.Entries[index].Value;
        storage.RemoveAt(index);
        tag.Parent = null;
        tag.CompoundKey = null;
        return true;
    }

    /// <summary>
    /// Rename a tag in this compound tag.
    /// </summary>
    /// <param name="oldName">The old name of the tag</param>
    /// <param name="newName">The new name of the tag</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the old or new name is null or empty</exception>
    /// <exception cref="ArgumentException">Thrown if the old name doesn't exist or the new name already exists</exception>
    public void Rename(string oldName, string newName)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot rename tag in non-compound tag");
        if (string.IsNullOrEmpty(oldName))
            throw new ArgumentNullException(nameof(oldName));
        if (string.IsNullOrEmpty(newName))
            throw new ArgumentNullException(nameof(newName));
        if (oldName == newName) return;

        var storage = Compound;
        int index = storage.IndexOf(oldName);
        if (index < 0)
            throw new ArgumentException("Tag with old name doesn't exist", nameof(oldName));
        if (storage.IndexOf(newName) >= 0)
            throw new ArgumentException("Tag with new name already exists", nameof(newName));

        var tag = storage.Entries[index].Value;
        storage.Entries[index] = new KeyValuePair<string, EchoObject>(newName, tag);
        storage.Index = null;
        tag.CompoundKey = newName;
    }

    /// <summary>
    /// Get the type stored inside this compound if it was serialized with type information.
    /// This is useful in networked environments for security reasons, when you want to confirm the type of the object before deserializing it.
    /// Otherwise a malicious actor could send a serialized object with a malicious type and cause a security vulnerability.
    ///
    /// For example, Maybe in your networked game you have an IPacket type that gets serialized by Echo and sent back and forth.
    /// When the server goes to deserialize the packet, it can use this method to confirm that the type is actually IPacket before deserializing it.
    /// Otherwise a client could send a malicious packet with a different type maybe a OpenGL Texture type or something and trigger a memory leak.
    /// </summary>
    /// <returns>The type stored in this compound tag, or null if no type was stored</returns>
    public Type? GetStoredType()
    {
        if (TryGet("$type", out var typeTag))
            return ReflectionUtils.FindTypeByName(typeTag!.StringValue);
        return null;
    }

}

/// <summary> A read only view of a compound's tags, in insertion order. Change tags through the EchoObject itself. </summary>
public readonly struct CompoundTags : IReadOnlyDictionary<string, EchoObject>
{
    private readonly EchoObject _owner;

    internal CompoundTags(EchoObject owner) => _owner = owner;

    public int Count => _owner.Count;

    public EchoObject this[string key] =>
        _owner.TryGet(key, out var value) ? value! : throw new KeyNotFoundException($"The tag '{key}' was not found.");

    public bool ContainsKey(string key) => _owner.Contains(key);

    public bool TryGetValue(string key, out EchoObject value)
    {
        bool found = _owner.TryGet(key, out var result);
        value = result!;
        return found;
    }

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var entry in this)
                yield return entry.Key;
        }
    }

    public IEnumerable<EchoObject> Values
    {
        get
        {
            foreach (var entry in this)
                yield return entry.Value;
        }
    }

    public Enumerator GetEnumerator() => new(_owner.CompoundEntries);

    IEnumerator<KeyValuePair<string, EchoObject>> IEnumerable<KeyValuePair<string, EchoObject>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<KeyValuePair<string, EchoObject>>
    {
        private readonly EchoObject.CompoundStorage _storage;
        private readonly int _version;
        private int _index;

        internal Enumerator(EchoObject.CompoundStorage storage)
        {
            _storage = storage;
            _version = storage.Version;
            _index = -1;
        }

        public KeyValuePair<string, EchoObject> Current => _storage.Entries[_index];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_version != _storage.Version)
                throw new InvalidOperationException("The compound was modified while it was being enumerated.");
            return ++_index < _storage.Count;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
