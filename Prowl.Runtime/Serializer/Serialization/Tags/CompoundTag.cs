using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Prowl.Runtime.Serialization
{
    public class CompoundTag : Tag
    {
        public Dictionary<string, Tag> Tags { get; set; }
		public string SerializedType { get; set; } = "";
		public int SerializedID { get; set; } = 0;

		public Tag this[string tagName]
		{
			get { return Get<Tag>(tagName); }
            set
            {
                if (tagName == null)
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
        public IEnumerable<Tag> AllTags => Tags.Values;

        public CompoundTag() : this(new (string, Tag)[]{}) { }
		public CompoundTag(IEnumerable<(string, Tag)> tags)
		{
			Tags = new();
            SerializedType = "";
			SerializedID = 0;

			foreach (var tag in tags)
				Tags[tag.Item1] = tag.Item2;
		}

		public Tag Get(string tagName) => Get<Tag>(tagName);
		public T Get<T>(string tagName) where T : Tag
        {
            if (tagName == null)
                throw new ArgumentNullException("tagName");
            Tag result;
            if (Tags.TryGetValue(tagName, out result))
            {
                return (T)result;
            }
            return null;
        }
        public bool TryGet<T>(string tagName, out T result) where T : Tag
        {
            if (tagName == null)
                throw new ArgumentNullException("tagName");
            Tag tempResult;
            if (Tags.TryGetValue(tagName, out tempResult))
            {
                result = (T)tempResult;
                return true;
            }
            else
            {
                result = null;
                return false;
            }
        }
        public bool Contains(string tagName)
        {
            if (tagName == null)
                throw new ArgumentNullException("tagName");
            return Tags.ContainsKey(tagName);
        }
        public void Add(string name, Tag newTag)
        {
            if (newTag == null)
                throw new ArgumentNullException("newTag");
            else if (newTag == this)
                throw new ArgumentException("Cannot add tag to self");
            Tags.Add(name, newTag);
        }

        public bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException("name");
            return Tags.Remove(name);
        }

        #region Querying

        public bool TryFind<T>(string path, [MaybeNullWhen(false)] out T tag) where T : Tag
        {
            tag = Find<T>(path);
            return tag != null;
        }

        public bool TryFind(string path, [MaybeNullWhen(false)] out Tag tag)
        {
            tag = Find(path);
            return tag != null;
        }

        public T? Find<T>(string path) where T : Tag
        {
            var result = Find(path);
            if (result is T tagFound)
                return tagFound;

            return null;
        }

        public Tag? Find(string path)
        {
            CompoundTag currentTag = this;
            while (true)
            {
                var i = path.IndexOf('/');
                var name = i < 0 ? path : path[..i];
                if (!currentTag.TryGet(name, out Tag tag))
                    return null;

                if (i < 0)
                    return tag;

                if (tag is not CompoundTag c)
                    return null;

                currentTag = c;
                path = path[(i + 1)..];
            }
        }

        #endregion

        public override TagType GetTagType() => TagType.Compound;

        public override Tag Clone()
        {
            var tags = new List<(string, Tag)>();
            foreach (var tag in Tags) tags.Add((tag.Key, tag.Value.Clone()));
            return new CompoundTag(tags);
        }

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("CompoundTAG");
			sb.AppendFormat(": {0} entries\n", Tags.Count);

			sb.Append("{\n");
			foreach(Tag tag in AllTags) sb.AppendFormat("\t{0}\n", tag.ToString().Replace("\n", "\n\t"));
			sb.Append("}");
			return sb.ToString();
		}
	}
}
