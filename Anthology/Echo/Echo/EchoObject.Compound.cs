// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.ComponentModel.DataAnnotations;

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    public Dictionary<string, EchoObject> Tags => (Value as Dictionary<string, EchoObject>)!;

    /// <summary>
    /// Get or set a tag by name in this CompoundTag.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <returns>The tag if found, otherwise null</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or whitespace or the value that is being set is null</exception>
    public EchoObject this[string tagName]
    {
        get { return Get(tagName); }
        set
        {
            if (TagType != EchoType.Compound)
                throw new InvalidOperationException("Cannot set tag on non-compound tag");

            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentNullException(nameof(tagName));

            ArgumentNullException.ThrowIfNull(value, nameof(value));

            if (value.Parent != null)
                throw new ArgumentException("Tag already has a parent, Did you want to clone this?", nameof(value));

            var oldValue = Tags.TryGetValue(tagName, out var existingTag) ? existingTag : null;

            if (oldValue != null)
            {
                oldValue.Parent = null;
                oldValue.CompoundKey = null;
                OnPropertyChanged(new EchoChangeEventArgs(
                    this, oldValue, oldValue.Value, null, ChangeType.TagRemoved));
            }

            Tags[tagName] = value;
            value.Parent = this;
            value.CompoundKey = tagName;

            OnPropertyChanged(new EchoChangeEventArgs(
                this, value, null, value.Value, ChangeType.TagAdded));
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
    /// <exception cref="ArgumentNullException">Thrown if the name is null or whitespace</exception>
    public EchoObject? Get(string tagName)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentNullException(nameof(tagName));

        return Tags.TryGetValue(tagName, out var result) ? result : null;
    }

    /// <summary>
    /// Try to get a tag from this compound tag by name.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <param name="result">The tag if found, otherwise null</param>
    /// <returns>True if the tag was found, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or whitespace</exception>
    public bool TryGet(string tagName, out EchoObject? result)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentNullException(nameof(tagName));

        return Tags.TryGetValue(tagName, out result);
    }

    /// <summary>
    /// Check if this compound tag contains a tag by name.
    /// </summary>
    /// <param name="tagName">The name of the tag to check for</param>
    /// <returns>True if the tag exists, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or whitespace</exception>
    public bool Contains(string tagName)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentNullException(nameof(tagName));
        return Tags.ContainsKey(tagName);
    }

    /// <summary>
    /// Add a tag to this compound tag.
    /// </summary>
    /// <param name="name">The name of the tag</param>
    /// <param name="newTag">The tag to add</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or whitespace</exception>
    /// <exception cref="ArgumentException">Thrown if the new tag is null or the same as this tag</exception>
    public void Add(string name, EchoObject newTag)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (newTag == null)
            throw new ArgumentException(null, nameof(newTag));
        else if (object.ReferenceEquals(newTag, this))
            throw new ArgumentException("Cannot add tag to self", nameof(newTag));

        if (newTag.Parent != null)
            throw new ArgumentException("Tag already has a parent, Did you want to clone this?", nameof(newTag));

        if (Tags.TryAdd(name, newTag))
        {
            newTag.Parent = this;
            newTag.CompoundKey = name;

            OnPropertyChanged(new EchoChangeEventArgs(
                this,           // Source is this compound
                newTag,         // Property is the new tag
                null,           // Old value null since it's an add
                newTag.Value,   // New value is the tag's value
                ChangeType.TagAdded));
        }
        else
        {
            throw new ArgumentException("Tag with this name already exists", nameof(name));
        }
    }

    /// <summary>
    /// Remove a tag from this compound tag by name.
    /// </summary>
    /// <param name="name">The name of the tag to remove</param>
    /// <returns>True if the tag was removed, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the name is null or whitespace</exception></exception>
    public bool Remove(string name)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (Tags.Remove(name, out var tag))
        {
            // Fire change event before clearing parent/key
            OnPropertyChanged(new EchoChangeEventArgs(
                this,       // Source is this compound
                tag,        // Property is the removed tag
                tag.Value,  // Old value is the tag's current value
                null,       // New value null since it's a remove
                ChangeType.TagRemoved));

            tag.Parent = null;
            tag.CompoundKey = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rename a tag in this compound tag.
    /// </summary>
    /// <param name="oldName">The old name of the tag</param>
    /// <param name="newName">The new name of the tag</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    /// <exception cref="ArgumentNullException">Thrown if the old or new name is null or whitespace</exception>
    /// <exception cref="ArgumentException">Thrown if the old name doesn't exist or the new name already exists</exception>
    public void Rename(string oldName, string newName)
    {
        if (TagType != EchoType.Compound)
            throw new InvalidOperationException("Cannot rename tag in non-compound tag");
        if (string.IsNullOrWhiteSpace(oldName))
            throw new ArgumentNullException(nameof(oldName));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentNullException(nameof(newName));
        if (oldName == newName) return;
        if (!Tags.ContainsKey(oldName))
            throw new ArgumentException("Tag with old name doesn't exist", nameof(oldName));
        if (Tags.ContainsKey(newName))
            throw new ArgumentException("Tag with new name already exists", nameof(newName));

        var tag = Tags[oldName];
        Tags.Remove(oldName);
        Tags.Add(newName, tag);
        tag.CompoundKey = newName;

        OnPropertyChanged(new EchoChangeEventArgs(
            this,     // Source is this compound
            tag,      // Property is the renamed tag
            oldName,  // Old value is the old name
            newName,  // New value is the new name
            ChangeType.TagRenamed));
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
