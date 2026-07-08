using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Prowl.OrigamiUI;

namespace Prowl.Editor.Theming;

/// <summary>Static editor-background style used when the animated background is off.</summary>
public enum EditorBackgroundStyle { Nebula, Gradient, Color }

/// <summary>
/// A color ramp with a single primary color. Other stops are computed from RGB offsets.
/// Optionally, all stops can be individually overridden.
/// </summary>
public class ColorRamp
{
    /// <summary>Primary color (the ★ stop) as hex.</summary>
    public string Primary { get; set; } = "#FF00FF";

    /// <summary>When true, use Overrides[] instead of computed offsets.</summary>
    public bool OverrideAll { get; set; }

    /// <summary>Per-stop override colors as hex. Only used when OverrideAll=true.</summary>
    public string[]? Overrides { get; set; }

    /// <summary>Number of stops in this ramp.</summary>
    [JsonIgnore] public int StopCount { get; set; }

    /// <summary>Index of the primary stop (0-based).</summary>
    [JsonIgnore] public int PrimaryIndex { get; set; }

    /// <summary>RGB offsets from primary for each stop. Set by Init().</summary>
    [JsonIgnore] public (int R, int G, int B)[] Offsets { get; set; } = [];

    /// <summary>Get the resolved color for a stop index.</summary>
    public Color GetStop(int index)
    {
        if (OverrideAll && Overrides != null && index < Overrides.Length)
            return ParseHex(Overrides[index]);

        if (index == PrimaryIndex)
            return ParseHex(Primary);

        var p = ParseHex(Primary);
        if (index >= Offsets.Length) return p;
        var o = Offsets[index];
        return Color.FromArgb(255,
            Math.Clamp(p.R + o.R, 0, 255),
            Math.Clamp(p.G + o.G, 0, 255),
            Math.Clamp(p.B + o.B, 0, 255));
    }

    /// <summary>Initialize offsets from default colors. Call once at startup.</summary>
    public void Init(int stopCount, int primaryIndex, Color[] defaults)
    {
        StopCount = stopCount;
        PrimaryIndex = primaryIndex;
        var pc = defaults[primaryIndex];
        Offsets = new (int, int, int)[stopCount];
        for (int i = 0; i < stopCount; i++)
            Offsets[i] = (defaults[i].R - pc.R, defaults[i].G - pc.G, defaults[i].B - pc.B);

        // Initialize overrides array from defaults if not set
        if (Overrides == null || Overrides.Length != stopCount)
        {
            Overrides = new string[stopCount];
            for (int i = 0; i < stopCount; i++)
                Overrides[i] = ColorToHex(defaults[i]);
        }
    }

    public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color ParseHex(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.Magenta; }
    }
}

/// <summary>
/// Serializable theme data. Ramps store a single primary color + optional per-stop overrides.
/// </summary>
public class EditorThemeData
{
    public string Name { get; set; } = "Indigo";

    // Color ramps (customization overlaid onto Origami's defaults). Primary = the bright ★ C500 stop.
    public ColorRamp Neutral { get; set; } = new() { Primary = "#181830" };
    public ColorRamp Purple { get; set; } = new() { Primary = "#6366F1" };
    public ColorRamp Blue { get; set; } = new() { Primary = "#8B5CF6" };
    public ColorRamp Red { get; set; } = new() { Primary = "#FB7185" };
    public ColorRamp Green { get; set; } = new() { Primary = "#4ADE80" };
    public ColorRamp Amber { get; set; } = new() { Primary = "#FBBF24" };
    public ColorRamp Ink { get; set; } = new() { Primary = "#EAEAF7" };

    // Font
    public string DefaultFontName { get; set; } = "Geist";

    public string DefaultBoldFontName { get; set; } = "Geist";

    public float UserScale { get; set; } = 1f;

    // Sizing
    public float MenuBarHeight { get; set; } = 40f;
    public float StatusBarHeight { get; set; } = 26f;
    public float RowHeight { get; set; } = 24f;
    public float FontSize { get; set; } = 17f;
    public float LabelWidth { get; set; } = 150f;
    public float Spacing { get; set; } = 4f;
    public float Padding { get; set; } = 6f;
    // Single knob driving both the dock gutter padding and the splitter thickness.
    public float DockSpacing { get; set; } = 6f;
    public float TabBarHeight { get; set; } = 32f;
    public float TabPadding { get; set; } = 12f;
    public float Roundness { get; set; } = 6f;

    // Effects
    public bool GlassBlur { get; set; } = true;
    public float BlurAmount { get; set; } = 22f;
    public bool DropShadows { get; set; } = true;
    public bool AccentGlow { get; set; } = true;
    public bool AntiAliasing { get; set; } = true;

