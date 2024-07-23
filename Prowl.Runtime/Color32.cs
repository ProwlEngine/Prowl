using System;
using System.Runtime.InteropServices;

namespace Prowl.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Color32
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color32(uint rgba)
        {
            r = (byte)(rgba & 0xFF);
            g = (byte)((rgba >> 8) & 0xFF);
            b = (byte)((rgba >> 16) & 0xFF);
            a = (byte)((rgba >> 24) & 0xFF);
        }

        internal uint GetUInt()
        {
            uint @out;
            @out = (uint)r;
            @out |= (uint)g << 8;
            @out |= (uint)b << 16;
            @out |= (uint)a << 24;
            return @out;
        }

        public static implicit operator Color32(Color c)
        {
            return new Color32((byte)(MathF.Min(MathF.Max(c.r, 0f), 1f) * 255f), (byte)(MathF.Min(MathF.Max(c.g, 0f), 1f) * 255f), (byte)(MathF.Min(MathF.Max(c.b, 0f), 1f) * 255f), (byte)(MathF.Min(MathF.Max(c.a, 0f), 1f) * 255f));
        }

        public static implicit operator Color(Color32 v)
        {
            return new Color((float)v.r / 255f, (float)v.g / 255f, (float)v.b / 255f, (float)v.a / 255f);
        }

        public override readonly string ToString() => string.Format("RGBA({0}, {1}, {2}, {3})", new object[] { r, g, b, a });
    }
}
