using Newtonsoft.Json.Linq;
using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class NullTag : Tag
    {
        public NullTag(string tagName = "") => Name = tagName;

        public override TagType GetTagType() => TagType.Null;

        public override Tag Clone() => new NullTag(Name);

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("NullTag");
            if (Name.Length > 0) sb.AppendFormat("(\"{0}\")", Name);
            return sb.ToString();
        }
    }
}
