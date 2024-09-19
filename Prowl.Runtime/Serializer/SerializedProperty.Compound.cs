// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime;

public sealed partial class SerializedProperty
{
    public Dictionary<string, SerializedProperty> Tags => (Value as Dictionary<string, SerializedProperty>)!;

    public SerializedProperty this[string tagName]
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
    public IEnumerable<SerializedProperty> GetAllTags() => Tags.Values;

    public SerializedProperty? Get(string tagName)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        else if (tagName == null)
            throw new ArgumentNullException(nameof(tagName));
        return Tags.TryGetValue(tagName, out var result) ? result : null;
    }

    public bool TryGet(string tagName, out SerializedProperty? result)
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

    public void Add(string name, SerializedProperty newTag)
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

    public bool TryFind(string path, out SerializedProperty? tag)
    {
        tag = Find(path);
        return tag != null;
    }

    public SerializedProperty? Find(string path)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get tag from non-compound tag");
        SerializedProperty currentTag = this;
        while (true)
        {
            var i = path.IndexOf('/');
            var name = i < 0 ? path : path[..i];
            if (!currentTag.TryGet(name, out SerializedProperty? tag) || tag == null)
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
    /// Returns a new compound with all the tags/paths combined
    /// If a tag is found with identical Path/Name, and values are the same its kept, otherwise its discarded
    /// NOTE: Completely Untested
    /// </summary>
    public static SerializedProperty Merge(List<SerializedProperty> allTags)
    {
        SerializedProperty result = NewCompound();
        if (allTags.Count == 0) return result;

        var referenceTag = allTags[0];
        if (referenceTag.TagType != PropertyType.Compound) return result;

        foreach (var nameVal in referenceTag.Tags)
        {
            SerializedProperty mergedTag = null;

            if (allTags.Skip(1).All(tag =>
                {
                    if (tag.TagType != PropertyType.Compound)
                        return false;

                    if (!tag.TryGet(nameVal.Key, out SerializedProperty? nTag))
                        return false;

                    if (nTag.TagType != nameVal.Value.TagType)
                        return false;

                    switch (nameVal.Value.TagType)
                    {
                        case PropertyType.Compound:
                        case PropertyType.List:
                            mergedTag = Merge(allTags.Where(t => t != null).Select(t => t?.Get(nameVal.Key)!).ToList());
                            return mergedTag != null;
                        default:
                            if (nameVal.Value.Value?.Equals(nTag.Value) != false)
                            {
                                mergedTag = nameVal.Value;
                                return true;
                            }
                            return false;
                    }
                }))
            {
                result.Add(nameVal.Key, mergedTag);
            }
        }

        return result;
    }

    /// <summary>
    /// Apply the given CompoundTag to this CompoundTag
    /// All tags inside the given CompoundTag will be added to this CompoundTag or replaced if they already exist
    /// All instances of this CompoundTag will remain the same just their values will be updated
    /// NOTE: Completely Untested
    /// </summary>
    /// <param name="tag"></param>
    public void Apply(SerializedProperty toApply)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot apply to a non-compound tag");
        if (toApply.TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot apply a non-compound tag");

        foreach (var applyTag in toApply.Tags)
        {
            if (Tags.TryGetValue(applyTag.Key, out SerializedProperty? tag))
            {
                if (tag.TagType == PropertyType.Compound)
                {
                    if (applyTag.Value.TagType == PropertyType.Compound)
                        tag.Apply(applyTag.Value);
                    else
                        throw new InvalidOperationException("Cannot apply a non-compound tag to a compound tag");
                }
                else
                {
                    // Clone it so that Value isnt a reference
                    SerializedProperty cloned = applyTag.Value.Clone();
                    tag.Value = cloned.Value;
                }
            }
            else
            {
                // Add the new tag
                Add(applyTag.Key, applyTag.Value);
            }
        }
    }

    /// <summary>
    /// Returns a new CompoundTag that contains all the tags from us who vary from the given CompoundTag
    /// For example if we have an additional tag that the given CompoundTag does not have, it will be included in the result
    /// Or if we have a tag with a different value, it will be included in the result
    /// This will occur recursively for CompoundTags
    /// NOTE: Completely Untested
    /// </summary>
    /// <param name="from">The Given CompoundTag To Compare Against</param>
    public SerializedProperty DifferenceFrom(SerializedProperty from)
    {
        if (TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get the difference with a non-compound tag");
        if (from.TagType != PropertyType.Compound)
            throw new InvalidOperationException("Cannot get the difference from a non-compound tag");


        SerializedProperty result = NewCompound();

        foreach (var kvp in Tags)
        {
            if (!from.Tags.TryGetValue(kvp.Key, out SerializedProperty? value))
            {
                // Tag exists in this tag but not in the 'from' tag
                result.Add(kvp.Key, kvp.Value);
            }
            else if (kvp.Value.TagType == PropertyType.Compound && value.TagType == PropertyType.Compound)
            {
                // Recursively get the difference for compound tags
                SerializedProperty subDifference = kvp.Value.DifferenceFrom(value);
                if (subDifference.Tags.Count > 0)
                {
                    result.Add(kvp.Key, subDifference);
                }
            }
            else if (!kvp.Value.Value.Equals(value.Value))
            {
                // Tag values are different
                result.Add(kvp.Key, kvp.Value);
            }
        }

        return result;

    }
}
