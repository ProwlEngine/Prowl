using System;
using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class DoubleTag : Tag
	{
		public double Value { get; set; }

        public DoubleTag() {}
        public DoubleTag(double value = 0.0)
		{
			Value = value;
        }

        public static explicit operator double(DoubleTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Double;

        public override Tag Clone() => new DoubleTag( Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("DoubleTAG");
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
