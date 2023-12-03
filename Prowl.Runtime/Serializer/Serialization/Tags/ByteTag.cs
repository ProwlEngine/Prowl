using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class ByteTag : Tag
	{
		public byte Value { get; set; }

		public ByteTag(string name = "", byte value = 0x00)
		{
			Name = name;
			Value = value;
        }

        public static explicit operator byte(ByteTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Byte;

        public override Tag Clone() => new ByteTag(Name, Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("ByteTAG");
			if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
