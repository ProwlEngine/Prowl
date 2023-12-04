namespace Prowl.Runtime.Serialization
{
    public class FloatTag : Tag
	{
		public float Value { get; set; }
        public FloatTag() {}
        public FloatTag(float value = 0f) => Value = value;
        public override TagType GetTagType() => TagType.Float;
        public override Tag Clone() => new FloatTag(Value);
    }

    public class DoubleTag : Tag
    {
        public double Value { get; set; }
        public DoubleTag() { }
        public DoubleTag(double value = 0.0) => Value = value;
        public override TagType GetTagType() => TagType.Double;
        public override Tag Clone() => new DoubleTag(Value);
    }

    public class DecimalTag : Tag
    {
        public decimal Value { get; set; }
        public DecimalTag() { }
        public DecimalTag(decimal value = 0.0m) => Value = value;
        public override TagType GetTagType() => TagType.Decimal;
        public override Tag Clone() => new DecimalTag(Value);
    }
}
