// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GUI;

public struct Size
{
    public static Size Default => new(0, LayoutValueType.Pixel);

    public static Size Max => new(double.MaxValue, LayoutValueType.Pixel);

    private bool isLerp = false;

    public readonly double Value;
    public readonly double PixelOffset;
    public readonly LayoutValueType Type;

    private double _lerpValue;
    private double _lerpPixelOffset;
    private double _lerpTime;
    private LayoutValueType _lerpType;

    public Size() { }

    public Size(double value, LayoutValueType type)
    {
        Value = value;
        Type = type;
    }
    public Size(double value, double pixelOffset, LayoutValueType type)
    {
        Value = value;
        PixelOffset = pixelOffset;
        Type = type;
    }

    public double ToPixels(double parentValue)
    {
        if (Type == LayoutValueType.Percent)
            return (Value * parentValue) + PixelOffset;
        else if (isLerp)
        {
            Size a = new(Value, PixelOffset, Type);
            Size b = new(_lerpValue, _lerpPixelOffset, _lerpType);
            return MathD.Lerp(a.ToPixels(parentValue), b.ToPixels(parentValue), _lerpTime);
        }
        else
            return Value + PixelOffset;
    }

    public static Size Percentage(double normalized, double pixelOffset = 0) => new Size(normalized, pixelOffset, LayoutValueType.Percent);
    public static Size Pixels(double pixels, double pixelOffset = 0) => new Size(pixels, pixelOffset, LayoutValueType.Pixel);

    public static Size Lerp(Size a, Size b, double t)
    {
        if (a.isLerp) throw new System.Exception("Cannot lerp a lerp");
        if (b.isLerp) throw new System.Exception("Cannot lerp a lerp");

        Size lerped = a;
        lerped.isLerp = true;
        lerped._lerpValue = b.Value;
        lerped._lerpPixelOffset = b.PixelOffset;
        lerped._lerpTime = t;
        lerped._lerpType = b.Type;
        return lerped;
    }

    // Int to Size with type Pixels Cast
    public static implicit operator Size(int value) => new Size(value, LayoutValueType.Pixel);
    public static implicit operator Size(float value) => new Size(value, LayoutValueType.Pixel);
    public static implicit operator Size(double value) => new Size(value, LayoutValueType.Pixel);

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
