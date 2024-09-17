// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GUI;

public struct Offset
{
    public static Offset Default => new(0, LayoutValueType.Pixel);

    public static Offset Max => new(double.MaxValue, LayoutValueType.Pixel);

    private bool isLerp = false;

    public readonly double Value;
    public double PixelOffset;
    public readonly LayoutValueType Type;

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
            return MathD.Lerp(a.ToPixels(parentValue), b.ToPixels(parentValue), _lerpTime);
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
        if (a.isLerp) throw new System.Exception("Cannot lerp a lerp");
        if (b.isLerp) throw new System.Exception("Cannot lerp a lerp");

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

    public ulong GetHashCode64()
    {
        ulong hash = 17;
        hash = hash * 23 + (ulong)Value.GetHashCode();
        hash = hash * 23 + (ulong)PixelOffset.GetHashCode();
        hash = hash * 23 + (ulong)Type.GetHashCode();
        hash = hash * 23 + (ulong)_lerpValue.GetHashCode();
        hash = hash * 23 + (ulong)_lerpPixelOffset.GetHashCode();
        hash = hash * 23 + (ulong)_lerpTime.GetHashCode();
        hash = hash * 23 + (ulong)_lerpType.GetHashCode();
        return hash;
    }
}
