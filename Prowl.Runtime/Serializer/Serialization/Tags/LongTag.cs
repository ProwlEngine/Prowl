using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class LongTag : Tag
	{
		public long Value { get; set; }

		public LongTag(string tagName = "", long value = 0)
		{
			Name = tagName;
			Value = value;
        }

        public static explicit operator long(LongTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Long;

        public override Tag Clone() => new LongTag(Name, Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("LongTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
