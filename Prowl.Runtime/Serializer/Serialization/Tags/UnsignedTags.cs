namespace Prowl.Runtime.Serializer
{
    public class ByteTag : Tag
	{
		public byte Value { get; set; }
		public ByteTag() {}
        public ByteTag(byte value) => Value = value;
        public override TagType GetTagType() => TagType.Byte;
        public override Tag Clone() => new ByteTag(Value);
    }

    public class UShortTag : Tag
    {
        public ushort Value { get; set; }
        public UShortTag() { }
        public UShortTag(ushort value = 0) => Value = value;
        public override TagType GetTagType() => TagType.UShort;
        public override Tag Clone() => new UShortTag(Value);
    }

    public class UIntTag : Tag
    {
        public uint Value { get; set; }
        public UIntTag() { }
        public UIntTag(uint value = 0) => Value = value;
        public override TagType GetTagType() => TagType.UInt;
        public override Tag Clone() => new UIntTag(Value);
    }

    public class ULongTag : Tag
    {
        public ulong Value { get; set; }
        public ULongTag() { }
        public ULongTag(ulong value = 0) => Value = value;
        public override TagType GetTagType() => TagType.ULong;
        public override Tag Clone() => new ULongTag(Value);
    }
}
