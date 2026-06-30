using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FontColor
{
    public byte R, G, B, A;

    public FontColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    public FontColor(int r, int g, int b, int a = 255)
    {
        R = (byte)Math.Clamp(r, 0, 255);
        G = (byte)Math.Clamp(g, 0, 255);
        B = (byte)Math.Clamp(b, 0, 255);
        A = (byte)Math.Clamp(a, 0, 255);
    }

    public FontColor(float r, float g, float b, float a = 1.0f)
    {
        R = (byte)Math.Clamp(r * 255, 0, 255);
        G = (byte)Math.Clamp(g * 255, 0, 255);
        B = (byte)Math.Clamp(b * 255, 0, 255);
        A = (byte)Math.Clamp(a * 255, 0, 255);
    }

    /// <summary>
    /// Create color from HSV values (Hue: 0-360, Saturation: 0-1, Value: 0-1)
    /// </summary>
    public static FontColor FromHSV(float hue, float saturation, float value, float alpha = 1.0f)
    {
        hue = hue % 360;
        if (hue < 0) hue += 360;

        float c = value * saturation;
        float x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        float m = value - c;

        float r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new FontColor((r + m), (g + m), (b + m), alpha);
    }

    /// <summary>
    /// Create color from hex string (#RGB, #RGBA, #RRGGBB, #RRGGBBAA)
    /// </summary>
    public static FontColor FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#')
            throw new ArgumentException("Invalid hex color format");

        hex = hex.Substring(1);

        return hex.Length switch {
            3 => new FontColor( // #RGB
                Convert.ToByte(hex.Substring(0, 1) + hex.Substring(0, 1), 16),
                Convert.ToByte(hex.Substring(1, 1) + hex.Substring(1, 1), 16),
                Convert.ToByte(hex.Substring(2, 1) + hex.Substring(2, 1), 16)),
            4 => new FontColor( // #RGBA
                Convert.ToByte(hex.Substring(0, 1) + hex.Substring(0, 1), 16),
                Convert.ToByte(hex.Substring(1, 1) + hex.Substring(1, 1), 16),
                Convert.ToByte(hex.Substring(2, 1) + hex.Substring(2, 1), 16),
                Convert.ToByte(hex.Substring(3, 1) + hex.Substring(3, 1), 16)),
            6 => new FontColor( // #RRGGBB
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16)),
            8 => new FontColor( // #RRGGBBAA
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16)),
            _ => throw new ArgumentException("Invalid hex color format")
        };
    }

    public static implicit operator FontColor(System.Drawing.Color color) =>
        new FontColor(color.R, color.G, color.B, color.A);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    // These are in fact AI Generated, So lets hope AI got it right :shrug:

    // Basic Colors
    public static FontColor Transparent => new FontColor(0, 0, 0, 0);
    public static FontColor White => new FontColor(255, 255, 255);
    public static FontColor Black => new FontColor(0, 0, 0);
    public static FontColor Gray => new FontColor(128, 128, 128);
    public static FontColor Silver => new FontColor(192, 192, 192);
    public static FontColor LightGray => new FontColor(211, 211, 211);
    public static FontColor DarkGray => new FontColor(64, 64, 64);

    // Primary Colors
    public static FontColor Red => new FontColor(255, 0, 0);
    public static FontColor Green => new FontColor(0, 255, 0);
    public static FontColor Blue => new FontColor(0, 0, 255);
    public static FontColor Yellow => new FontColor(255, 255, 0);
    public static FontColor Magenta => new FontColor(255, 0, 255);
    public static FontColor Cyan => new FontColor(0, 255, 255);

    // Popular Web Colors
    public static FontColor Orange => new FontColor(255, 165, 0);
    public static FontColor Purple => new FontColor(128, 0, 128);
    public static FontColor Pink => new FontColor(255, 192, 203);
    public static FontColor Brown => new FontColor(139, 69, 19);
    public static FontColor Lime => new FontColor(0, 255, 0);
    public static FontColor Olive => new FontColor(128, 128, 0);
    public static FontColor Navy => new FontColor(0, 0, 128);
    public static FontColor Teal => new FontColor(0, 128, 128);
    public static FontColor Maroon => new FontColor(128, 0, 0);
    public static FontColor Aqua => new FontColor(0, 255, 255);
    public static FontColor Fuchsia => new FontColor(255, 0, 255);

    // Shades of Red
    public static FontColor DarkRed => new FontColor(139, 0, 0);
    public static FontColor LightRed => new FontColor(255, 204, 204);
    public static FontColor Crimson => new FontColor(220, 20, 60);
    public static FontColor IndianRed => new FontColor(205, 92, 92);
    public static FontColor FireBrick => new FontColor(178, 34, 34);
    public static FontColor Salmon => new FontColor(250, 128, 114);
    public static FontColor LightSalmon => new FontColor(255, 160, 122);
    public static FontColor DarkSalmon => new FontColor(233, 150, 122);
    public static FontColor Coral => new FontColor(255, 127, 80);
    public static FontColor Tomato => new FontColor(255, 99, 71);

    // Shades of Orange
    public static FontColor DarkOrange => new FontColor(255, 140, 0);
    public static FontColor OrangeRed => new FontColor(255, 69, 0);
    public static FontColor Gold => new FontColor(255, 215, 0);
    public static FontColor DarkGoldenrod => new FontColor(184, 134, 11);
    public static FontColor Goldenrod => new FontColor(218, 165, 32);
    public static FontColor PaleGoldenrod => new FontColor(238, 232, 170);
    public static FontColor Khaki => new FontColor(240, 230, 140);
    public static FontColor DarkKhaki => new FontColor(189, 183, 107);

    // Shades of Yellow
    public static FontColor LightYellow => new FontColor(255, 255, 224);
    public static FontColor LemonChiffon => new FontColor(255, 250, 205);
    public static FontColor LightGoldenrodYellow => new FontColor(250, 250, 210);
    public static FontColor PapayaWhip => new FontColor(255, 239, 213);
    public static FontColor Moccasin => new FontColor(255, 228, 181);
    public static FontColor PeachPuff => new FontColor(255, 218, 185);
    public static FontColor BurlyWood => new FontColor(222, 184, 135);

    // Shades of Green
    public static FontColor DarkGreen => new FontColor(0, 100, 0);
    public static FontColor LightGreen => new FontColor(144, 238, 144);
    public static FontColor ForestGreen => new FontColor(34, 139, 34);
    public static FontColor LimeGreen => new FontColor(50, 205, 50);
    public static FontColor PaleGreen => new FontColor(152, 251, 152);
    public static FontColor DarkSeaGreen => new FontColor(143, 188, 143);
    public static FontColor MediumSeaGreen => new FontColor(60, 179, 113);
    public static FontColor SeaGreen => new FontColor(46, 139, 87);
    public static FontColor DarkOliveGreen => new FontColor(85, 107, 47);
    public static FontColor OliveDrab => new FontColor(107, 142, 35);
    public static FontColor YellowGreen => new FontColor(154, 205, 50);
    public static FontColor LawnGreen => new FontColor(124, 252, 0);
    public static FontColor Chartreuse => new FontColor(127, 255, 0);
    public static FontColor GreenYellow => new FontColor(173, 255, 47);
    public static FontColor SpringGreen => new FontColor(0, 255, 127);
    public static FontColor MediumSpringGreen => new FontColor(0, 250, 154);

    // Shades of Blue
    public static FontColor DarkBlue => new FontColor(0, 0, 139);
    public static FontColor LightBlue => new FontColor(173, 216, 230);
    public static FontColor SkyBlue => new FontColor(135, 206, 235);
    public static FontColor LightSkyBlue => new FontColor(135, 206, 250);
    public static FontColor DeepSkyBlue => new FontColor(0, 191, 255);
    public static FontColor DodgerBlue => new FontColor(30, 144, 255);
    public static FontColor CornflowerBlue => new FontColor(100, 149, 237);
    public static FontColor SteelBlue => new FontColor(70, 130, 180);
    public static FontColor LightSteelBlue => new FontColor(176, 196, 222);
    public static FontColor LightSlateGray => new FontColor(119, 136, 153);
    public static FontColor SlateGray => new FontColor(112, 128, 144);
    public static FontColor DarkSlateGray => new FontColor(47, 79, 79);
    public static FontColor MidnightBlue => new FontColor(25, 25, 112);
    public static FontColor DarkSlateBlue => new FontColor(72, 61, 139);
    public static FontColor SlateBlue => new FontColor(106, 90, 205);
    public static FontColor MediumSlateBlue => new FontColor(123, 104, 238);
    public static FontColor RoyalBlue => new FontColor(65, 105, 225);
    public static FontColor BlueViolet => new FontColor(138, 43, 226);
    public static FontColor Indigo => new FontColor(75, 0, 130);
    public static FontColor DarkOrchid => new FontColor(153, 50, 204);
    public static FontColor DarkViolet => new FontColor(148, 0, 211);
    public static FontColor MediumOrchid => new FontColor(186, 85, 211);
    public static FontColor Thistle => new FontColor(216, 191, 216);
    public static FontColor Plum => new FontColor(221, 160, 221);
    public static FontColor Violet => new FontColor(238, 130, 238);
    public static FontColor Orchid => new FontColor(218, 112, 214);
    public static FontColor MediumVioletRed => new FontColor(199, 21, 133);
    public static FontColor PaleVioletRed => new FontColor(219, 112, 147);
    public static FontColor DeepPink => new FontColor(255, 20, 147);
    public static FontColor HotPink => new FontColor(255, 105, 180);
    public static FontColor LightPink => new FontColor(255, 182, 193);

    // Shades of Purple/Violet
    public static FontColor DarkMagenta => new FontColor(139, 0, 139);
    public static FontColor MediumPurple => new FontColor(147, 112, 219);
    public static FontColor Lavender => new FontColor(230, 230, 250);
    public static FontColor LavenderBlush => new FontColor(255, 240, 245);

    // Flat UI Colors
    public static FontColor Turquoise => new FontColor(26, 188, 156);
    public static FontColor Emerald => new FontColor(46, 204, 113);
    public static FontColor PeterRiver => new FontColor(52, 152, 219);
    public static FontColor Amethyst => new FontColor(155, 89, 182);
    public static FontColor WetAsphalt => new FontColor(52, 73, 94);
    public static FontColor SunFlower => new FontColor(241, 196, 15);
    public static FontColor Carrot => new FontColor(230, 126, 34);
    public static FontColor Alizarin => new FontColor(231, 76, 60);
    public static FontColor Clouds => new FontColor(236, 240, 241);
    public static FontColor Concrete => new FontColor(149, 165, 166);

    // Pastel Colors
    public static FontColor PastelRed => new FontColor(255, 179, 186);
    public static FontColor PastelOrange => new FontColor(255, 218, 185);
    public static FontColor PastelYellow => new FontColor(255, 255, 186);
    public static FontColor PastelGreen => new FontColor(186, 255, 201);
    public static FontColor PastelBlue => new FontColor(186, 225, 255);
    public static FontColor PastelPurple => new FontColor(221, 186, 255);
    public static FontColor PastelPink => new FontColor(255, 186, 221);

    // Neon Colors
    public static FontColor NeonRed => new FontColor(255, 16, 16);
    public static FontColor NeonOrange => new FontColor(255, 153, 0);
    public static FontColor NeonYellow => new FontColor(255, 255, 51);
    public static FontColor NeonGreen => new FontColor(51, 255, 51);
    public static FontColor NeonBlue => new FontColor(51, 51, 255);
    public static FontColor NeonPurple => new FontColor(204, 51, 255);
    public static FontColor NeonPink => new FontColor(255, 51, 204);
    public static FontColor NeonCyan => new FontColor(51, 255, 255);

    // Earth Tones
    public static FontColor SaddleBrown => new FontColor(139, 69, 19);
    public static FontColor Sienna => new FontColor(160, 82, 45);
    public static FontColor Peru => new FontColor(205, 133, 63);
    public static FontColor Tan => new FontColor(210, 180, 140);
    public static FontColor RosyBrown => new FontColor(188, 143, 143);
    public static FontColor SandyBrown => new FontColor(244, 164, 96);
    public static FontColor Wheat => new FontColor(245, 222, 179);
    public static FontColor NavajoWhite => new FontColor(255, 222, 173);
    public static FontColor Bisque => new FontColor(255, 228, 196);
    public static FontColor BlanchedAlmond => new FontColor(255, 235, 205);
    public static FontColor Cornsilk => new FontColor(255, 248, 220);
}