using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prowl.Editor.Theming;

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
    public string Name { get; set; } = "Default Dark";

    // Color ramps
    public ColorRamp Neutral { get; set; } = new() { Primary = "#1D1E22" };
    public ColorRamp Purple { get; set; } = new() { Primary = "#563784" };
    public ColorRamp Blue { get; set; } = new() { Primary = "#82AAC6" };
    public ColorRamp Red { get; set; } = new() { Primary = "#CB594F" };
    public ColorRamp Ink { get; set; } = new() { Primary = "#6C6A7A" };

    // Font
    public string DefaultFontName { get; set; } = "bahnschrift";

    public string DefaultBoldFontName { get; set; } = "bahnschrift";

    public float UserScale { get; set; } = 1f;

    // Sizing
    public float MenuBarHeight { get; set; } = 26f;
    public float RowHeight { get; set; } = 22f;
    public float FontSize { get; set; } = 17f;
    public float LabelWidth { get; set; } = 120f;
    public float Spacing { get; set; } = 2f;
    public float Padding { get; set; } = 4f;
    public float SplitterSize { get; set; } = 14f;
    public float DockPadding { get; set; } = 14f;
    public float TabBarHeight { get; set; } = 26f;
    public float TabPadding { get; set; } = 12f;
    public float Roundness { get; set; } = 8f;

    // Default ramp colors for computing offsets
    private static readonly Color[] DefaultNeutral = [H("#101116"), H("#16151A"), H("#18191D"), H("#1D1E22"), H("#2E2D35")];
    private static readonly Color[] DefaultPurple = [H("#1D1010"), H("#271D36"), H("#3D2660"), H("#563784"), H("#7252AA"), H("#A886D8"), H("#D4B8F4")];
    private static readonly Color[] DefaultBlue = [H("#0D1A24"), H("#1D3044"), H("#2E5470"), H("#82AAC6"), H("#AECADD"), H("#CDDEED"), H("#E8F2F9")];
    private static readonly Color[] DefaultRed = [H("#1C0E0E"), H("#361818"), H("#7A3030"), H("#CB594F"), H("#E68880"), H("#F2B0AB"), H("#FDE0DE")];
    private static readonly Color[] DefaultInk = [H("#2E2D35"), H("#3E3D47"), H("#6C6A7A"), H("#B0ADBE"), H("#F0EEF8")];

    private static Color H(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>Initialize ramp offsets from defaults. Must be called after deserialization.</summary>
    public void InitRamps()
    {
        Neutral.Init(5, 3, DefaultNeutral);   // ★ = 400 (index 3)
        Purple.Init(7, 3, DefaultPurple);     // ★ = 400 (index 3)
        Blue.Init(7, 3, DefaultBlue);         // ★ = 400 (index 3)
        Red.Init(7, 3, DefaultRed);           // ★ = 400 (index 3)
        Ink.Init(5, 2, DefaultInk);           // ★ = 300 (index 2)
    }

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
