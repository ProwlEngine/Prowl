using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class IntTag : Tag
	{
		public int Value { get; set; }

        public IntTag() { }
        public IntTag(int value = 0)
		{
			Value = value;
        }

        public static explicit operator int(IntTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Int;

        public override Tag Clone() => new IntTag(Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("IntTAG");
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
