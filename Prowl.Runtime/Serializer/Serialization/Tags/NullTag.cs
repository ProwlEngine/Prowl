using System.Text;

namespace Prowl.Runtime.Serialization
{
    public class NullTag : Tag
    {
        public NullTag() {}

        public override TagType GetTagType() => TagType.Null;

        public override Tag Clone() => new NullTag();

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("NullTag");
            return sb.ToString();
        }
    }
}
