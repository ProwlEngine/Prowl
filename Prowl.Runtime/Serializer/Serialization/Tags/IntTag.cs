using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class IntTag : Tag
	{
		public int Value { get; set; }

		public IntTag(string name = "", int value = 0)
		{
			Name = name;
			Value = value;
        }

        public static explicit operator int(IntTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Int;

        public override Tag Clone() => new IntTag(Name, Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("IntTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
