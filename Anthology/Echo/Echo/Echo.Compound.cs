// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    public Dictionary<string, EchoObject> Tags => (Value as Dictionary<string, EchoObject>)!;

    public EchoObject this[string tagName]
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
    public IEnumerable<EchoObject> GetAllTags() => Tags.Values;

    public EchoObject? Get(string tagName)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        else if (tagName == null)
            throw new ArgumentNullException(nameof(tagName));
        return Tags.TryGetValue(tagName, out var result) ? result : null;
    }

    public bool TryGet(string tagName, out EchoObject? result)
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

    public void Add(string name, EchoObject newTag)
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

    public bool TryFind(string path, out EchoObject? tag)
    {
        tag = Find(path);
        return tag != null;
    }

    public EchoObject? Find(string path)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        EchoObject currentTag = this;
        while (true)
        {
            var i = path.IndexOf('/');
            var name = i < 0 ? path : path[..i];
            if (!currentTag.TryGet(name, out EchoObject? tag) || tag == null)
                return null;

            if (i < 0)
                return tag;

            if (tag.TagType != PropertyType.Compound)
                return null;

            currentTag = tag;
            path = path[(i + 1)..];
        }
    }
    /// <summary>
    /// Write this tag to a binary file in the Echo format.
    /// </summary>
    /// <param name="file">The file to write to</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public void WriteToBinary(FileInfo file)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot convert non-compound tag to Binary");

        using var stream = file.OpenWrite();
        using var writer = new BinaryWriter(stream);
        BinaryTagConverter.WriteTo(this, writer);
    }

    /// <summary>
    /// Write this tag to a binary file in the Echo format.
    /// </summary>
    /// <param name="writer">The writer to write to</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public void WriteToBinary(BinaryWriter writer)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot convert non-compound tag to Binary");

        BinaryTagConverter.WriteTo(this, writer);
    }

    /// <summary>
    /// Read a tag from a binary file in the Echo format.
    /// </summary>
    /// <param name="file">The file to read from</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromBinary(FileInfo file)
    {
        return BinaryTagConverter.ReadFromFile(file);
    }

    /// <summary>
    /// Read a tag from a binary file in the Echo format.
    /// </summary>
    /// <param name="reader">The reader to read from</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromBinary(BinaryReader reader)
    {
        return BinaryTagConverter.ReadFrom(reader);
    }


    /// <summary>
    /// Write this tag to a string in the Echo format.
    /// </summary>
    /// <param name="file">The file to write to</param>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public void WriteToString(FileInfo file)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot convert non-compound tag to String");

        StringTagConverter.WriteToFile(this, file);
    }

    /// <summary>
    /// Write this tag to a string in the Echo format.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public string WriteToString()
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot convert non-compound tag to String");

        return StringTagConverter.Write(this);
    }

    /// <summary>
    /// Read a tag from a file in the Echo format.
    /// </summary>
    /// <param name="file">The file to read from</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromString(FileInfo file)
    {
        return StringTagConverter.ReadFromFile(file);
    }

    /// <summary>
    /// Read a tag from a string in the Echo format.
    /// </summary>
    /// <param name="input">The string to read from</param>
    /// <returns>The tag read from the string</returns>
    public static EchoObject ReadFromString(string input)
    {
        return StringTagConverter.Read(input);
    }

}
