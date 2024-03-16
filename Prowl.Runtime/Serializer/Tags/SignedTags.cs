namespace Prowl.Runtime
{

    public class sByteTag : Tag
    {
        public sbyte Value { get; set; }
        public sByteTag() { }
        public sByteTag(sbyte value) => Value = value;
        public override object GetValue() => Value;
        public override TagType GetTagType() => TagType.sByte;
        public override Tag Clone() => new sByteTag(Value);
    }

    public class ShortTag : Tag
    {
        public short Value { get; set; }
        public ShortTag() { }
        public ShortTag(short value = 0) => Value = value;
        public override object GetValue() => Value;
        public override TagType GetTagType() => TagType.Short;
        public override Tag Clone() => new ShortTag(Value);
    }

    public class IntTag : Tag
    {
        public int Value { get; set; }
        public IntTag() { }
        public IntTag(int value = 0) => Value = value;
        public override object GetValue() => Value;
        public override TagType GetTagType() => TagType.Int;
        public override Tag Clone() => new IntTag(Value);
    }

    public class LongTag : Tag
    {
        public long Value { get; set; }

        public LongTag() { }
        public LongTag(long value = 0) => Value = value;
        public override object GetValue() => Value;
        public override TagType GetTagType() => TagType.Long;
        public override Tag Clone() => new LongTag(Value);
    }
}
