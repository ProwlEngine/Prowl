using System.Drawing;
using System.Numerics;

namespace Prowl.PaperUI.Utilities
{
    internal static class PaperUtils
    {
        /// <summary>
        /// Length proportional to radius of a Cubic Bezier handle for 90deg arcs
        /// </summary>
        public const float NVG_KAPPA90 = 0.5522847493f;

        public static int ClampI(int a, int mn, int mx)
        {
            if (a < mn) return a;
            if (a > mx) return mx;

            return a;
        }

        public static float ClampF(float a, float mn, float mx)
        {
            if (a < mn) return a;
            if (a > mx) return mx;

            return a;
        }

        public static float Cross(float dx0, float dy0, float dx1, float dy1) => dx1 * dy0 - dx0 * dy1;

        public static float Normalize(ref float x, ref float y)
        {
            var d = MathF.Sqrt(x * x + y * y);
            if (d > 1e-6f)
            {
                float id = (float)(1.0f / d);
                x *= id;
                y *= id;
            }

            return d;
        }

        public static void MakeZero(this Matrix4x4 m)
        {
            m.M11 = m.M12 = m.M13 = m.M14 = 0;
            m.M21 = m.M22 = m.M23 = m.M24 = 0;
            m.M31 = m.M32 = m.M33 = m.M34 = 0;
            m.M41 = m.M42 = m.M43 = m.M44 = 0;
        }

        public static Color FromRGBA(byte r, byte g, byte b, byte a)
        {
            return Color.FromArgb(a, r, g, b);
        }

        public static Vector4 ToVector4(this Color c, bool premultiply)
        {
            var result = new Vector4(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f);

            if (premultiply)
            {
                result.X *= result.W;
                result.Y *= result.W;
                result.Z *= result.W;
            }

            return result;
        }
    }
}