// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Drawing;

namespace Prowl.Paper.Utilities;

public struct HSV
{
    public double H; // [0, 360)
    public double S; // [0, 1]
    public double V; // [0, 1]
    public double A; // [0, 1]

    public HSV(double h, double s, double v, double a = 1.0)
    {
        H = h; S = s; V = v; A = a;
    }

    public static HSV FromColor(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max == 0 ? 0 : delta / max;
        return new HSV(h, s, max, c.A / 255.0);
    }

    public Color ToColor()
    {
        double c = V * S;
        double x = c * (1 - Math.Abs((H / 60) % 2 - 1));
        double m = V - c;

        double r = 0, g = 0, b = 0;
        if (H < 60) { r = c; g = x; }
        else if (H < 120) { r = x; g = c; }
        else if (H < 180) { g = c; b = x; }
        else if (H < 240) { g = x; b = c; }
        else if (H < 300) { r = x; b = c; }
        else { r = c; b = x; }

        return Color.FromArgb(
            (int)(A * 255),
            (int)((r + m) * 255),
            (int)((g + m) * 255),
            (int)((b + m) * 255)
        );
    }

    public static HSV Lerp(HSV a, HSV b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);

        // Shortest path hue interpolation
        double dh = b.H - a.H;
        if (Math.Abs(dh) > 180)
            dh -= Math.Sign(dh) * 360;

        return new HSV(
            (a.H + t * dh + 360) % 360,
            a.S + (b.S - a.S) * t,
            a.V + (b.V - a.V) * t,
            a.A + (b.A - a.A) * t
        );
    }
}
