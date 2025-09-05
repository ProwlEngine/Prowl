using System.Drawing;

namespace Prowl.PaperUI.Themes.Origami;

/// <summary>
/// Theme configuration for Origami components.
/// Provides consistent styling across all components with customizable color schemes and layout properties.
/// </summary>
public class OrigamiTheme
{
    public struct FontSizeSet
    {
        public double Tiny = 12;
        public double Small = 14;
        public double Medium = 16;
        public double Large = 18;

        public FontSizeSet() { }
    }

    public struct RadiusSet
    {
        public double Small = 8;
        public double Medium = 11;
        public double Large = 14;

        public RadiusSet() { }
    }

    public struct BorderWidthSet
    {
        public double Small = 1.5f;
        public double Medium = 2.5f;
        public double Large = 4;

        public BorderWidthSet() { }
    }

    public struct ThemeColor
    {
        public Color Base;
        public Color Base50, Base100, Base200, Base300, Base400, Base500, Base600, Base700, Base800, Base900;

        public ThemeColor(System.Drawing.Color color)
        {
            Base = color;
            Base50 = CreateShade(0);
            Base100 = CreateShade(1);
            Base200 = CreateShade(2);
            Base300 = CreateShade(3);
            Base400 = CreateShade(4);
            Base500 = CreateShade(5);
            Base600 = CreateShade(6);
            Base700 = CreateShade(7);
            Base800 = CreateShade(8);
            Base900 = CreateShade(9);
        }

        private Color CreateShade(double index)
        {
            (double h, double s, double l) = RgbToHsl(Base);

            // Map shade index into a lightness factor [0..1]
            double factor = index / 9.0;

            // 50 should be very light, 900 should be very dark
            double newL = 0.95 - factor * 0.75; // range from ~95% down to ~20%

            // Adjust saturation slightly toward midtones
            double newS = Math.Clamp(s * (0.8 + 0.4 * (1 - Math.Abs(0.5 - newL) * 2)), 0, 1);

            return HslToRgb(h, newS, newL);
        }

        private (double H, double S, double L) RgbToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h, s, l;
            h = s = l = (max + min) / 2.0;

            if (max == min)
            {
                h = s = 0; // gray
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;

                h /= 6.0;
            }

            return (h, s, l);
        }

        private Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l; // gray
            }
            else
            {
                double HueToRgb(double p, double q, double t)
                {
                    if (t < 0) t += 1;
                    if (t > 1) t -= 1;
                    if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                    if (t < 1.0 / 2) return q;
                    if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                    return p;
                }

                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3);
            }

            return Color.FromArgb(
                255,
                (int)Math.Round(r * 255),
                (int)Math.Round(g * 255),
                (int)Math.Round(b * 255));
        }
    }

    public FontSizeSet FontSize = new();
    public RadiusSet Radius = new();
    public BorderWidthSet BorderWidth = new();

    public double DisableOpacity = 0.5;
    public double HoverOpacity = 0.8;

    public ThemeColor Primary = new ThemeColor(Color.FromArgb(0x00, 0x6F, 0xEE)); // #006FEE (Primary-400)
    public ThemeColor Secondary = new ThemeColor(Color.FromArgb(0x78, 0x28, 0xC8)); // #7828C8 (Secondary-400)
    public ThemeColor Success = new ThemeColor(Color.FromArgb(0x17, 0xC9, 0x64)); // #17C964 (Success-400)
    public ThemeColor Warning = new ThemeColor(Color.FromArgb(0xF5, 0xA5, 0x24)); // #F5A524 (Warning-400)
    public ThemeColor Danger = new ThemeColor(Color.FromArgb(0xF3, 0x12, 0x60)); // #F31260 (Danger-400)

    public ThemeColor Background = new ThemeColor(Color.FromArgb(0x00, 0x00, 0x00)); // #000000
    public ThemeColor Foreground = new ThemeColor(Color.FromArgb(0xEC, 0xED, 0xEE)); // #ECEDEE

    public ThemeColor Content1 = new ThemeColor(Color.FromArgb(0x18, 0x18, 0x1B)); // #18181B
    public ThemeColor Content2 = new ThemeColor(Color.FromArgb(0x27, 0x27, 0x2A)); // #27272A
    public ThemeColor Content3 = new ThemeColor(Color.FromArgb(0x3F, 0x3F, 0x46)); // #3F3F46
    public ThemeColor Content4 = new ThemeColor(Color.FromArgb(0x52, 0x52, 0x5B)); // #52525B

    public ThemeColor Focus = new ThemeColor(Color.FromArgb(0x00, 0x6F, 0xEE)); // same as Primary-400
    public ThemeColor Overlay = new ThemeColor(Color.FromArgb(0, 0, 0, 128)); // semi-transparent black
    public ThemeColor Divider = new ThemeColor(Color.FromArgb(38, 255, 255, 255));

    public ThemeColor GetColor(OrigamiColor color)
    {
        return color switch
        {
            OrigamiColor.Primary => Primary,
            OrigamiColor.Secondary => Secondary,
            OrigamiColor.Success => Success,
            OrigamiColor.Warning => Warning,
            OrigamiColor.Danger => Danger,
            _ => Foreground,
        };
    }

    public double GetFontSize(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => FontSize.Small,
            OrigamiSize.Medium => FontSize.Medium,
            OrigamiSize.Large => FontSize.Large,
            _ => throw new InvalidOperationException()
        };
    }

    public double GetRadius(OrigamiRadius radius)
    {
        return radius switch
        {
            OrigamiRadius.None => 0,
            OrigamiRadius.Small => Radius.Small,
            OrigamiRadius.Medium => Radius.Medium,
            OrigamiRadius.Large => Radius.Large,
            OrigamiRadius.Full => 99999,
            _ => throw new InvalidOperationException()
        };
    }

    public double GetBorderWidth(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => BorderWidth.Small,
            OrigamiSize.Medium => BorderWidth.Medium,
            OrigamiSize.Large => BorderWidth.Large,
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>
    /// Creates a copy of this theme.
    /// </summary>
    /// <returns>A new theme instance</returns>
    public OrigamiTheme Clone()
    {
        return new OrigamiTheme
        {
            FontSize = this.FontSize,
            Radius = this.Radius,
            BorderWidth = this.BorderWidth,

            Primary = this.Primary,
            Secondary = this.Secondary,
            Success = this.Success,
            Warning = this.Warning,
            Danger = this.Danger,

            Background = this.Background,
            Foreground = this.Foreground,

            Content1 = this.Content1,
            Content2 = this.Content2,
            Content3 = this.Content3,
            Content4 = this.Content4,

            Focus = this.Focus,
            Overlay = this.Overlay,
            Divider = this.Divider
        };
    }
}
