using System;
using System.Numerics;

namespace Prowl.Runtime
{
    public struct Color
    {
        public float r, g, b, a;

        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;

        public static Color black => new Color(0f, 0f, 0f, 1f);

        public static Color blue => new Color(0f, 0f, 1f, 1f);

        public static Color clear => new Color(0f, 0f, 0f, 0f);

        public static Color cyan => new Color(0f, 1f, 1f, 1f);

        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);

        public static Color green => new Color(0f, 1f, 0f, 1f);

        public static Color grey => new Color(0.5f, 0.5f, 0.5f, 1f);

        public static Color magenta => new Color(1f, 0f, 1f, 1f);

        public static Color red => new Color(1f, 0f, 0f, 1f);

        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static Color yellow => new Color(1f, 0.9215f, 0.0156f, 1f);

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
                    _ => throw new IndexOutOfRangeException("Invalid Color index!")
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
                    _ => throw new IndexOutOfRangeException("Invalid Color index!")
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
            this.a = 1f;
        }

        public Color(byte r, byte g, byte b)
        {
            this.r = r / 255f;
            this.g = g / 255f;
            this.b = b / 255f;
            this.a = 1f;
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = MathF.Min(MathF.Max(t, 0f), 1f);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);

        public static Color operator /(Color a, float b) => new Color(a.r / b, a.g / b, a.b / b, a.a / b);

        public static bool operator ==(Color lhs, Color rhs) => lhs == rhs;

        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static implicit operator System.Numerics.Vector4(Color c) => new System.Numerics.Vector4(c.r, c.g, c.b, c.a);

        public static implicit operator Color(Vector4 v) => new Color((float)v.X, (float)v.Y, (float)v.Z, (float)v.W);
        public static implicit operator Color(System.Numerics.Vector4 v) => new Color(v.X, v.Y, v.Z, v.W);

        public static bool operator !=(Color lhs, Color rhs) => lhs != rhs;

        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);

        public static Color operator *(Color a, float b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);

        public static Color operator *(float b, Color a) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);

        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);

        public override bool Equals(object other)
        {
            if (other is not Color c) return false;
            return r.Equals(c.r) && g.Equals(c.g) && b.Equals(c.b) && a.Equals(c.a);
        }

        public override string ToString() => string.Format("RGBA({0}, {1}, {2}, {3})", new object[] { r, g, b, a });
    }
}
