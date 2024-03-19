using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prowl.Runtime
{
    public sealed partial class SerializedProperty
    {
        public Dictionary<string, SerializedProperty> Tags => Value as Dictionary<string, SerializedProperty>;
        public string SerializedType { get; set; } = "";
        public int SerializedID { get; set; } = 0;

        public SerializedProperty this[string tagName]
        {
            get { return Get(tagName); }
            set
            {
                if (TagType != PropertyType.Compound)
                    throw new InvalidOperationException("Cannot set tag on non-compound tag");
                else if(tagName == null)
                    throw new ArgumentNullException("tagName");
                else if (value == null)
                    throw new ArgumentNullException("value");
                Tags[tagName] = value;
            }
        }


        /// <summary> Gets a collection containing all tag names in this CompoundTag. </summary>
        [JsonIgnore]
        public IEnumerable<string> Names => Tags.Keys;
        /// <summary> Gets a collection containing all tags in this CompoundTag. </summary>
        [JsonIgnore]
        public IEnumerable<SerializedProperty> AllTags => Tags.Values;

        public SerializedProperty Get(string tagName)
        {
            if (TagType != PropertyType.Compound)
                throw new InvalidOperationException("Cannot get tag from non-compound tag");
            else if (tagName == null)
                throw new ArgumentNullException("tagName");
            return Tags.TryGetValue(tagName, out SerializedProperty result) ? result : null;
        }
        public bool TryGet(string tagName, out SerializedProperty result)
        {
            if (TagType != PropertyType.Compound)
                throw new InvalidOperationException("Cannot get tag from non-compound tag");
            if (tagName == null)
                throw new ArgumentNullException("tagName");
            return Tags.TryGetValue(tagName, out result);
        }
        public bool Contains(string tagName)
        {
            if (TagType != PropertyType.Compound)
                throw new InvalidOperationException("Cannot get tag from non-compound tag");
            if (tagName == null)
                throw new ArgumentNullException("tagName");
            return Tags.ContainsKey(tagName);
        }
        public void Add(string name, SerializedProperty newTag)
        {
            if (TagType != PropertyType.Compound)
                throw new InvalidOperationException("Cannot get tag from non-compound tag");
            if (newTag == null)
                throw new ArgumentNullException("newTag");
            else if (newTag == this)
                throw new ArgumentException("Cannot add tag to self");
            Tags.Add(name, newTag);
        }

        public bool Remove(string name)
        {
            if (TagType != PropertyType.Compound)
                throw new InvalidOperationException("Cannot get tag from non-compound tag");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException("name");
            return Tags.Remove(name);
        }

        #region Querying

        public bool TryFind(string path, [MaybeNullWhen(false)] out SerializedProperty tag)
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
                if (!currentTag.TryGet(name, out SerializedProperty tag))
                    return null;

                if (i < 0)
                    return tag;

                if (tag.TagType != PropertyType.Compound)
                    return null;

                currentTag = tag;
                path = path[(i + 1)..];
            }
        }

        #endregion

        /// <summary>
        /// Returns a new compound with all the tags/paths combined
        /// If a tag is found with identical Path/Name, and values are the same its kept, otherwise its discarded
        /// NOTE: Completely Untested
        /// </summary>
        public static SerializedProperty Merge(List<SerializedProperty> allTags)
        {
            SerializedProperty result = new SerializedProperty();
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

                    if (!tag.TryGet(nameVal.Key, out SerializedProperty nTag))
                        return false;

                    if (nTag.TagType != nameVal.Value.TagType)
                        return false;

                    switch (nameVal.Value.TagType)
                    {
                        case PropertyType.Compound:
                            mergedTag = Merge(allTags.Select(t => t.Get(nameVal.Key)).ToList());
                            return mergedTag != null;
                        case PropertyType.List:
                            mergedTag = Merge(allTags.Select(t => t.Get(nameVal.Key)).ToList());
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
                        if(applyTag.Value.TagType == PropertyType.Compound)
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
                    Tags.Add(applyTag.Key, applyTag.Value);
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


            SerializedProperty result = new SerializedProperty();

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
                else if (!kvp.Value.Equals(value))
                {
                    // Tag values are different
                    result.Add(kvp.Key, kvp.Value);
                }
            }

            return result;

        }
    }
}
