// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace Prowl.Vector
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Color : IEquatable<Color>
    {
        public double r, g, b, a;

        public double grayscale => 0.299 * r + 0.587 * g + 0.114 * b;

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

        public double this[int index]
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

        public Color(double r, double g, double b, double a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(double r, double g, double b)
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

        #region Static Functions

        public static Color Lerp(Color a, Color b, double t)
        {
            t = Maths.Min(Maths.Max(t, 0), 1);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static bool IsGrayscale(Color color)
        {
            return color.r == color.g && color.g == color.b;
        }

        /// <summary>Performs color-space aware linear interpolation between two RGB colors.</summary>
        public static Color LerpColorSpace(Color from, Color to, double t)
        {
            // Convert to HSV for more natural color interpolation
            var hsvFrom = RGBToHSV(from);
            var hsvTo = RGBToHSV(to);

            // Handle hue wrapping for shortest path interpolation
            double deltaHue = hsvTo.X - hsvFrom.X;
            if (deltaHue > 180f)
                hsvFrom.X += 360f;
            else if (deltaHue < -180f)
                hsvTo.X += 360f;

            // Interpolate in HSV space
            var hsvResult = new Double4(
                hsvFrom.X + (hsvTo.X - hsvFrom.X) * t,
                hsvFrom.Y + (hsvTo.Y - hsvFrom.Y) * t,
                hsvFrom.Z + (hsvTo.Z - hsvFrom.Z) * t,
                hsvFrom.W + (hsvTo.W - hsvFrom.W) * t
            );

            // Wrap hue back to 0-360 range
            if (hsvResult.X < 0f) hsvResult.X += 360f;
            if (hsvResult.X >= 360f) hsvResult.X -= 360f;

            // Convert back to RGB
            return HSVToRGB(hsvResult);
        }

        /// <summary>Adjusts the brightness of a color.</summary>
        public static Color Brightness(Color color, double brightness)
        {
            return new Color(
                Maths.Max(0f, color.r * brightness),
                Maths.Max(0f, color.g * brightness),
                Maths.Max(0f, color.b * brightness), color.a
            );
        }

        /// <summary>Adjusts the contrast of a color.</summary>
        public static Color Contrast(Color color, double contrast)
        {
            const double midpoint = 0.5f;
            return new Color(
                Maths.Clamp((color.r - midpoint) * contrast + midpoint, 0f, 1f),
                Maths.Clamp((color.g - midpoint) * contrast + midpoint, 0f, 1f),
                Maths.Clamp((color.b - midpoint) * contrast + midpoint, 0f, 1f), color.a
            );
        }


        /// <summary>Desaturates a color by blending it towards grayscale.</summary>
        public static Color Desaturate(Color color, double amount = 1f)
        {
            // Standard luminance weights for RGB
            double luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            var gray = new Color(luminance, luminance, luminance, color.a);
            amount = Maths.Clamp(amount, 0f, 1f);
            return color + (gray - color) * amount;
        }

        /// <summary>Applies gamma correction to a color.</summary>
        public static Color Gamma(Color color, double gamma = 2.2f)
        {
            return new Color(
                Maths.Pow(Maths.Max(0f, color.r), 1f / gamma),
                Maths.Pow(Maths.Max(0f, color.g), 1f / gamma),
                Maths.Pow(Maths.Max(0f, color.b), 1f / gamma), color.a
            );
        }

        /// <summary>Converts a gamma space color to linear space.</summary>
        public static Color GammaToLinear(Color gamma)
        {
            return new Color(
                Maths.Pow(Maths.Max(0f, gamma.r), 2.2f),
                Maths.Pow(Maths.Max(0f, gamma.g), 2.2f),
                Maths.Pow(Maths.Max(0f, gamma.b), 2.2f), gamma.a
            );
        }

        /// <summary>Converts a linear color to gamma space (sRGB).</summary>
        public static Color LinearToGamma(Color linear)
        {
            return Gamma(linear, 2.2f);
        }

        /// <summary>Converts HSL to RGB color space.</summary>
        public static Color HSLToRGB(Color hsla)
        {
            double h = hsla.r, s = hsla.g, l = hsla.b;

            if (s <= 0f)
            {
                double gray = l;
                return new Color(gray, gray, gray, hsla.a);
            }

            double c = (1f - Maths.Abs(2f * l - 1f)) * s;
            double x = c * (1f - Maths.Abs((h / 60f) % 2f - 1f));
            double m = l - c / 2f;

            double r = 0f, g = 0f, b = 0f;

            if (h >= 0f && h < 60f)
                (r, g, b) = (c, x, 0f);
            else if (h >= 60f && h < 120f)
                (r, g, b) = (x, c, 0f);
            else if (h >= 120f && h < 180f)
                (r, g, b) = (0f, c, x);
            else if (h >= 180f && h < 240f)
                (r, g, b) = (0f, x, c);
            else if (h >= 240f && h < 300f)
                (r, g, b) = (x, 0f, c);
            else if (h >= 300f && h < 360f)
                (r, g, b) = (c, 0f, x);

            r += m; g += m; b += m;

            return new Color(r, g, b, hsla.a);
        }

        /// <summary>Converts RGB to HSL color space.</summary>
        public static Double4 RGBToHSL(Color rgba)
        {
            double r = rgba.r, g = rgba.g, b = rgba.b;
            double max = Maths.Max(r, Maths.Max(g, b));
            double min = Maths.Min(r, Maths.Min(g, b));
            double delta = max - min;

            double h = 0f, s = 0f, l = (max + min) / 2f;

            if (delta > 0f)
            {
                s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

                if (max == r)
                    h = 60f * (((g - b) / delta) % 6f);
                else if (max == g)
                    h = 60f * ((b - r) / delta + 2f);
                else if (max == b)
                    h = 60f * ((r - g) / delta + 4f);

                if (h < 0f) h += 360f;
            }

            return new Double4(h, s, l, rgba.a);
        }

        /// <summary>Converts HSV to RGB color space.</summary>
        public static Color HSVToRGB(Color hsva)
        {
            double h = hsva.r, s = hsva.g, v = hsva.b;

            if (s <= 0f)
            {
                double gray = v;
                return new Color(gray, gray, gray, hsva.a);
            }

            double c = v * s;
            double x = c * (1f - Maths.Abs((h / 60f) % 2f - 1f));
            double m = v - c;

            double r = 0f, g = 0f, b = 0f;

            if (h >= 0f && h < 60f)
                (r, g, b) = (c, x, 0f);
            else if (h >= 60f && h < 120f)
                (r, g, b) = (x, c, 0f);
            else if (h >= 120f && h < 180f)
                (r, g, b) = (0f, c, x);
            else if (h >= 180f && h < 240f)
                (r, g, b) = (0f, x, c);
            else if (h >= 240f && h < 300f)
                (r, g, b) = (x, 0f, c);
            else if (h >= 300f && h < 360f)
                (r, g, b) = (c, 0f, x);

            r += m; g += m; b += m;

            return new Color(r, g, b, hsva.a);
        }

        /// <summary>Converts RGB to HSV color space.</summary>
        public static Double4 RGBToHSV(Color rgba)
        {
            double r = rgba.r, g = rgba.g, b = rgba.b;
            double max = Maths.Max(r, Maths.Max(g, b));
            double min = Maths.Min(r, Maths.Min(g, b));
            double delta = max - min;

            double h = 0f, s = 0f, v = max;

            if (delta > 0f)
            {
                s = delta / max;

                if (max == r)
                    h = 60f * (((g - b) / delta) % 6f);
                else if (max == g)
                    h = 60f * ((b - r) / delta + 2f);
                else if (max == b)
                    h = 60f * ((r - g) / delta + 4f);

                if (h < 0f) h += 360f;
            }

            return new Double4(h, s, v, rgba.a);
        }

        #endregion

        public static implicit operator Double4(Color c) => new Color(c.r, c.g, c.b, c.a);
        public static implicit operator System.Numerics.Vector4(Color c) => new Color(c.r, c.g, c.b, c.a);

        public static implicit operator Color(Double4 v) => new Color(v.X, v.Y, v.Z, v.W);
        public static implicit operator Color(System.Numerics.Vector4 v) => new Color(v.X, v.Y, v.Z, v.W);

        public static Color operator +(Color a, double b) => new Color(a.r + b, a.g + b, a.b + b, a.a + b);
        public static Color operator -(Color a, double b) => new Color(a.r - b, a.g - b, a.b - b, a.a - b);
        public static Color operator *(Color a, double b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator /(Color a, double b) => new Color(a.r / b, a.g / b, a.b / b, a.a / b);

        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator /(Color a, Color b) => new Color(a.r / b.r, a.g / b.g, a.b / b.b, a.a / b.a);

        #region Equals and HashCode

        public static bool operator ==(Color lhs, Color rhs) => lhs.Equals(rhs);

        public static bool operator !=(Color lhs, Color rhs) => !lhs.Equals(rhs);

        public bool Equals(Color other) => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);

        public override bool Equals(object? other)
        {
            if (!(other is Color c)) return false;
            return r.Equals(c.r) && g.Equals(c.g) && b.Equals(c.b) && a.Equals(c.a);
        }

        public override int GetHashCode() => HashCode.Combine(r, g, b, a);

        #endregion

        public override string ToString() => string.Format("RGBA({0}, {1}, {2}, {3})", new object[] { r, g, b, a });
    }
}
