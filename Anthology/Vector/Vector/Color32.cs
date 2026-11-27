// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace Prowl.Vector
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Color32
    {
        public byte R, G, B, A;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }

        public Color32(uint rgba)
        {
            R = (byte)(rgba & 0xFF);
            G = (byte)((rgba >> 8) & 0xFF);
            B = (byte)((rgba >> 16) & 0xFF);
            A = (byte)((rgba >> 24) & 0xFF);
        }

        internal uint GetUInt()
        {
            uint @out;
            @out = R;
            @out |= (uint)G << 8;
            @out |= (uint)B << 16;
            @out |= (uint)A << 24;
            return @out;
        }

        public static Color FromArgb(int alpha, int red, int green, int blue) => new Color(red / 255f, green / 255f, blue / 255f, alpha / 255f);

        public static Color FromArgb(int alpha, Color baseColor) => new Color(baseColor.R, baseColor.G, baseColor.B, alpha / 255f);

        public static Color FromArgb(int red, int green, int blue) => FromArgb(byte.MaxValue, red, green, blue);
        
        public static implicit operator Color32(Color c) => new Color32((byte)Maths.Clamp(c.R * 255.0, 0, 255), (byte)Maths.Clamp(c.G * 255.0, 0, 255), (byte)Maths.Clamp(c.B * 255.0, 0, 255), (byte)Maths.Clamp(c.A * 255.0, 0, 255));
        public static implicit operator System.Drawing.Color(Color32 c) => System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

        public static implicit operator Color(Color32 v) => new Color(v.R / 255f, v.G / 255f, v.B / 255f, v.A / 255f);
        public static implicit operator Color32(System.Drawing.Color c) => new Color32(c.R, c.G, c.B, c.A);


        #region Equals and HashCode

        public static bool operator ==(Color32 lhs, Color32 rhs) => lhs.Equals(rhs);

        public static bool operator !=(Color32 lhs, Color32 rhs) => !lhs.Equals(rhs);

        public bool Equals(Color32 other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

        public override bool Equals(object? other)
        {
            if (!(other is Color32 c)) return false;
            return R.Equals(c.R) && G.Equals(c.G) && B.Equals(c.B) && A.Equals(c.A);
        }

        public override int GetHashCode() => HashCode.Combine(R, G, B, A);

        #endregion

        public override readonly string ToString() => string.Format("RGBA({0}, {1}, {2}, {3})", new object[] { R, G, B, A });
    }
}
