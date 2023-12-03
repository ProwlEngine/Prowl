using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class ShortTag : Tag
	{
		public short Value { get; set; }

		public ShortTag(string tagName = "", short value = 0)
		{
			Name = tagName;
			Value = value;
        }

        public static explicit operator short(ShortTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Short;

        public override Tag Clone() => new ShortTag(Name, Value);

        public override string ToString()
		{
            StringBuilder sb = new();
			sb.Append("ShortTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
