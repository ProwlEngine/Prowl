namespace Prowl.Runtime.GUI
{
    public struct Offset
    {
        public static Offset Default => new(0, LayoutValueType.Pixel);

        public static Offset Max => new(double.MaxValue, LayoutValueType.Pixel);

        private bool isLerp = false;

        public double Value;
        public double PixelOffset;
        public LayoutValueType Type;

        private double _lerpValue;
        private double _lerpPixelOffset;
        private double _lerpTime;
        private LayoutValueType _lerpType;

        public Offset() { }

        public Offset(double value, LayoutValueType type)
        {
            Value = value;
            Type = type;
        }
        public Offset(double value, double pixelOffset, LayoutValueType type)
        {
            Value = value;
            PixelOffset = pixelOffset;
            Type = type;
        }

        public double ToPixels(double parentValue)
        {
            if (isLerp)
            {
                Offset a = new(Value, PixelOffset, Type);
                Offset b = new(_lerpValue, _lerpPixelOffset, _lerpType);
                return Mathf.Lerp(a.ToPixels(parentValue), b.ToPixels(parentValue), _lerpTime);
            }
            else if (Type == LayoutValueType.Percent)
                return (Value * parentValue) + PixelOffset;
            else
                return Value + PixelOffset;
        }

        public static Offset Percentage(double normalized, double pixelOffset = 0) => new Offset(normalized, pixelOffset, LayoutValueType.Percent);
        public static Offset Pixels(double pixels, double pixelOffset = 0) => new Offset(pixels, pixelOffset, LayoutValueType.Pixel);

        public static Offset Lerp(Offset a, Offset b, double t)
        {
            if(a.isLerp) throw new System.Exception("Cannot lerp a lerp");
            if(b.isLerp) throw new System.Exception("Cannot lerp a lerp");

            Offset lerped = a;
            lerped.isLerp = true;
            lerped._lerpValue = b.Value;
            lerped._lerpPixelOffset = b.PixelOffset;
            lerped._lerpTime = t;
            lerped._lerpType = b.Type;
            return lerped;
        }

        // Int to Size with type Pixels Cast
        public static implicit operator Offset(int value) => new Offset(value, LayoutValueType.Pixel);
        public static implicit operator Offset(float value) => new Offset(value, LayoutValueType.Pixel);
        public static implicit operator Offset(double value) => new Offset(value, LayoutValueType.Pixel);

    }

}
