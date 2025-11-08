// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.PaperUI.Utilities
{
    public struct HSV
    {
        public float H; // [0, 360)
        public float S; // [0, 1]
        public float V; // [0, 1]
        public float A; // [0, 1]

        public HSV(float h, float s, float v, float a = 1.0f)
        {
            H = h; S = s; V = v; A = a;
        }

        public static HSV FromColor(Color32 c)
        {
            float r = c.R / 255.0f;
            float g = c.G / 255.0f;
            float b = c.B / 255.0f;
            float max = Maths.Max(r, Maths.Max(g, b));
            float min = Maths.Min(r, Maths.Min(g, b));
            float delta = max - min;

            float h = 0;
            if (delta > 0)
            {
                if (max == r) h = 60 * (((g - b) / delta) % 6);
                else if (max == g) h = 60 * (((b - r) / delta) + 2);
                else h = 60 * (((r - g) / delta) + 4);
            }
            if (h < 0) h += 360;

            float s = max == 0 ? 0 : delta / max;
            return new HSV(h, s, max, c.A / 255.0f);
        }

        public Color32 ToColor()
        {
            float c = V * S;
            float x = c * (1 - Maths.Abs((H / 60) % 2 - 1));
            float m = V - c;

            float r = 0, g = 0, b = 0;
            if (H < 60) { r = c; g = x; }
            else if (H < 120) { r = x; g = c; }
            else if (H < 180) { g = c; b = x; }
            else if (H < 240) { g = x; b = c; }
            else if (H < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color32.FromArgb(
                (int)(A * 255),
                (int)((r + m) * 255),
                (int)((g + m) * 255),
                (int)((b + m) * 255)
            );
        }

        public static HSV Lerp(HSV a, HSV b, float t)
        {
            t = Maths.Clamp(t, 0.0f, 1.0f);

            // Shortest path hue interpolation
            float dh = b.H - a.H;
            if (Maths.Abs(dh) > 180)
                dh -= Maths.Sign(dh) * 360;

            return new HSV(
                (a.H + t * dh + 360) % 360,
                a.S + (b.S - a.S) * t,
                a.V + (b.V - a.V) * t,
                a.A + (b.A - a.A) * t
            );
        }
    }
}
