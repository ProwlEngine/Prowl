using System;

namespace Prowl.Runtime
{
    public struct Color32
    {
        public byte red;

        public byte green;

        public byte blue;

        public byte alpha;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.red = r;
            this.green = g;
            this.blue = b;
            this.alpha = a;
        }

        public static implicit operator Raylib_cs.Color(Color32 c) => new Raylib_cs.Color(c.red, c.green, c.blue, c.alpha);
        public static implicit operator Color32(Raylib_cs.Color c) => new Color32(c.r, c.g, c.b, c.a);

        public static implicit operator Color32(Color c)
        {
            return new Color32((byte)(MathF.Min(MathF.Max(c.r, 0f), 1f) * 255f), (byte)(MathF.Min(MathF.Max(c.g, 0f), 1f) * 255f), (byte)(MathF.Min(MathF.Max(c.b, 0f), 1f) * 255f), (byte)(MathF.Min(MathF.Max(c.a, 0f), 1f) * 255f));
        }

        public static implicit operator Color(Color32 v)
        {
            return new Color((float)v.red / 255f, (float)v.green / 255f, (float)v.blue / 255f, (float)v.alpha / 255f);
        }
    }
}
