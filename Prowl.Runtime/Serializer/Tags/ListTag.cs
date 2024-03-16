using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Prowl.Runtime
{
    public class ListTag : Tag
    {
        public List<Tag> Tags { get; set; }

        [JsonIgnore]
        public int Count => Tags.Count;

        public Tag this[int tagIdx]
        {
            get { return Get<Tag>(tagIdx); }
            set { Tags[tagIdx] = value; }
        }

        public ListTag() : this(new Tag[] { }) { }
        public ListTag(IEnumerable<Tag> tags)
        {
            Tags = new List<Tag>();
            if (tags != null) Tags.AddRange(tags);
        }

        public Tag Get(int tagIdx) => Get<Tag>(tagIdx);
        public T Get<T>(int tagIdx) where T : Tag => (T)Tags[tagIdx];
        public void Add(Tag tag) => Tags.Add(tag);
        public override object GetValue() => Tags;
        public override TagType GetTagType() => TagType.List;
        public override Tag Clone()
        {
            var tags = new List<Tag>();
            foreach (var tag in Tags) tags.Add(tag.Clone());
            return new ListTag(tags);
        }
    }
}
