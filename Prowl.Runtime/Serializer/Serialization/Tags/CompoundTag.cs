using System;
using System.Collections.Generic;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class CompoundTag : Tag
    {
        protected Dictionary<string, Tag> Tags { get; set; }
		public string SerializedType = "";
		public int SerializedID = 0;

		public Tag this[string tagName]
		{
			get { return Get<Tag>(tagName); }
            set
            {
                if (tagName == null)
                    throw new ArgumentNullException("tagName");
                else if (value == null)
                    throw new ArgumentNullException("value");
                else if (value.Name != tagName)
                    throw new ArgumentException("Given tag name must match tag's actual name.");
                Tags[tagName] = value;
            }
        }


        /// <summary> Gets a collection containing all tag names in this CompoundTag. </summary>
        public IEnumerable<string> Names => Tags.Keys;
        /// <summary> Gets a collection containing all tags in this CompoundTag. </summary>
        public IEnumerable<Tag> AllTags => Tags.Values;

        public CompoundTag(string tagName = "") : this(tagName, new Tag[]{}) { }
		public CompoundTag(string tagName, IEnumerable<Tag> tags)
		{
			Name = tagName;
			Tags = new();
            SerializedType = "";
			SerializedID = 0;

			foreach (var tag in tags)
				Tags[tag.Name] = tag;
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
        public void Add(Tag newTag)
        {
            if (newTag == null)
                throw new ArgumentNullException("newTag");
            else if (newTag == this)
                throw new ArgumentException("Cannot add tag to self");
            else if (newTag.Name == null)
                throw new ArgumentException("Only named tags are allowed in compound tags.");
            Tags.Add(newTag.Name, newTag);
        }

        public bool Remove(Tag tag)
        {
            if (tag == null)
                throw new ArgumentNullException("tag");
            if (tag.Name == null)
                throw new ArgumentException("Trying to remove an unnamed tag.");
            Tag maybeItem;
            if (Tags.TryGetValue(tag.Name, out maybeItem))
                if (maybeItem == tag && Tags.Remove(tag.Name))
                    return true;
            return false;
        }

        public bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException("name");
            return Tags.Remove(name);
        }

        public override TagType GetTagType() => TagType.Compound;

        public override Tag Clone()
        {
            var tags = new List<Tag>();
            foreach (var tag in AllTags) tags.Add(tag.Clone());
            return new CompoundTag(Name, tags);
        }

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("CompoundTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0} entries\n", Tags.Count);

			sb.Append("{\n");
			foreach(Tag tag in AllTags) sb.AppendFormat("\t{0}\n", tag.ToString().Replace("\n", "\n\t"));
			sb.Append("}");
			return sb.ToString();
		}
	}
}
