// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace Prowl.Runtime;

[StructLayout(LayoutKind.Sequential)]
public struct Color : IEquatable<Color>
{
    public float r, g, b, a;

    public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;

    public static Color black => new(0f, 0f, 0f, 1f);

    public static Color blue => new(0f, 0f, 1f, 1f);

    public static Color clear => new(0f, 0f, 0f, 0f);

    public static Color cyan => new(0f, 1f, 1f, 1f);

    public static Color gray => new(0.5f, 0.5f, 0.5f, 1f);

    public static Color green => new(0f, 1f, 0f, 1f);

    public static Color grey => new(0.5f, 0.5f, 0.5f, 1f);

    public static Color magenta => new(1f, 0f, 1f, 1f);

    public static Color red => new(1f, 0f, 0f, 1f);

    public static Color white => new(1f, 1f, 1f, 1f);

    public static Color yellow => new(1f, 0.9215f, 0.0156f, 1f);

    public float this[int index]
    {
        get
        {
            return index switch
            {
                0 => r,
                1 => g,
                2 => b,
                3 => a,
                _ => throw new IndexOutOfRangeException("Invalid Color index.")
            };

        }
        set
        {
            _ = index switch
            {
                0 => r = value,
                1 => g = value,
                2 => b = value,
                3 => a = value,
                _ => throw new IndexOutOfRangeException("Invalid Color index.")
            };
        }
    }

    public Color(float r, float g, float b, float a)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public Color(float r, float g, float b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        a = 1f;
    }

    public Color(byte r, byte g, byte b, byte a)
    {
        this.r = r / 255f;
        this.g = g / 255f;
        this.b = b / 255f;
        this.a = a / 255f;
    }

    public Color(byte r, byte g, byte b)
    {
        this.r = r / 255f;
        this.g = g / 255f;
        this.b = b / 255f;
        a = 1f;
    }

    public uint GetUInt() => ((Color32)this).GetUInt();

    public static Color Lerp(Color a, Color b, float t)
    {
        t = MathF.Min(MathF.Max(t, 0f), 1f);
        return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }

    // Source: https://gist.github.com/doomlaser/c7b894fa4936297195a053eda21fc0a0
    public static Color FromHSV(float h, float s, float v, float a = 1)
    {
        // no saturation, we can return the value across the board (grayscale)
        if (s == 0)
            return new Color(v, v, v, a);

        // which chunk of the rainbow are we in?
        float sector = h / 60;

        // split across the decimal (ie 3.87 into 3 and 0.87)
        int i = (int)sector;
        float f = sector - i;

        float p = v * (1 - s);
        float q = v * (1 - s * f);
        float t = v * (1 - s * (1 - f));

        // build our rgb color
        Color color = new(0, 0, 0, a);

        switch (i)
        {
            case 0:
                color.r = v;
                color.g = t;
                color.b = p;
                break;

            case 1:
                color.r = q;
                color.g = v;
                color.b = p;
                break;

            case 2:
                color.r = p;
                color.g = v;
                color.b = t;
                break;

            case 3:
                color.r = p;
                color.g = q;
                color.b = v;
                break;

            case 4:
                color.r = t;
                color.g = p;
                color.b = v;
                break;

            default:
                color.r = v;
                color.g = p;
                color.b = q;
                break;
        }

        return color;
    }

    public static Color operator +(Color a, Color b) => new(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);

    public static Color operator /(Color a, float b) => new(a.r / b, a.g / b, a.b / b, a.a / b);

    public static bool operator ==(Color lhs, Color rhs) => lhs.Equals(rhs);

    public static implicit operator Vector4(Color c) => new(c.r, c.g, c.b, c.a);
    public static implicit operator System.Numerics.Vector4(Color c) => new(c.r, c.g, c.b, c.a);

    public static implicit operator Color(Vector4 v) => new((float)v.x, (float)v.y, (float)v.z, (float)v.w);
    public static implicit operator Color(System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    public static bool operator !=(Color lhs, Color rhs) => !lhs.Equals(rhs);

    public static Color operator *(Color a, Color b) => new(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);

    public static Color operator *(Color a, float b) => new(a.r * b, a.g * b, a.b * b, a.a * b);

    public static Color operator *(float b, Color a) => new(a.r * b, a.g * b, a.b * b, a.a * b);

    public static Color operator -(Color a, Color b) => new(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);

    public override bool Equals(object? other)
    {
        if (other is not Color c) return false;
        return r.Equals(c.r) && g.Equals(c.g) && b.Equals(c.b) && a.Equals(c.a);
    }

    public override string ToString() => string.Format("RGBA({0}, {1}, {2}, {3})", new object[] { r, g, b, a });

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }

    public bool Equals(Color other) => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);
}
