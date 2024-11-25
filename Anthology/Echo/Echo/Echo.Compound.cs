// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class Echo
{
    public Dictionary<string, Echo> Tags => (Value as Dictionary<string, Echo>)!;

    public Echo this[string tagName]
    {
        get { return Get(tagName); }
        set
        {
            if (TagType != PropertyType.Compound)
                throw new InvalidOperationException("Cannot set tag on non-compound tag");
            else if (tagName == null)
                throw new ArgumentNullException(nameof(tagName));
            else if (value == null)
                throw new ArgumentNullException(nameof(value));
            Tags[tagName] = value;
            value.Parent = this;
        }
    }

    /// <summary> Gets a collection containing all tag names in this CompoundTag. </summary>
    public IEnumerable<string> GetNames() => Tags.Keys;

    /// <summary> Gets a collection containing all tags in this CompoundTag. </summary>
    public IEnumerable<Echo> GetAllTags() => Tags.Values;

    public Echo? Get(string tagName)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        else if (tagName == null)
            throw new ArgumentNullException(nameof(tagName));
        return Tags.TryGetValue(tagName, out var result) ? result : null;
    }

    public bool TryGet(string tagName, out Echo? result)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        return tagName != null ? Tags.TryGetValue(tagName, out result) : throw new ArgumentNullException(nameof(tagName));
    }

    public bool Contains(string tagName)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        return tagName != null ? Tags.ContainsKey(tagName) : throw new ArgumentNullException(nameof(tagName));
    }

    public void Add(string name, Echo newTag)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (newTag == null)
            throw new ArgumentNullException(nameof(newTag));
        else if (newTag == this)
            throw new ArgumentException("Cannot add tag to self");
        Tags.Add(name, newTag);
        newTag.Parent = this;
    }

    public bool Remove(string name)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));
        return Tags.Remove(name);
    }

    public bool TryFind(string path, out Echo? tag)
    {
        tag = Find(path);
        return tag != null;
    }

    public Echo? Find(string path)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        Echo currentTag = this;
        while (true)
        {
            var i = path.IndexOf('/');
            var name = i < 0 ? path : path[..i];
            if (!currentTag.TryGet(name, out Echo? tag) || tag == null)
                return null;

            if (i < 0)
                return tag;

            if (tag.TagType != PropertyType.Compound)
                return null;

            currentTag = tag;
            path = path[(i + 1)..];
        }
    }
}
