using System;
using System.Globalization;

using Prowl.OrigamiUI;
using Prowl.Scribe;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal static class Style
{
    public static OrigamiTheme Theme => Origami.Current;

    public static Color Selection => Theme.Amber.C500;
    public static Color SelectionInk => Theme.Neutral.C100;
    public static Color OverBudget => Theme.Red.C500;
    public static Color FrameBar => Theme.Primary.C500;
    public static Color Good => Theme.Green.C500;
    public static Color Bad => Theme.Red.C500;
    public static Color Snapshot => Theme.Blue.C600;
    public static Color SnapshotInk => Theme.Blue.C700;
    public static Color SnapshotFill => Theme.Blue.C300;
    public static Color Write => Theme.Amber.C400;
    public static Color Read => Theme.Primary.C500;
    public static Color Record => Theme.Red.C500;
    public static Color Coverage => Theme.Red.C600;
    public static Color RawValue => Theme.Blue.C600;
    public static Color ChannelR => Theme.Red.C500;
    public static Color ChannelG => Theme.Green.C500;
    public static Color ChannelB => Theme.Primary.C500;
    public static Color Accent => Theme.Red.C700;
    public static Color Primary => Theme.Primary.C500;

    public static FontFile Font => Theme.Font!;
    public static FontFile Mono => Theme.Mono!;
    public static FontFile Strong => Theme.SemiBold!;

    public static float Body => Theme.Metrics.FontSizeSmall;
    public static float Small => Theme.Metrics.FontSizeSmall - 1f;
    public static float Tiny => Theme.Metrics.FontSizeSmall - 2f;
    public static float Title => Theme.Metrics.FontSizeSmall + 4f;

    public static Color Ink => Theme.Ink.C500;
    public static Color InkSoft => Theme.Ink.C400;
    public static Color InkDim => Theme.Ink.C200;
    public static Color InkFaint => Theme.Ink.C100;
    public static Color ChartFill => Color.FromArgb(255, 10, 10, 14);
    public static Color Border => Theme.BorderStrong;
    public static Color CardFill => Theme.Glass;
    public static Color CardBorder => Theme.BorderSoft;

    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static OrigamiRamp KindRamp(NodeKind kind) => kind switch
    {
        NodeKind.Frame => Theme.Blue,
        NodeKind.View => Theme.Red,
        NodeKind.Pass => Theme.Amber,
        NodeKind.CommandBuffer => Theme.Primary,
        NodeKind.Program => Theme.Green,
        NodeKind.Marker => Theme.Primary,
        _ => Theme.Ink,
    };

    public static Color KindFill(NodeKind kind) => KindRamp(kind).C300;

    public static Color KindDot(NodeKind kind) => KindRamp(kind).C500;

    public static string KindLabel(NodeKind kind) => kind switch
    {
        NodeKind.Frame => "Frame",
        NodeKind.View => "View",
        NodeKind.Pass => "Render Pass",
        NodeKind.CommandBuffer => "Command Buffer (outside passes)",
        NodeKind.Program => "Program",
        NodeKind.Marker => "Renderer (marker)",
        _ => "Draw",
    };

    public static string Ms(double v) => v.ToString("0.000", CultureInfo.InvariantCulture) + " ms";
    public static string Ms2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
    public static string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
    public static string Int(double v) => Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture);
    public static string Millions(double v) => F(v / 1e6, "0.00") + " M";
    public static string Hex32(ulong v, int digits = 8) => "0x" + v.ToString("X" + digits, CultureInfo.InvariantCulture);

    public static string Bytes(double b)
    {
        double a = Math.Abs(b);
        string sign = b < 0 ? "-" : "";
        if (a >= 1073741824) return sign + F(a / 1073741824, "0.00") + " GB";
        if (a >= 1048576) return sign + F(a / 1048576, "0.0") + " MB";
        if (a >= 1024) return sign + F(a / 1024, "0.0") + " KB";
        return sign + Math.Round(a) + " B";
    }
}

internal static class Glyph
{
    public const string ChartLine = "\uf201";
    public const string Camera = "\uf030";
    public const string Rotate = "\uf2f1";
}
