using System.IO;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class StringTag : Tag
    {
        public string Value { get; set; }

        public StringTag(string tagName = "", string value = "")
        {
            Name = tagName;
            Value = value;
        }

        public static explicit operator string(StringTag tag) => tag.Value;

        public override TagType GetTagType() => TagType.String;

        public override Tag Clone() => new StringTag(Name, Value);

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("StringTAG");
            if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
            sb.AppendFormat(": {0}", Value);
            return sb.ToString();
        }
    }
}
