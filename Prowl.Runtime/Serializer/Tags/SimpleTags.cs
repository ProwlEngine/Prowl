using System.Text;

namespace Prowl.Runtime
{
    public class NullTag : Tag
    {
        public NullTag() { }
        public override TagType GetTagType() => TagType.Null;
        public override Tag Clone() => new NullTag();
    }

    public class StringTag : Tag
    {
        public string Value { get; set; }
        public StringTag() { }
        public StringTag(string value = "") => Value = value;
        public override TagType GetTagType() => TagType.String;
        public override Tag Clone() => new StringTag(Value);
    }

    public class ByteArrayTag : Tag
    {
        public byte[] Value { get; set; }
        public ByteArrayTag() : this(System.Array.Empty<byte>()) { }
        public ByteArrayTag(byte[] value)
        {
            value ??= new byte[] { };
            Value = (byte[])value.Clone();
        }
        public override TagType GetTagType() => TagType.ByteArray;
        public override Tag Clone() => new ByteArrayTag((byte[])Value.Clone());
    }

    public class BoolTag : Tag
    {
        public bool Value { get; set; }
        public BoolTag() { }
        public BoolTag(bool value) => Value = value;
        public override TagType GetTagType() => TagType.Bool;
        public override Tag Clone() => new BoolTag(Value);
    }
}
