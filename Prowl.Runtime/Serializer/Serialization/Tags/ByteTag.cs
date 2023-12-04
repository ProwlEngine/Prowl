using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class ByteTag : Tag
	{
		public byte Value { get; set; }

		public ByteTag() {}
		public ByteTag(byte value = 0x00)
		{
			Value = value;
        }

        public static explicit operator byte(ByteTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.Byte;

        public override Tag Clone() => new ByteTag(Value);

        public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("ByteTAG");
			sb.AppendFormat(": {0}", Value);
			return sb.ToString();
		}
	}
}
