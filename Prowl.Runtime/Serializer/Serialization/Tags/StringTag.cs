using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class StringTag : Tag
    {
        public string Value { get; set; }

        public StringTag() {}
        public StringTag(string value = "")
        {
            Value = value;
        }

        public static explicit operator string(StringTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.String;

        public override Tag Clone() => new StringTag(Value);

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("StringTAG");
            sb.AppendFormat(": {0}", Value);
            return sb.ToString();
        }
    }
}
