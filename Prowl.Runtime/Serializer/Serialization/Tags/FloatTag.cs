using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class FloatTag : Tag
	{
		public float Value { get; set; }

		public FloatTag(string tagName = "", float value = 0f)
		{
			Name = tagName;
			Value = value;
        }

        public static explicit operator float(FloatTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Float;

        public override Tag Clone() => new FloatTag(Name, Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("FloatTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