    // Background: animated nebula, or a static style (frozen nebula / gradient / solid colour).
    public bool AnimatedBackground { get; set; } = true;
    public float BackgroundSpeed { get; set; } = 1f;
    public EditorBackgroundStyle BackgroundStyle { get; set; } = EditorBackgroundStyle.Nebula;
    public string BackgroundColorA { get; set; } = "#1B1130";
    public string BackgroundColorB { get; set; } = "#08060C";

    // Nebula layer toggles + the raw void colour behind everything.
    public bool BgShowGradients { get; set; } = true;
    public bool BgShowStars { get; set; } = true;
    public bool BgShowComets { get; set; } = true;
    public string BackgroundVoidColor { get; set; } = "#060409";

    // Default ramp stops (RGB) = Origami's ramps. Customization is applied on top of Origami's
    // live theme, preserving each stop's alpha, so translucent glass surfaces stay glass.
    private static readonly Color[] DefaultNeutral = [H("#06060E"), H("#96A0FF"), H("#161628"), H("#0E0E1C"), H("#181830"), H("#22223E"), H("#30304E")];
    private static readonly Color[] DefaultPurple  = [H("#14153A"), H("#1E1F4D"), H("#2E3072"), H("#464B9E"), H("#6366F1"), H("#818CF8"), H("#A5B4FC")];
    private static readonly Color[] DefaultBlue    = [H("#1A0F33"), H("#241547"), H("#372066"), H("#4E2E8C"), H("#8B5CF6"), H("#A78BFA"), H("#C4B5FD")];
    private static readonly Color[] DefaultRed     = [H("#1F0E10"), H("#3A181E"), H("#5A242C"), H("#8C3442"), H("#FB7185"), H("#FC8C9C"), H("#FAAFBA")];
    private static readonly Color[] DefaultGreen   = [H("#0F1F15"), H("#162C20"), H("#1F4530"), H("#2D6446"), H("#4ADE80"), H("#78E6A0"), H("#AAF0C3")];
    private static readonly Color[] DefaultAmber   = [H("#1F1808"), H("#3A2A10"), H("#5C4017"), H("#825C28"), H("#FBBF24"), H("#FCD060"), H("#FAE0A0")];
    private static readonly Color[] DefaultInk     = [H("#494960"), H("#6A6A86"), H("#9090AB"), H("#BBBBD4"), H("#EAEAF7"), H("#FFFFFF"), H("#FFFFFF")];

    private static Color H(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>Initialize ramp offsets from the Origami defaults. Must be called after deserialization.</summary>
    public void InitRamps()
    {
        Neutral.Init(7, 4, DefaultNeutral);   // ★ = C500 (index 4)
        Purple.Init(7, 4, DefaultPurple);
        Blue.Init(7, 4, DefaultBlue);
        Red.Init(7, 4, DefaultRed);
        Green.Init(7, 4, DefaultGreen);
        Amber.Init(7, 4, DefaultAmber);
        Ink.Init(7, 4, DefaultInk);
    }

    /// <summary>Overlay this customization onto Origami's live theme: overwrite each ramp's RGB from the
    /// edited stops while keeping Origami's per-stop alpha (so translucent surfaces stay glass).</summary>
    public void ApplyTo(OrigamiTheme t)
    {
        ApplyRamp(t.Neutral, Neutral);
        ApplyRamp(t.Primary, Purple);
        ApplyRamp(t.Blue, Blue);
        ApplyRamp(t.Red, Red);
        ApplyRamp(t.Green, Green);
        ApplyRamp(t.Amber, Amber);
        ApplyRamp(t.Ink, Ink);
    }

    private static void ApplyRamp(OrigamiRamp dst, ColorRamp src)
    {
        dst.C100 = KeepAlpha(dst.C100, src.GetStop(0));
        dst.C200 = KeepAlpha(dst.C200, src.GetStop(1));
        dst.C300 = KeepAlpha(dst.C300, src.GetStop(2));
        dst.C400 = KeepAlpha(dst.C400, src.GetStop(3));
        dst.C500 = KeepAlpha(dst.C500, src.GetStop(4));
        dst.C600 = KeepAlpha(dst.C600, src.GetStop(5));
        dst.C700 = KeepAlpha(dst.C700, src.GetStop(6));
    }

    private static Color KeepAlpha(Color dst, Color rgb) => Color.FromArgb(dst.A, rgb.R, rgb.G, rgb.B);

    public EditorThemeData Clone()
    {
        var json = JsonSerializer.Serialize(this);
        var clone = JsonSerializer.Deserialize<EditorThemeData>(json)!;
        clone.InitRamps();
        return clone;
    }

    public void ExportToFile(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static EditorThemeData? ImportFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<EditorThemeData>(json);
            data?.InitRamps();
            return data;
        }
        catch { return null; }
    }

    public static EditorThemeData CreateDefault()
    {
        var d = new EditorThemeData();
        d.InitRamps();
        return d;
    }
}
