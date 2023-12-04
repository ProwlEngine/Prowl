using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class DoubleTag : Tag
	{
		public double Value { get; set; }

        public DoubleTag() { Name = ""; }
        public DoubleTag(string tagName = "", double value = 0.0)
		{
			Name = tagName;
			Value = value;
        }

        public static explicit operator double(DoubleTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Double;

        public override Tag Clone() => new DoubleTag(Name, Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("DoubleTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
