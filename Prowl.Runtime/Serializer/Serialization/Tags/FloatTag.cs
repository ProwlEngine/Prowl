using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class FloatTag : Tag
	{
		public float Value { get; set; }

        public FloatTag() {}
        public FloatTag(float value = 0f)
		{
			Value = value;
        }

        public static explicit operator float(FloatTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Float;

        public override Tag Clone() => new FloatTag(Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("FloatTAG");
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
